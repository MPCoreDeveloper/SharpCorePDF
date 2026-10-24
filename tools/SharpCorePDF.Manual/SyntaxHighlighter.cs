using System.Text;

namespace SharpCorePDF.Manual;

/// <summary>
/// Colours a fenced block with the token classes the manual stylesheet declares.
/// </summary>
/// <remarks>
/// The stylesheet was written for Pygments token names, so the output keeps those names: a keyword is
/// <c>k</c>, a string <c>s</c>, a comment <c>c</c>, a number <c>m</c>, punctuation <c>p</c>. The five
/// languages the manual uses are covered — C#, shell, PowerShell, JSON and plain text — and any other
/// language is escaped and left uncoloured rather than guessed at.
/// </remarks>
internal static class SyntaxHighlighter
{
    private const string Punctuation = "{}()[];,.<>=+-*/%&|^!~?:";

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "and", "as", "ascending", "async", "await", "base", "bool", "break", "by", "byte",
        "case", "catch", "char", "checked", "class", "const", "continue", "decimal", "default",
        "delegate", "descending", "do", "double", "dynamic", "else", "enum", "equals", "event",
        "explicit", "extern", "false", "file", "finally", "fixed", "float", "for", "foreach", "from",
        "get", "global", "goto", "group", "if", "implicit", "in", "init", "int", "interface",
        "internal", "into", "is", "join", "let", "lock", "long", "nameof", "namespace", "new", "nint",
        "not", "null", "nuint", "object", "on", "operator", "or", "orderby", "out", "override",
        "params", "partial", "private", "protected", "public", "readonly", "record", "ref", "required",
        "return", "sbyte", "scoped", "sealed", "select", "set", "short", "sizeof", "stackalloc",
        "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint",
        "ulong", "unchecked", "unsafe", "ushort", "using", "value", "var", "virtual", "void",
        "volatile", "when", "where", "while", "with", "yield",
    };

    private static readonly HashSet<string> ShellKeywords = new(StringComparer.Ordinal)
    {
        "alias", "break", "case", "cd", "continue", "declare", "do", "done", "elif", "else", "esac",
        "eval", "exec", "exit", "export", "fi", "for", "function", "if", "in", "local", "readonly",
        "return", "set", "shift", "source", "then", "trap", "unset", "until", "while",
    };

    private static readonly HashSet<string> PowerShellKeywords = new(StringComparer.Ordinal)
    {
        "begin", "break", "catch", "class", "continue", "data", "do", "dynamicparam", "else", "elseif",
        "end", "enum", "exit", "filter", "finally", "for", "foreach", "from", "function", "if", "in",
        "namespace", "param", "process", "return", "sequence", "switch", "throw", "trap", "try",
        "until", "using", "while", "workflow",
    };

    private static readonly HashSet<string> JsonKeywords = new(StringComparer.Ordinal)
    {
        "true", "false", "null",
    };

    /// <summary>Highlights one fenced block as the HTML a <c>pre/code</c> element expects.</summary>
    /// <param name="code">The text of the block, newlines included.</param>
    /// <param name="language">The language stated on the opening fence.</param>
    /// <returns>Escaped HTML with Pygments-compatible token spans.</returns>
    public static string Highlight(string code, string language)
    {
        ArgumentNullException.ThrowIfNull(code);

        return Normalise(language) switch
        {
            "csharp" or "cs" => CSharp(code),
            "json" => Json(code),
            "bash" or "sh" or "shell" => Shell(code, powerShell: false),
            "powershell" or "ps1" => Shell(code, powerShell: true),
            _ => Escape(code),
        };
    }

    /// <summary>Gets the label the block header shows for a fence language.</summary>
    /// <param name="language">The language stated on the opening fence.</param>
    /// <returns>The label the stylesheet expects above the block.</returns>
    public static string Label(string language) => Normalise(language) switch
    {
        "csharp" or "cs" => "C#",
        "protobuf" or "proto" => "proto",
        "yaml" or "yml" => "YAML",
        "json" => "JSON",
        "bash" or "sh" or "shell" => "shell",
        "powershell" or "ps1" => "PowerShell",
        "xml" or "msbuild" => "MSBuild / XML",
        "console" => "console",
        "ini" or "editorconfig" => "editorconfig",
        "" => "text",
        var other => other,
    };

    private static string Normalise(string language) =>
        (language ?? string.Empty).Trim().ToLowerInvariant();

    // ------------------------------------------------------------------ C# ---

    private static string CSharp(string code)
    {
        var html = new StringBuilder(code.Length + 256);
        var index = 0;

        while (index < code.Length)
        {
            var current = code[index];

            if (current == '/' && index + 1 < code.Length && code[index + 1] == '/')
            {
                var start = index;
                while (index < code.Length && code[index] != '\n')
                {
                    index++;
                }

                Span(html, "c", code.AsSpan(start, index - start));
                continue;
            }

            if (current == '/' && index + 1 < code.Length && code[index + 1] == '*')
            {
                var start = index;
                index += 2;
                while (index + 1 < code.Length && !(code[index] == '*' && code[index + 1] == '/'))
                {
                    index++;
                }

                index = Math.Min(code.Length, index + 2);
                Span(html, "c", code.AsSpan(start, index - start));
                continue;
            }

            // A preprocessor directive is a directive only at the start of a line.
            if (current == '#' && IsLineStart(code, index))
            {
                var start = index;
                while (index < code.Length && code[index] != '\n')
                {
                    index++;
                }

                Span(html, "c", code.AsSpan(start, index - start));
                continue;
            }

            if (current == '@' && index + 1 < code.Length && code[index + 1] == '"')
            {
                var start = index;
                index = ScanVerbatim(code, index + 1);
                Span(html, "s", code.AsSpan(start, index - start));
                continue;
            }

            if (current == '$' && index + 1 < code.Length && IsInterpolatedStart(code, index + 1))
            {
                var start = index;
                var quote = index + 1;
                var verbatim = code[quote] == '@';
                if (verbatim)
                {
                    quote++;
                }

                index = ScanInterpolated(code, quote, verbatim);
                Span(html, "s", code.AsSpan(start, index - start));
                continue;
            }

            if (current == '"')
            {
                var start = index;
                index = ScanString(code, index);
                Span(html, "s", code.AsSpan(start, index - start));
                continue;
            }

            if (current == '\'')
            {
                var start = index;
                index = ScanCharacter(code, index);
                Span(html, "s", code.AsSpan(start, index - start));
                continue;
            }

            if (char.IsAsciiDigit(current)
                || (current == '.' && index + 1 < code.Length && char.IsAsciiDigit(code[index + 1])))
            {
                var start = index;
                index = ScanNumber(code, index);
                Span(html, "m", code.AsSpan(start, index - start));
                continue;
            }

            if (char.IsLetter(current) || current == '_')
            {
                var start = index;
                while (index < code.Length && (char.IsLetterOrDigit(code[index]) || code[index] == '_'))
                {
                    index++;
                }

                var word = code.AsSpan(start, index - start);
                if (CSharpKeywords.Contains(word.ToString()))
                {
                    Span(html, "k", word);
                }
                else
                {
                    AppendEscaped(html, word);
                }

                continue;
            }

            if (Punctuation.Contains(current))
            {
                var start = index;
                while (index < code.Length && Punctuation.Contains(code[index]))
                {
                    index++;
                }

                Span(html, "p", code.AsSpan(start, index - start));
                continue;
            }

            AppendEscaped(html, current);
            index++;
        }

        return html.ToString();
    }

    private static bool IsInterpolatedStart(string code, int index)
    {
        if (code[index] == '"')
        {
            return true;
        }

        return code[index] == '@' && index + 1 < code.Length && code[index + 1] == '"';
    }

    private static int ScanVerbatim(string code, int quote)
    {
        var index = quote + 1;

        while (index < code.Length)
        {
            if (code[index] != '"')
            {
                index++;
                continue;
            }

            if (index + 1 < code.Length && code[index + 1] == '"')
            {
                index += 2;
                continue;
            }

            return index + 1;
        }

        return code.Length;
    }

    private static int ScanString(string code, int quote)
    {
        var index = quote + 1;

        if (index + 1 < code.Length && code[index] == '"' && code[index + 1] == '"')
        {
            return ScanRaw(code, quote);
        }

        while (index < code.Length)
        {
            if (code[index] == '\\')
            {
                index += 2;
                continue;
            }

            if (code[index] == '"')
            {
                return index + 1;
            }

            if (code[index] == '\n')
            {
                return index;
            }

            index++;
        }

        return code.Length;
    }

    private static int ScanRaw(string code, int quote)
    {
        var run = 0;
        while (quote + run < code.Length && code[quote + run] == '"')
        {
            run++;
        }

        var index = quote + run;
        while (index < code.Length)
        {
            if (code[index] != '"')
            {
                index++;
                continue;
            }

            var closing = 0;
            while (index + closing < code.Length && code[index + closing] == '"')
            {
                closing++;
            }

            if (closing >= run)
            {
                return index + closing;
            }

            index += closing;
        }

        return code.Length;
    }

    private static int ScanInterpolated(string code, int quote, bool verbatim)
    {
        var run = 0;
        while (quote + run < code.Length && code[quote + run] == '"')
        {
            run++;
        }

        // A raw interpolated string keeps its holes; the quote run is the delimiter.
        if (run >= 3)
        {
            return ScanRaw(code, quote);
        }

        var index = quote + 1;
        var depth = 0;

        while (index < code.Length)
        {
            var current = code[index];

            if (depth == 0)
            {
                if (!verbatim && current == '\\')
                {
                    index += 2;
                    continue;
                }

                if (current == '"')
                {
                    if (verbatim && index + 1 < code.Length && code[index + 1] == '"')
                    {
                        index += 2;
                        continue;
                    }

                    return index + 1;
                }

                if (current == '{')
                {
                    if (index + 1 < code.Length && code[index + 1] == '{')
                    {
                        index += 2;
                        continue;
                    }

                    depth = 1;
                    index++;
                    continue;
                }

                if (!verbatim && current == '\n')
                {
                    return index;
                }

                index++;
                continue;
            }

            switch (current)
            {
                case '{':
                    depth++;
                    index++;
                    break;
                case '}':
                    depth--;
                    index++;
                    break;
                case '"':
                    index = ScanString(code, index);
                    break;
                default:
                    index++;
                    break;
            }
        }

        return code.Length;
    }

    private static int ScanCharacter(string code, int quote)
    {
        var index = quote + 1;

        while (index < code.Length)
        {
            if (code[index] == '\\')
            {
                index += 2;
                continue;
            }

            if (code[index] == '\'')
            {
                return index + 1;
            }

            if (code[index] == '\n')
            {
                return index;
            }

            index++;
        }

        return code.Length;
    }

    private static int ScanNumber(string code, int start)
    {
        var index = start;

        if (code[index] == '0'
            && index + 1 < code.Length
            && code[index + 1] is 'x' or 'X' or 'b' or 'B')
        {
            index += 2;
            while (index < code.Length && (char.IsAsciiLetterOrDigit(code[index]) || code[index] == '_'))
            {
                index++;
            }

            return index;
        }

        while (index < code.Length && (char.IsAsciiDigit(code[index]) || code[index] == '_'))
        {
            index++;
        }

        if (index + 1 < code.Length && code[index] == '.' && char.IsAsciiDigit(code[index + 1]))
        {
            index++;
            while (index < code.Length && (char.IsAsciiDigit(code[index]) || code[index] == '_'))
            {
                index++;
            }
        }

        if (index < code.Length && code[index] is 'e' or 'E')
        {
            var save = index;
            index++;
            if (index < code.Length && code[index] is '+' or '-')
            {
                index++;
            }

            if (index < code.Length && char.IsAsciiDigit(code[index]))
            {
                while (index < code.Length && char.IsAsciiDigit(code[index]))
                {
                    index++;
                }
            }
            else
            {
                index = save;
            }
        }

        if (index < code.Length && "uUlLmMdDfF".Contains(code[index]))
        {
            index++;
        }

        return index;
    }

    private static bool IsLineStart(string code, int index)
    {
        var cursor = index - 1;
        while (cursor >= 0 && code[cursor] is ' ' or '\t')
        {
            cursor--;
        }

        return cursor < 0 || code[cursor] == '\n';
    }

    // --------------------------------------------------------------- shell ---

    private static string Shell(string code, bool powerShell)
    {
        var keywords = powerShell ? PowerShellKeywords : ShellKeywords;
        var escape = powerShell ? '`' : '\\';
        var html = new StringBuilder(code.Length + 128);
        var index = 0;

        while (index < code.Length)
        {
            var current = code[index];

            if (current == '#')
            {
                var start = index;
                while (index < code.Length && code[index] != '\n')
                {
                    index++;
                }

                Span(html, "c", code.AsSpan(start, index - start));
                continue;
            }

            if (current == '\'' || current == '"')
            {
                var start = index;
                index = ScanShellString(code, index, escape);
                Span(html, "s", code.AsSpan(start, index - start));
                continue;
            }

            if (current == '$'
                || (powerShell && current == '@' && index + 1 < code.Length && code[index + 1] is '(' or '{'))
            {
                var start = index;
                index = ScanVariable(code, index);
                Span(html, "n", code.AsSpan(start, index - start));
                continue;
            }

            if (current == '-'
                && index + 1 < code.Length
                && char.IsLetter(code[index + 1])
                && (index == 0 || char.IsWhiteSpace(code[index - 1])))
            {
                var start = index;
                index++;
                while (index < code.Length && (char.IsLetterOrDigit(code[index]) || code[index] is '-' or '_'))
                {
                    index++;
                }

                Span(html, "na", code.AsSpan(start, index - start));
                continue;
            }

            if (char.IsAsciiDigit(current) && (index == 0 || !char.IsLetterOrDigit(code[index - 1])))
            {
                var start = index;
                while (index < code.Length && (char.IsAsciiDigit(code[index]) || code[index] is '.' or '_'))
                {
                    index++;
                }

                Span(html, "m", code.AsSpan(start, index - start));
                continue;
            }

            if (char.IsLetter(current) || current == '_')
            {
                var start = index;
                while (index < code.Length && (char.IsLetterOrDigit(code[index]) || code[index] is '_' or '-'))
                {
                    index++;
                }

                var word = code.AsSpan(start, index - start);
                if (keywords.Contains(word.ToString()))
                {
                    Span(html, "k", word);
                }
                else
                {
                    AppendEscaped(html, word);
                }

                continue;
            }

            if (Punctuation.Contains(current))
            {
                var start = index;
                while (index < code.Length && Punctuation.Contains(code[index]))
                {
                    index++;
                }

                Span(html, "p", code.AsSpan(start, index - start));
                continue;
            }

            AppendEscaped(html, current);
            index++;
        }

        return html.ToString();
    }

    private static int ScanShellString(string code, int quote, char escape)
    {
        var delimiter = code[quote];
        var index = quote + 1;

        while (index < code.Length)
        {
            if (code[index] == escape && delimiter == '"')
            {
                index += 2;
                continue;
            }

            if (code[index] == delimiter)
            {
                // PowerShell and POSIX shells both escape a single quote by doubling it.
                if (delimiter == '\'' && index + 1 < code.Length && code[index + 1] == '\'')
                {
                    index += 2;
                    continue;
                }

                return index + 1;
            }

            index++;
        }

        return code.Length;
    }

    private static int ScanVariable(string code, int start)
    {
        var index = start + 1;
        if (index >= code.Length)
        {
            return index;
        }

        if (code[index] == '{' || code[index] == '(')
        {
            var opening = code[index];
            var closing = opening == '{' ? '}' : ')';
            var depth = 1;
            index++;
            while (index < code.Length && depth > 0)
            {
                if (code[index] == opening)
                {
                    depth++;
                }
                else if (code[index] == closing)
                {
                    depth--;
                }

                index++;
            }

            return index;
        }

        while (index < code.Length && (char.IsLetterOrDigit(code[index]) || code[index] is '_' or ':'))
        {
            index++;
        }

        return index;
    }

    // ---------------------------------------------------------------- JSON ---

    private static string Json(string code)
    {
        var html = new StringBuilder(code.Length + 64);
        var index = 0;

        while (index < code.Length)
        {
            var current = code[index];

            if (current == '"')
            {
                var start = index;
                index = ScanString(code, index);
                var text = code.AsSpan(start, index - start);
                var cursor = index;
                while (cursor < code.Length && char.IsWhiteSpace(code[cursor]))
                {
                    cursor++;
                }

                Span(html, cursor < code.Length && code[cursor] == ':' ? "nt" : "s", text);
                continue;
            }

            if (char.IsAsciiDigit(current) || current == '-')
            {
                var start = index;
                index++;
                while (index < code.Length
                    && (char.IsAsciiDigit(code[index]) || code[index] is '.' or 'e' or 'E' or '+' or '-'))
                {
                    index++;
                }

                Span(html, "m", code.AsSpan(start, index - start));
                continue;
            }

            if (char.IsLetter(current))
            {
                var start = index;
                while (index < code.Length && char.IsLetter(code[index]))
                {
                    index++;
                }

                var word = code.AsSpan(start, index - start);
                if (JsonKeywords.Contains(word.ToString()))
                {
                    Span(html, "k", word);
                }
                else
                {
                    AppendEscaped(html, word);
                }

                continue;
            }

            if (Punctuation.Contains(current))
            {
                var start = index;
                while (index < code.Length && Punctuation.Contains(code[index]))
                {
                    index++;
                }

                Span(html, "p", code.AsSpan(start, index - start));
                continue;
            }

            AppendEscaped(html, current);
            index++;
        }

        return html.ToString();
    }

    // ------------------------------------------------------------- escaping ---

    private static void Span(StringBuilder html, string cssClass, ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return;
        }

        html.Append("<span class=\"").Append(cssClass).Append("\">");
        AppendEscaped(html, text);
        html.Append("</span>");
    }

    private static void AppendEscaped(StringBuilder html, ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            AppendEscaped(html, character);
        }
    }

    private static void AppendEscaped(StringBuilder html, char character)
    {
        switch (character)
        {
            case '&':
                html.Append("&amp;");
                break;
            case '<':
                html.Append("&lt;");
                break;
            case '>':
                html.Append("&gt;");
                break;
            default:
                html.Append(character);
                break;
        }
    }

    private static string Escape(string text)
    {
        var html = new StringBuilder(text.Length + 16);
        AppendEscaped(html, text.AsSpan());
        return html.ToString();
    }
}
