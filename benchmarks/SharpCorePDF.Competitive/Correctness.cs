using System.Text.Json;

namespace SharpCorePDF.Competitive;

internal sealed record ExpectedLetter(int PageNumber, string Text, Box Bounds);
internal sealed record ExpectedWord(int PageNumber, string Text, Box Bounds, int Order);
internal sealed record LabelledDocument(
    string Name,
    byte[] Content,
    IReadOnlyList<ExpectedLetter> Letters,
    IReadOnlyList<ExpectedWord> Words);

internal sealed record CorrectnessResult(
    string Engine,
    string Document,
    string Status,
    int ExpectedCharacters,
    int MatchedCharacters,
    double? CharacterRecall,
    double? BoxAgreement,
    double? BoxOverlap,
    int ExpectedWords,
    int ActualWords,
    int MatchedWords,
    double? WordPrecision,
    double? WordRecall,
    double? WordF1,
    double? ReadingOrderAgreement,
    int WarningCount,
    string? Error);

internal static partial class CorrectnessRunner
{
    public static async Task<IReadOnlyList<CorrectnessResult>> RunAsync(
        IEnumerable<EngineAdapter> adapters,
        IReadOnlyList<LabelledDocument> documents,
        CancellationToken cancellationToken)
    {
        var results = new List<CorrectnessResult>();
        foreach (var document in documents)
        {
            foreach (var adapter in adapters.Where(adapter => adapter.SupportsExtraction))
            {
                try
                {
                    var observed = await adapter.ExtractAsync(document.Content, PageSelection.All, cancellationToken);
                    results.Add(Score(adapter.Name, document, observed));
                }
                catch (Exception exception)
                {
                    results.Add(Error(adapter.Name, document, exception));
                }
            }
        }

        return results;
    }

    public static IReadOnlyList<LabelledDocument> LoadLabels(string repositoryRoot)
    {
        var folder = Path.Combine(
            repositoryRoot, "tests", "SharpCorePDF.Tests", "Assets", "parity", "labelled");
        var documents = new List<LabelledDocument>();
        foreach (var labelPath in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(labelPath);
            using var json = JsonDocument.Parse(File.ReadAllBytes(labelPath));
            var letters = new List<ExpectedLetter>();
            var words = new List<ExpectedWord>();
            var pageNumber = 0;
            var wordOrder = 0;

            foreach (var page in json.RootElement.GetProperty("pages").EnumerateArray())
            {
                pageNumber++;
                foreach (var block in page.GetProperty("blocks").EnumerateArray())
                {
                    var current = new List<(string Text, Box Bounds)>();
                    foreach (var character in block.GetProperty("characters").EnumerateArray())
                    {
                        var text = character.GetProperty("text").GetString() ?? string.Empty;
                        var bounds = ReadBox(character.GetProperty("box"));
                        if (text.All(char.IsWhiteSpace))
                        {
                            FlushWord(pageNumber, current, words, ref wordOrder);
                            continue;
                        }

                        current.Add((text, bounds));
                        letters.Add(new ExpectedLetter(pageNumber, text, bounds));
                    }

                    FlushWord(pageNumber, current, words, ref wordOrder);
                }
            }

            documents.Add(new LabelledDocument(
                name,
                File.ReadAllBytes(Path.Combine(folder, name + ".pdf")),
                letters,
                words));
        }

        return documents;
    }

    private static void FlushWord(
        int pageNumber,
        List<(string Text, Box Bounds)> current,
        List<ExpectedWord> words,
        ref int wordOrder)
    {
        if (current.Count == 0)
        {
            return;
        }

        words.Add(new ExpectedWord(
            pageNumber,
            string.Concat(current.Select(character => character.Text)),
            Union(current.Select(character => character.Bounds)),
            wordOrder++));
        current.Clear();
    }

    private static Box ReadBox(JsonElement element)
    {
        var values = element.EnumerateArray().Select(value => value.GetDouble()).ToArray();
        return new Box(values[0], values[1], values[2], values[3]);
    }

    private static Box Union(IEnumerable<Box> boxes)
    {
        var values = boxes.ToArray();
        return new Box(
            values.Min(box => box.Left),
            values.Min(box => box.Bottom),
            values.Max(box => box.Right),
            values.Max(box => box.Top));
    }
}
