namespace SharpCorePDF.Competitive;

internal static partial class CorrectnessRunner
{
    private const double BoxTolerance = 0.5d;

    private sealed record ActualWord(WordObservation Value, int Index, string Token);
    private sealed record ActualLetter(LetterObservation Value, int WordIndex);

    private static CorrectnessResult Score(
        string engine,
        LabelledDocument document,
        ExtractionObservation observed)
    {
        var actualWords = observed.Words
            .Select((word, index) => new ActualWord(word, index, Token(word.Text)))
            .Where(item => item.Token.Length > 0)
            .ToArray();
        var (matchedCharacters, matchingBoxes, overlap) = MatchCharacters(document.Letters, actualWords);
        var matchedOrder = MatchWords(document.Words, actualWords);
        var matchedWords = matchedOrder.Count;
        var precision = actualWords.Length == 0 ? 0d : (double)matchedWords / actualWords.Length;
        var recall = document.Words.Count == 0 ? 1d : (double)matchedWords / document.Words.Count;
        var f1 = precision + recall == 0d ? 0d : (2d * precision * recall) / (precision + recall);
        var concordant = 0;
        var pairs = 0;

        for (var first = 0; first < matchedOrder.Count; first++)
        {
            for (var second = first + 1; second < matchedOrder.Count; second++)
            {
                if (matchedOrder[first].Expected < matchedOrder[second].Expected)
                {
                    pairs++;
                    if (matchedOrder[first].Actual < matchedOrder[second].Actual)
                    {
                        concordant++;
                    }
                }
            }
        }

        return new CorrectnessResult(
            engine,
            document.Name,
            "Completed",
            document.Letters.Count,
            matchedCharacters,
            document.Letters.Count == 0 ? 1d : (double)matchedCharacters / document.Letters.Count,
            matchedCharacters == 0 ? 0d : (double)matchingBoxes / matchedCharacters,
            matchedCharacters == 0 ? 0d : overlap / matchedCharacters,
            document.Words.Count,
            actualWords.Length,
            matchedWords,
            precision,
            recall,
            f1,
            pairs == 0 ? null : (double)concordant / pairs,
            observed.WarningCount,
            null);
    }

    private static (int Matched, int Boxes, double Overlap) MatchCharacters(
        IReadOnlyList<ExpectedLetter> expectedLetters,
        IReadOnlyList<ActualWord> actualWords)
    {
        var actualLetters = actualWords
            .SelectMany(word => word.Value.Letters.Select(letter => new ActualLetter(letter, word.Index)))
            .Where(item => !item.Value.Text.All(char.IsWhiteSpace))
            .ToArray();
        var used = new bool[actualLetters.Length];
        var matched = 0;
        var boxes = 0;
        var overlap = 0d;

        foreach (var expected in expectedLetters)
        {
            var best = -1;
            var bestDistance = double.PositiveInfinity;
            for (var index = 0; index < actualLetters.Length; index++)
            {
                if (used[index]
                    || actualLetters[index].Value.PageNumber != expected.PageNumber
                    || !string.Equals(actualLetters[index].Value.Text, expected.Text, StringComparison.Ordinal))
                {
                    continue;
                }

                var distance = Distance(actualLetters[index].Value.Bounds, expected.Bounds);
                if (distance < bestDistance)
                {
                    best = index;
                    bestDistance = distance;
                }
            }

            if (best < 0)
            {
                continue;
            }

            used[best] = true;
            matched++;
            if (actualLetters[best].Value.Bounds.IsWithin(expected.Bounds, BoxTolerance))
            {
                boxes++;
            }

            overlap += actualLetters[best].Value.Bounds.IntersectionOverUnion(expected.Bounds);
        }

        return (matched, boxes, overlap);
    }

    private static List<(int Expected, int Actual)> MatchWords(
        IReadOnlyList<ExpectedWord> expectedWords,
        IReadOnlyList<ActualWord> actualWords)
    {
        var used = new bool[actualWords.Count];
        var matched = new List<(int Expected, int Actual)>();
        foreach (var expected in expectedWords.OrderBy(word => word.Order))
        {
            var best = -1;
            var bestDistance = double.PositiveInfinity;
            for (var index = 0; index < actualWords.Count; index++)
            {
                if (used[index]
                    || actualWords[index].Value.PageNumber != expected.PageNumber
                    || !string.Equals(actualWords[index].Token, Token(expected.Text), StringComparison.Ordinal))
                {
                    continue;
                }

                var distance = Distance(actualWords[index].Value.Bounds, expected.Bounds);
                if (distance < bestDistance)
                {
                    best = index;
                    bestDistance = distance;
                }
            }

            if (best >= 0)
            {
                used[best] = true;
                matched.Add((expected.Order, actualWords[best].Index));
            }
        }

        return matched;
    }

    private static CorrectnessResult Error(string engine, LabelledDocument document, Exception exception) =>
        new(
            engine,
            document.Name,
            "Error",
            document.Letters.Count,
            0,
            null,
            null,
            null,
            document.Words.Count,
            0,
            0,
            null,
            null,
            null,
            null,
            0,
            exception.GetType().Name + ": " + exception.Message);

    private static string Token(string text) =>
        string.Concat(text.Where(character => !char.IsWhiteSpace(character)));

    private static double Distance(Box left, Box right)
    {
        var x = left.CentreX - right.CentreX;
        var y = left.CentreY - right.CentreY;
        return (x * x) + (y * y);
    }
}
