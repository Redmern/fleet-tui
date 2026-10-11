using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Fleet.Platform.Jira.Models;
using Fleet.Ports.Jira;
using Fleet.Shared.Constants;
using Fleet.Shared.Results;
using Fleet.Shared.Settings.Models;
using Fleet.Shared.Todos.Models;

namespace Fleet.Platform.Jira;

public sealed class JiraRestClient : IJiraClient, IDisposable
{
    public static readonly TimeSpan AnswerWithin = TimeSpan.FromSeconds(20);

    public static readonly TimeSpan PullWithin = TimeSpan.FromMinutes(2);

    private const int PageSize = 100;

    private const int MaxPages = 50;

    private const string Unreadable = "Jira sent an answer fleet could not read.";

    private readonly HttpClient _http;

    public JiraRestClient(HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = AnswerWithin;
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("fleet", FleetVersion.Current));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<Result<IReadOnlyList<JiraIssue>>> SearchAsync(
        JiraSettings settings, string token, CancellationToken ct = default)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.Email + ":" + token));
        var issues = new List<JiraIssue>();
        string? next = null;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(PullWithin);

        try
        {
            for (var page = 0; page < MaxPages; page++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, SearchUri(settings, next));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

                using var response = await _http.SendAsync(request, deadline.Token).ConfigureAwait(false);

                var body = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);

                if (Refused(response, body, settings) is { } refused)
                {
                    return Result<IReadOnlyList<JiraIssue>>.Fail(refused);
                }

                if (JsonSerializer.Deserialize(body, JiraJsonContext.Default.JiraSearchJson) is not { } found)
                {
                    return Result<IReadOnlyList<JiraIssue>>.Fail(Unreadable);
                }

                issues.AddRange(found.Issues.Where(i => i.Key.Length > 0).Select(i => Issue(i, settings.StoryPointsField)));

                next = found.NextPageToken;

                if (found.IsLast == true || string.IsNullOrEmpty(next))
                {
                    return Result<IReadOnlyList<JiraIssue>>.Ok(issues);
                }
            }

            return Result<IReadOnlyList<JiraIssue>>.Fail(
                $"the Jira query matches more than {MaxPages * PageSize} issues; narrow the jql in the project settings.");
        }
        catch (HttpRequestException e)
        {
            return Result<IReadOnlyList<JiraIssue>>.Fail($"Jira is unreachable at {settings.BaseUrl}: {e.Message}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result<IReadOnlyList<JiraIssue>>.Fail($"Jira at {settings.BaseUrl} did not answer in time.");
        }
        catch (JsonException)
        {
            return Result<IReadOnlyList<JiraIssue>>.Fail(Unreadable);
        }
    }

    public void Dispose() => _http.Dispose();

    private static Uri SearchUri(JiraSettings settings, string? next)
    {
        var fields = "summary,description,status," + settings.StoryPointsField;
        var query = new StringBuilder()
            .Append("jql=").Append(Uri.EscapeDataString(settings.Jql))
            .Append("&fields=").Append(Uri.EscapeDataString(fields))
            .Append("&maxResults=").Append(PageSize);

        if (!string.IsNullOrEmpty(next))
        {
            query.Append("&nextPageToken=").Append(Uri.EscapeDataString(next));
        }

        return new Uri(settings.BaseUrl.TrimEnd('/') + "/rest/api/3/search/jql?" + query);
    }

    private static string? Refused(HttpResponseMessage response, string body, JiraSettings settings)
    {
        var code = (int)response.StatusCode;

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden || LoginFailed(response))
        {
            return $"Jira rejected the credentials for {settings.Email} (HTTP {code}); check the email and API token.";
        }

        if (response.IsSuccessStatusCode)
        {
            return null;
        }

        var explained = Explained(body);

        return explained.Length == 0 ? $"Jira answered HTTP {code}." : $"Jira answered HTTP {code}: {explained}";
    }

    private static bool LoginFailed(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Seraph-LoginReason", out var reasons)
        && reasons.Any(r => r.Contains("FAILED", StringComparison.OrdinalIgnoreCase)
            || r.Contains("DENIED", StringComparison.OrdinalIgnoreCase));

    private static string Explained(string body)
    {
        try
        {
            if (JsonSerializer.Deserialize(body, JiraJsonContext.Default.JiraErrorJson) is not { } error)
            {
                return string.Empty;
            }

            return string.Join(' ', error.ErrorMessages.Concat(error.Errors.Select(e => $"{e.Key}: {e.Value}")));
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static JiraIssue Issue(JiraIssueJson issue, string storyPointsField)
    {
        var fields = issue.Fields ?? new JiraFieldsJson();

        return new JiraIssue(
            issue.Key,
            fields.Summary ?? string.Empty,
            AdfText.From(fields.Description),
            Points(fields, storyPointsField),
            string.Equals(fields.Status?.StatusCategory?.Key, "done", StringComparison.OrdinalIgnoreCase));
    }

    private static double? Points(JiraFieldsJson fields, string storyPointsField) =>
        fields.Other.TryGetValue(storyPointsField, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var points)
            ? points
            : null;
}
