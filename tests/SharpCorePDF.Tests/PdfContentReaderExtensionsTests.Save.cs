using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Domain.ReadModels;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the conveniences that write a payload to a file.
/// </summary>
/// <remarks>
/// What these pin is the file: it is created where the caller named it, the directory a path names is created
/// when it is not there, a file that is already there is replaced rather than appended to, and a call that is
/// refused leaves nothing behind. The payload and the stub come from the other part of this class, so the bytes
/// compared here are the bytes those tests compare — a file is simply the destination this time.
/// </remarks>
public sealed partial class PdfContentReaderExtensionsTests
{
    /// <summary>An image is written where the caller named it, and comes back byte for byte.</summary>
    [Fact]
    public async Task Saves_an_image_to_a_file()
    {
        using var folder = TestAssets.NewTemporaryFolder("save-image");
        var reader = new RecordingContentReader();
        var destination = folder.File("image.bin");

        await reader.SaveImageDataAsync(
            Source,
            new PdfImageInfo { Id = "img-1", ByteLength = PayloadLength },
            destination,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(Payload, await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
        Assert.Null(reader.LastBuffer);
    }

    /// <summary>
    /// A destination whose directory does not exist yet is created, so a caller may name a path instead of
    /// preparing one.
    /// </summary>
    [Fact]
    public async Task Creates_the_directory_an_image_destination_names()
    {
        using var folder = TestAssets.NewTemporaryFolder("save-image-nested");
        var reader = new RecordingContentReader();
        var destination = Path.Combine(folder.Path, "one", "two", "image.bin");

        await reader.SaveImageDataAsync(
            Source,
            new PdfImageInfo { Id = "img-1", ByteLength = PayloadLength },
            destination,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(Payload, await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
    }

    /// <summary>A file that is already there is replaced, not appended to.</summary>
    [Fact]
    public async Task Truncates_a_file_that_is_already_there()
    {
        using var folder = TestAssets.NewTemporaryFolder("save-image-over-longer");
        var reader = new RecordingContentReader();
        var destination = folder.File("image.bin");

        await File.WriteAllBytesAsync(
            destination,
            new byte[PayloadLength + 1_000],
            TestContext.Current.CancellationToken);

        await reader.SaveImageDataAsync(
            Source,
            new PdfImageInfo { Id = "img-1", ByteLength = PayloadLength },
            destination,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(Payload, await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
    }

    /// <summary>An embedded file is written on the same terms as an image.</summary>
    [Fact]
    public async Task Saves_an_embedded_file_to_a_file()
    {
        using var folder = TestAssets.NewTemporaryFolder("save-file");
        var reader = new RecordingContentReader();
        var destination = Path.Combine(folder.Path, "attachments", "data.bin");

        await reader.SaveEmbeddedFileAsync(
            Source,
            new PdfEmbeddedFileInfo { Id = "file-1", Name = "data.bin", ByteLength = PayloadLength },
            destination,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(Payload, await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A member handed nothing — or a path that is not a path — says so rather than opening a file somewhere
    /// the caller did not mean, and leaves nothing behind.
    /// </summary>
    /// <remarks>
    /// The whitespace path is asserted down to <see cref="ArgumentException.ParamName"/>, because the type
    /// alone would not tell the guard apart: <see cref="Path.GetFullPath(string)"/> refuses a whitespace path
    /// with an <see cref="ArgumentException"/> of its own. What the guard adds is the name of the caller's
    /// argument, and that the call is refused before the file system is touched.
    /// </remarks>
    [Fact]
    public async Task Refuses_a_missing_argument_before_a_file_is_opened()
    {
        using var folder = TestAssets.NewTemporaryFolder("save-refuses");
        var reader = new RecordingContentReader();
        var token = TestContext.Current.CancellationToken;
        var destination = folder.File("image.bin");
        var image = new PdfImageInfo { Id = "img-1", ByteLength = PayloadLength };
        var file = new PdfEmbeddedFileInfo { Id = "file-1", Name = "data.bin", ByteLength = PayloadLength };

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => reader.SaveImageDataAsync(null!, image, destination, cancellationToken: token));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => reader.SaveImageDataAsync(Source, null!, destination, cancellationToken: token));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => reader.SaveImageDataAsync(Source, image, null!, cancellationToken: token));
        await Assert.ThrowsAsync<ArgumentException>(
            () => reader.SaveImageDataAsync(Source, image, string.Empty, cancellationToken: token));

        var refusal = await Assert.ThrowsAsync<ArgumentException>(
            () => reader.SaveImageDataAsync(Source, image, "   ", cancellationToken: token));

        Assert.Equal("destinationPath", refusal.ParamName);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => reader.SaveEmbeddedFileAsync(null!, file, destination, cancellationToken: token));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => reader.SaveEmbeddedFileAsync(Source, null!, destination, cancellationToken: token));
        await Assert.ThrowsAsync<ArgumentException>(
            () => reader.SaveEmbeddedFileAsync(Source, file, string.Empty, cancellationToken: token));

        Assert.False(File.Exists(destination), "a refused call left a file behind");
        Assert.Null(reader.LastBuffer);
    }
}
