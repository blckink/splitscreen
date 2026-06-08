using System.Collections.Generic;
using System.Text;

namespace SplitPlay.Steam.Vdf;

/// <summary>
/// A minimal, allocation-light parser for Valve's text KeyValues format, used by
/// Steam for <c>libraryfolders.vdf</c> and <c>appmanifest_*.acf</c>. It handles
/// quoted tokens, escape sequences and nested blocks. Unsupported niceties
/// (conditionals like <c>[$WIN32]</c>, <c>#include</c>) are simply skipped, which
/// is sufficient for the files we read.
/// </summary>
public static class VdfParser
{
    /// <summary>Parses a VDF/ACF document into a root <see cref="VdfNode"/>.</summary>
    public static VdfNode Parse(string text)
    {
        int pos = 0;
        var root = new VdfNode();
        ParseBlockBody(text, ref pos, root);
        return root;
    }

    /// <summary>
    /// Parses key/value pairs and nested blocks into <paramref name="node"/> until
    /// the end of the current block ('}') or end of input.
    /// </summary>
    private static void ParseBlockBody(string text, ref int pos, VdfNode node)
    {
        while (true)
        {
            string? key = ReadToken(text, ref pos);
            if (key is null)
            {
                return; // End of input.
            }

            if (key == "}")
            {
                return; // End of this block.
            }

            // After a key we expect either a nested block '{' or a scalar value.
            SkipWhitespaceAndComments(text, ref pos);
            if (pos < text.Length && text[pos] == '{')
            {
                pos++; // Consume '{'.
                var child = new VdfNode();
                ParseBlockBody(text, ref pos, child);
                node.Children[key] = child;
            }
            else
            {
                string? value = ReadToken(text, ref pos);
                if (value is null || value == "}")
                {
                    return;
                }

                node.Values[key] = value;
            }
        }
    }

    /// <summary>
    /// Reads the next token: a quoted string, a bare word, or the single '{'/'}'
    /// punctuation characters. Returns null at end of input.
    /// </summary>
    private static string? ReadToken(string text, ref int pos)
    {
        SkipWhitespaceAndComments(text, ref pos);
        if (pos >= text.Length)
        {
            return null;
        }

        char c = text[pos];

        if (c == '{' || c == '}')
        {
            pos++;
            return c.ToString();
        }

        if (c == '"')
        {
            return ReadQuoted(text, ref pos);
        }

        // Bare (unquoted) token: read until whitespace or a brace.
        int start = pos;
        while (pos < text.Length && !char.IsWhiteSpace(text[pos]) &&
               text[pos] != '{' && text[pos] != '}' && text[pos] != '"')
        {
            pos++;
        }

        return text[start..pos];
    }

    private static string ReadQuoted(string text, ref int pos)
    {
        pos++; // Skip opening quote.
        var sb = new StringBuilder();
        while (pos < text.Length)
        {
            char c = text[pos++];
            if (c == '\\' && pos < text.Length)
            {
                // Handle the common escape sequences Steam emits.
                char next = text[pos++];
                sb.Append(next switch
                {
                    'n' => '\n',
                    't' => '\t',
                    '\\' => '\\',
                    '"' => '"',
                    _ => next
                });
            }
            else if (c == '"')
            {
                break; // Closing quote.
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static void SkipWhitespaceAndComments(string text, ref int pos)
    {
        while (pos < text.Length)
        {
            char c = text[pos];
            if (char.IsWhiteSpace(c))
            {
                pos++;
            }
            else if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
            {
                // Line comment: skip to end of line.
                while (pos < text.Length && text[pos] != '\n')
                {
                    pos++;
                }
            }
            else
            {
                return;
            }
        }
    }
}
