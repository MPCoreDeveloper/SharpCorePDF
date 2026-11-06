using System.Text;

namespace SharpCorePDF.Infrastructure.Pdf;

/// <summary>
/// The keywords a content stream can state, each returned as the one string instance it is.
/// </summary>
/// <remarks>
/// <para>
/// A content stream states an operator before almost every operand, and the set of operators is
/// closed: the specification names them, and a producer cannot invent one that a reader must honour.
/// Reading each of them into a fresh string is therefore a per-operator allocation that says nothing
/// a cached instance does not already say.
/// </para>
/// <para>
/// A keyword is packed into a <see cref="long"/> — its length, then its bytes — so the lookup is one
/// hash of a value instead of a comparison against every operator in turn. A keyword longer than
/// eight bytes, or one this library has never seen, falls back to the string it is, which keeps the
/// scanner tolerant of anything a damaged file writes.
/// </para>
/// </remarks>
internal static class PdfKeywords
{
    /// <summary>The number of bytes a packed keyword can carry.</summary>
    private const int MaximumKeywordLength = 8;

    private static readonly Dictionary<long, string> Known = Build();

    /// <summary>Reads a keyword, returning the cached instance when it is one the specification names.</summary>
    /// <param name="raw">The bytes of the keyword, without whitespace or delimiters.</param>
    /// <returns>The keyword.</returns>
    public static string Read(ReadOnlySpan<byte> raw)
    {
        if (raw.Length <= MaximumKeywordLength)
        {
            var packed = (long)raw.Length;

            for (var index = 0; index < raw.Length; index++)
            {
                packed = (packed << 8) | raw[index];
            }

            if (Known.TryGetValue(packed, out var keyword))
            {
                return keyword;
            }
        }

        return Encoding.Latin1.GetString(raw);
    }

    /// <summary>Builds the packed table of the operators and literal names a content stream uses.</summary>
    /// <returns>The table, keyed by the packed bytes of each keyword.</returns>
    private static Dictionary<long, string> Build()
    {
        string[] keywords =
        [
            "q", "Q", "cm", "BT", "ET", "Tf", "Tc", "Tw", "Tz", "TL", "Ts", "Td", "TD", "Tm",
            "T*", "Tj", "TJ", "'", "\"", "Do", "BI", "EI", "gs", "rg", "RG", "g", "G", "k", "K",
            "cs", "CS", "sc", "SC", "scn", "SCN", "w", "J", "j", "M", "d", "ri", "i", "W", "W*",
            "n", "m", "l", "c", "v", "y", "h", "re", "f", "F", "f*", "B", "B*", "b", "b*", "S",
            "s", "sh", "BMC", "BDC", "EMC", "MP", "DP", "BX", "EX", "d0", "d1", "true", "false",
            "null",
        ];

        var known = new Dictionary<long, string>(keywords.Length);

        foreach (var keyword in keywords)
        {
            var packed = (long)keyword.Length;

            foreach (var character in keyword)
            {
                packed = (packed << 8) | (byte)character;
            }

            known[packed] = keyword;
        }

        return known;
    }
}
