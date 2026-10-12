using System.Text.Json;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Tests.Platform.Mux.Embedded;

public class ClipboardImageWireTests
{
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 1, 2, 3];

    [Fact]
    public async Task A_clipboard_image_request_and_its_png_answer_survive_the_round_trip()
    {
        var buffer = new MemoryStream();
        var writer = new Wire(buffer);

        await writer.SendAsync(
            MessageType.ClipboardImageRequest,
            new ClipboardImageRequest { Id = 3, Pane = "p7" },
            WireJsonContext.Default.ClipboardImageRequest);
        await writer.SendAsync(
            MessageType.ClipboardImageResponse,
            ClipboardImageResponse.For(3, Png),
            WireJsonContext.Default.ClipboardImageResponse);

        buffer.Position = 0;
        var reader = new Wire(buffer);
        var request = (await reader.ReceiveAsync())!.Value;
        var response = (await reader.ReceiveAsync())!.Value;

        Assert.Equal(MessageType.ClipboardImageRequest, request.Type);
        Assert.Equal("p7", Wire.Read(request.Payload, WireJsonContext.Default.ClipboardImageRequest).Pane);
        Assert.Equal(MessageType.ClipboardImageResponse, response.Type);
        var answer = Wire.Read(response.Payload, WireJsonContext.Default.ClipboardImageResponse);
        Assert.Equal(3, answer.Id);
        Assert.Equal(ClipboardImageResponse.Png, answer.Mime);
        Assert.Equal(Png, answer.Data);
        Assert.Null(answer.Error);
    }

    [Fact]
    public void An_empty_clipboard_answers_with_no_data_and_no_error()
    {
        var answer = ClipboardImageResponse.For(4, null);

        Assert.Null(answer.Data);
        Assert.Null(answer.Error);
    }

    [Fact]
    public void An_image_over_the_limit_is_refused_with_a_clear_error_instead_of_its_bytes()
    {
        var answer = ClipboardImageResponse.For(5, new byte[ClipboardImageResponse.MaxBytes + 1]);

        Assert.Null(answer.Data);
        Assert.NotNull(answer.Error);
        Assert.Contains("too large", answer.Error);
        Assert.Contains("MiB", answer.Error);
    }

    [Fact]
    public void The_largest_allowed_image_still_fits_in_one_wire_message()
    {
        var answer = ClipboardImageResponse.For(int.MaxValue, new byte[ClipboardImageResponse.MaxBytes]);

        Assert.NotNull(answer.Data);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(answer, WireJsonContext.Default.ClipboardImageResponse).Length < Wire.MaxMessage);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(
            new ControlResponse { Id = int.MaxValue, Ok = true, Mime = answer.Mime, Data = answer.Data },
            WireJsonContext.Default.ControlResponse).Length < Wire.MaxMessage);
    }
}
