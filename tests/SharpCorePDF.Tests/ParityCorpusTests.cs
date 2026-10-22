using System.Security.Cryptography;
using System.Text.Json;
using SharpCorePDF.Infrastructure;
using SharpCorePDF.Infrastructure.Pdf;

namespace SharpCorePDF.Tests;

/// <summary>
/// Checks the parity corpus as evidence: every manifest entry exists, its bytes are the bytes the
/// generator recorded, every PDF opens, and the labelled and refusal subsets are present.
/// </summary>
/// <remarks>
/// The manifest hash is the in-test equivalent of regenerating the corpus and diffing the tree. The
/// generator is deterministic (fixed dates, identifiers and image pixels); a changed file without a
/// regenerated manifest therefore fails here rather than being discovered when a later phase reads it.
/// </remarks>
public sealed class ParityCorpusTests
{
    private static string Root => Path.Combine(TestAssets.Folder, "parity");

    private static string ManifestPath => Path.Combine(Root, "manifest.json");

    private static JsonElement Manifest()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ManifestPath));
        return document.RootElement.Clone();
    }

    private static JsonElement.ArrayEnumerator Files() =>
        Manifest().GetProperty("files").EnumerateArray();

    [Fact]
    public void Declares_at_least_fifty_born_digital_documents()
    {
        var count = Files().Count(file => file.GetProperty("kind").GetString() == "corpus");

        Assert.True(count >= 50, $"The corpus has {count} born-digital documents; at least 50 are required.");
    }

    [Fact]
    public void Every_manifest_entry_exists_and_matches_its_hash()
    {
        foreach (var file in Files())
        {
            var relative = file.GetProperty("path").GetString()!;
            var path = Path.Combine(Root, relative);
            var expected = file.GetProperty("sha256").GetString()!;

            Assert.True(File.Exists(path), $"The manifest names a missing file: {relative}.");

            var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            Assert.Equal(expected, actual);
            Assert.Equal(file.GetProperty("bytes").GetInt64(), new FileInfo(path).Length);
        }
    }

    [Fact]
    public void Every_manifest_entry_names_its_source_and_licence()
    {
        foreach (var file in Files())
        {
            Assert.False(string.IsNullOrWhiteSpace(file.GetProperty("source").GetString()));
            Assert.Equal("MIT", file.GetProperty("licence").GetString());
        }
    }

    [Fact]
    public void No_stray_files_sit_outside_the_manifest()
    {
        var declared = Files()
            .Select(file => file.GetProperty("path").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var actual = Directory
            .EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Root, path).Replace('\\', '/'))
            .Where(path => !string.Equals(path, "manifest.json", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(actual.Order(StringComparer.Ordinal), declared.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_pdf_in_the_manifest_opens_with_the_reader()
    {
        foreach (var file in Files().Where(file => file.GetProperty("path").GetString()!.EndsWith(".pdf", StringComparison.Ordinal)))
        {
            var relative = file.GetProperty("path").GetString()!;
            var bytes = File.ReadAllBytes(Path.Combine(Root, relative));
            var reader = new PdfFileReader(bytes, relative, PdfEngineOptions.Default);

            Assert.True(reader.Pages.Count > 0, $"{relative} opened with no pages.");

            if (file.TryGetProperty("pages", out var pages))
            {
                Assert.Equal(pages.GetInt32(), reader.Pages.Count);
            }
        }
    }

    [Fact]
    public void Labelled_documents_have_ordered_text_and_character_boxes()
    {
        var labelled = Files().Where(file => file.GetProperty("kind").GetString() == "labelled").ToArray();

        Assert.Equal(5, labelled.Length);

        foreach (var document in labelled)
        {
            var labels = Path.Combine(Root, document.GetProperty("labels").GetString()!);
            var expected = ReadJson(labels);
            var blocks = expected.GetProperty("pages")[0].GetProperty("blocks").EnumerateArray().ToArray();

            Assert.NotEmpty(blocks);
            Assert.Equal(
                Enumerable.Range(1, blocks.Length),
                blocks.Select(block => block.GetProperty("order").GetInt32()));

            foreach (var block in blocks)
            {
                Assert.False(string.IsNullOrWhiteSpace(block.GetProperty("text").GetString()));
                Assert.Equal(4, block.GetProperty("box").GetArrayLength());

                var characters = block.GetProperty("characters").EnumerateArray().ToArray();
                Assert.Equal(block.GetProperty("text").GetString()!.Length, characters.Length);
                Assert.All(characters, character => Assert.Equal(4, character.GetProperty("box").GetArrayLength()));
            }
        }
    }

    [Fact]
    public void Image_folder_covers_supported_and_refused_variants()
    {
        var images = Files().Where(file => file.GetProperty("kind").GetString() == "image").ToArray();
        var features = images
            .SelectMany(file => file.GetProperty("features").EnumerateArray())
            .Select(value => value.GetString())
            .ToHashSet();

        Assert.Contains("png", features);
        Assert.Contains("jpeg", features);
        Assert.Contains("bmp", features);
        Assert.Contains("gif", features);
        Assert.Contains("refused", features);
        Assert.Contains(images, file => file.GetProperty("path").GetString() == "images/unknown-signature.bin");
    }

    [Fact]
    public void Form_annotation_signature_and_pdfa_folders_are_populated()
    {
        var kinds = Files().Select(file => file.GetProperty("kind").GetString()).ToHashSet();

        Assert.Contains("form", kinds);
        Assert.Contains("annotation", kinds);
        Assert.Contains("signature", kinds);
        Assert.Contains("pdfa", kinds);
    }

    private static JsonElement ReadJson(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }
}
