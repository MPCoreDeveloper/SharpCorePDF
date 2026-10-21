using System.Text;
using SharpCorePDF.Application.Extensions;
using SharpCorePDF.Domain.ValueObjects;

namespace SharpCorePDF.Tests;

/// <summary>
/// Tests of the XMP packet and of embedded files: the packet byte-exact and parsed, the attachments
/// with distinct identifiers, and the copy route that keeps a large attachment out of memory.
/// </summary>
public sealed partial class PdfContentReaderTests
{
    /// <summary>The raw packet is returned exactly as stored, with its values parsed beside it.</summary>
    [Fact]
    public async Task Returns_the_packet_byte_exact_and_its_values()
    {
        var source = Source(PdfContentFixtures.Metadata(), "metadata.pdf");

        var result = await Reader().ReadMetadataAsync(source, cancellationToken: Token);

        Assert.NotNull(result.Metadata);
        Assert.Equal(Encoding.UTF8.GetBytes(PdfContentFixtures.XmpText), result.Metadata.RawPacket.ToArray());
        Assert.Equal("Sample title", result.Metadata.Properties["dc:title"]);
        Assert.Equal("Ada, Grace", result.Metadata.Properties["dc:creator"]);
        Assert.Equal("SharpCorePDF tests", result.Metadata.Producer);
        Assert.Equal("Unit test", result.Metadata.CreatorTool);
    }

    /// <summary>A document without a packet reports no packet at all, not an empty one.</summary>
    [Fact]
    public async Task Returns_null_metadata_for_a_document_without_a_packet()
    {
        var source = Source(PdfContentFixtures.WithoutMetadata(), "plain.pdf");

        var result = await Reader().ReadMetadataAsync(source, cancellationToken: Token);

        Assert.Null(result.Metadata);
        Assert.Empty(result.Warnings);
    }

    /// <summary>A packet that is not XML is reported, and its bytes are still returned.</summary>
    [Fact]
    public async Task Reports_a_packet_that_is_not_xml_and_keeps_the_bytes()
    {
        var source = Source(PdfContentFixtures.Metadata(wellFormed: false), "broken.pdf");

        var result = await Reader().ReadMetadataAsync(source, cancellationToken: Token);

        Assert.NotNull(result.Metadata);
        Assert.Empty(result.Metadata.Properties);
        Assert.Equal("<x:xmpmeta><rdf:RDF", Encoding.UTF8.GetString(result.Metadata.RawPacket.Span));
        AssertWarning(result.Warnings, PdfWarningKind.XmpPacketMalformed);
    }

    /// <summary>Every embedded file is returned, and two entries of one name get distinct identifiers.</summary>
    [Fact]
    public async Task Returns_every_embedded_file_with_a_distinct_identifier()
    {
        var source = Source(PdfContentFixtures.Metadata(), "metadata.pdf");

        var result = await Reader().ReadEmbeddedFilesAsync(source, cancellationToken: Token);

        Assert.Equal(3, result.Count);
        Assert.Equal(["a.txt", "a.txt#2", "b.bin"], result.Items.Select(file => file.Id));
        Assert.Equal(["a.txt", "a.txt", "b.bin"], result.Items.Select(file => file.Name));
        AssertWarning(result.Warnings, PdfWarningKind.EmbeddedFileNameCollision);
    }

    /// <summary>The bytes of an embedded file are its stream, decoded, exactly.</summary>
    [Fact]
    public async Task Extracts_an_embedded_file_byte_exact()
    {
        var source = Source(PdfContentFixtures.Metadata(), "metadata.pdf");

        var result = await Reader().ReadEmbeddedFilesAsync(source, cancellationToken: Token);

        Assert.Equal("text/plain", result.Items[0].MimeType);
        Assert.Equal("Data", result.Items[0].Relationship);
        Assert.Equal(5, result.Items[0].ByteLength);
        Assert.Equal("hello", Encoding.Latin1.GetString(result.Items[0].Data!.Value.Span));
        Assert.Equal("second", Encoding.Latin1.GetString(result.Items[1].Data!.Value.Span));
        Assert.Equal(PdfContentFixtures.EmbeddedBinary, result.Items[2].Data!.Value.ToArray());
        Assert.Equal(2024, result.Items[0].ModificationDate?.Year);
    }

    /// <summary>The copy route writes the same bytes a materialised read returns.</summary>
    [Fact]
    public async Task Copies_an_embedded_file_through_the_copy_route()
    {
        var source = Source(PdfContentFixtures.Metadata(), "metadata.pdf");
        var reader = Reader();
        var result = await reader.ReadEmbeddedFilesAsync(source, cancellationToken: Token);

        using var destination = new MemoryStream();

        await reader.CopyEmbeddedFileDataAsync(source, result.Items[2], destination, cancellationToken: Token);

        Assert.Equal(PdfContentFixtures.EmbeddedBinary, destination.ToArray());
        Assert.True(destination.CanWrite);

        var materialised = await reader.ReadEmbeddedFileDataAsync(source, result.Items[1], cancellationToken: Token);

        Assert.Equal("second", Encoding.Latin1.GetString(materialised.Span));
    }

    /// <summary>A document without attachments yields an empty result rather than an exception.</summary>
    [Fact]
    public async Task Returns_an_empty_result_for_a_document_without_embedded_files()
    {
        var source = Source(PdfContentFixtures.WithoutMetadata(), "plain.pdf");

        var result = await Reader().ReadEmbeddedFilesAsync(source, cancellationToken: Token);

        Assert.Empty(result.Items);
    }
}
