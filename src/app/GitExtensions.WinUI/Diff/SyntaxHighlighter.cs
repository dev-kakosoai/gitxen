using System.Text;

namespace GitExtensions.WinUI.Diff;

public enum TokenKind
{
    Text,
    Keyword,
    String,
    Comment,
    Number
}

public readonly record struct SyntaxToken(string Text, TokenKind Kind);

/// <summary>
///  A deliberately small, language-agnostic tokenizer: comments, strings, numbers and a per-language
///  keyword list.
/// </summary>
/// <remarks>
///  Not a parser, and not trying to be. A diff shows fragments of files without their surrounding
///  context, so anything more precise would be guessing anyway — and the real highlighting engine
///  the WinForms app uses (ICSharpCode.TextEditor) is WinForms-only.
/// </remarks>
public static class SyntaxHighlighter
{
    private static readonly HashSet<string> _cLikeKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "async", "await", "base", "bool", "break", "byte", "case", "catch", "char",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum",
        "event", "explicit", "extends", "extern", "false", "finally", "fixed", "float", "for", "foreach",
        "from", "function", "get", "goto", "if", "implements", "implicit", "import", "in", "int",
        "interface", "internal", "is", "let", "lock", "long", "namespace", "new", "null", "object",
        "operator", "out", "override", "params", "private", "protected", "public", "readonly", "record",
        "ref", "return", "sealed", "set", "short", "sizeof", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong", "unsafe", "ushort", "using", "var",
        "virtual", "void", "while", "yield", "export", "const", "type"
    };

    private static readonly HashSet<string> _pythonKeywords = new(StringComparer.Ordinal)
    {
        "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif",
        "else", "except", "False", "finally", "for", "from", "global", "if", "import", "in", "is",
        "lambda", "None", "nonlocal", "not", "or", "pass", "raise", "return", "True", "try", "while",
        "with", "yield"
    };

    /// <summary>Splits a single line into coloured tokens.</summary>
    public static IReadOnlyList<SyntaxToken> Tokenize(string line, string fileName)
    {
        Language language = DetectLanguage(fileName);

        if (language == Language.None || string.IsNullOrEmpty(line))
        {
            return [new SyntaxToken(line, TokenKind.Text)];
        }

        List<SyntaxToken> tokens = [];
        StringBuilder pending = new();
        int i = 0;

        void FlushPending()
        {
            if (pending.Length > 0)
            {
                tokens.Add(new SyntaxToken(pending.ToString(), TokenKind.Text));
                pending.Clear();
            }
        }

        while (i < line.Length)
        {
            // A comment runs to end of line, so everything left is one token.
            if (IsCommentStart(line, i, language, out int commentLength))
            {
                FlushPending();
                tokens.Add(new SyntaxToken(line[i..], TokenKind.Comment));
                return tokens;
            }

            char c = line[i];

            if (c is '"' or '\'')
            {
                FlushPending();
                int end = FindStringEnd(line, i, c);
                tokens.Add(new SyntaxToken(line[i..end], TokenKind.String));
                i = end;
                continue;
            }

            if (char.IsDigit(c) && (i == 0 || !char.IsLetterOrDigit(line[i - 1])))
            {
                FlushPending();
                int end = i;
                while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '.'))
                {
                    end++;
                }

                tokens.Add(new SyntaxToken(line[i..end], TokenKind.Number));
                i = end;
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int end = i;
                while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '_'))
                {
                    end++;
                }

                string word = line[i..end];
                if (KeywordsFor(language).Contains(word))
                {
                    FlushPending();
                    tokens.Add(new SyntaxToken(word, TokenKind.Keyword));
                }
                else
                {
                    pending.Append(word);
                }

                i = end;
                continue;
            }

            pending.Append(c);
            i++;
            _ = commentLength;
        }

        FlushPending();
        return tokens;
    }

    private static HashSet<string> KeywordsFor(Language language) =>
        language == Language.Python ? _pythonKeywords : _cLikeKeywords;

    private static bool IsCommentStart(string line, int index, Language language, out int length)
    {
        length = 0;

        if (language == Language.Python)
        {
            if (line[index] == '#')
            {
                length = 1;
                return true;
            }

            return false;
        }

        if (language == Language.Markup)
        {
            if (line.AsSpan(index).StartsWith("<!--", StringComparison.Ordinal))
            {
                length = 4;
                return true;
            }

            return false;
        }

        if (line.AsSpan(index).StartsWith("//", StringComparison.Ordinal))
        {
            length = 2;
            return true;
        }

        return false;
    }

    private static int FindStringEnd(string line, int start, char quote)
    {
        for (int i = start + 1; i < line.Length; i++)
        {
            if (line[i] == '\\')
            {
                i++;
                continue;
            }

            if (line[i] == quote)
            {
                return i + 1;
            }
        }

        return line.Length;
    }

    private static Language DetectLanguage(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".cs" or ".c" or ".cpp" or ".h" or ".hpp" or ".java" or ".js" or ".ts" or ".tsx" or ".jsx"
            or ".go" or ".rs" or ".kt" or ".swift" or ".php" or ".json" => Language.CLike,
        ".py" or ".sh" or ".yml" or ".yaml" or ".toml" => Language.Python,
        ".xml" or ".xaml" or ".html" or ".htm" or ".csproj" or ".props" or ".targets" or ".config"
            or ".svg" or ".slnx" => Language.Markup,
        _ => Language.None
    };

    private enum Language
    {
        None,
        CLike,
        Python,
        Markup
    }
}
