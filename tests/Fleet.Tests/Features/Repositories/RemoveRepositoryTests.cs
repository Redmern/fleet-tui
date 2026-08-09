using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.AddRepository.Models;
using Fleet.Features.Repositories.RemoveRepository;
using Fleet.Platform.Git;

namespace Fleet.Tests.Features.Repositories;

public sealed class RemoveRepositoryTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public RemoveRepositoryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (!Directory.Exists(_root))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(file);

                if (attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task<string> RepositoryAsync()
    {
        var created = await new AddRepositoryHandler(new GitRunner())
            .HandleAsync(AddRepositoryCommand.CreateNew(_root, "backend", "main"));

        Assert.True(created.Succeeded, created.Error);

        return created.Value!.Path;
    }

    [Fact]
    public async Task Removing_a_repository_deletes_it_from_disk()
    {
        var directory = await RepositoryAsync();

        var result = new RemoveRepositoryHandler(new GitRunner()).Handle(directory);

        Assert.True(result.Succeeded, result.Error);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task Git_objects_marked_read_only_do_not_stop_the_delete()
    {
        var directory = await RepositoryAsync();

        var objects = Directory
            .EnumerateFiles(Path.Combine(directory, ".git"), "*", SearchOption.AllDirectories)
            .ToList();

        Assert.NotEmpty(objects);

        var result = new RemoveRepositoryHandler(new GitRunner()).Handle(directory);

        Assert.True(result.Succeeded, result.Error);
    }

    [Fact]
    public async Task The_worktrees_that_would_go_are_listed_before_anything_happens()
    {
        var directory = await RepositoryAsync();

        var state = await new RemoveRepositoryHandler(new GitRunner()).InspectAsync(directory);

        Assert.True(state.Exists);
        Assert.Contains(state.Worktrees, w => w.Replace('\\', '/').EndsWith("/main"));
    }

    [Fact]
    public async Task A_branch_with_no_upstream_counts_as_unpushed()
    {
        var directory = await RepositoryAsync();

        var state = await new RemoveRepositoryHandler(new GitRunner()).InspectAsync(directory);

        Assert.Contains("main", state.Unpushed);
    }

    [Fact]
    public void A_repository_that_is_already_gone_is_not_an_error()
    {
        var result = new RemoveRepositoryHandler(new GitRunner())
            .Handle(Path.Combine(_root, "never-existed"));

        Assert.True(result.Succeeded, result.Error);
    }

    [Fact]
    public async Task Only_the_named_repository_goes()
    {
        var first = await RepositoryAsync();

        var second = await new AddRepositoryHandler(new GitRunner())
            .HandleAsync(AddRepositoryCommand.CreateNew(_root, "frontend", "main"));

        new RemoveRepositoryHandler(new GitRunner()).Handle(first);

        Assert.False(Directory.Exists(first));
        Assert.True(Directory.Exists(second.Value!.Path));
    }
}
