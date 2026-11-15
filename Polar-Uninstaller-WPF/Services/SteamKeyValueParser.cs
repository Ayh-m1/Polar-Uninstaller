using System.Text;

namespace PolarUninstallerWpf.Services;

/// <summary>
/// A small, dependency-free parser for Valve's KeyValues (VDF) format used by
/// Steam's libraryfolders.vdf and appmanifest_*.acf files. It returns a nested
/// dictionary so callers never have to rely on fragile regular expressions.
/// </summary>
public static class SteamKeyValueParser
{
    public static Dictionary<string, object?> Parse(string text)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return result;
        var parser = new Parser(text);
        parser.ParseInto(result);
        return result;
    }

    public static string? GetString(Dictionary<string, object?> obj, string key)
    {
        if (obj.TryGetValue(key, out var value) && value is string text) return text;
        return null;
    }

    public static Dictionary<string, object?>? GetObject(Dictionary<string, object?> obj, string key)
    {
        if (obj.TryGetValue(key, out var value) && value is Dictionary<string, object?> nested) return nested;
        return null;
    }

    public static IEnumerable<KeyValuePair<string, object?>> Children(Dictionary<string, object?> obj) => obj;

    private sealed class Parser
    {
        private readonly string text;
        private int index;

        public Parser(string text) => this.text = text;

        public void ParseInto(Dictionary<string, object?> target)
        {
            SkipWhitespace();
            while (index < text.Length && text[index] != '}')
            {
                if (text[index] == '{') { index++; continue; }
                if (text[index] != '"') { index++; continue; }
                string key = ReadString();
                SkipWhitespace();
                if (index >= text.Length) break;
                if (text[index] == '{')
                {
                    index++;
                    var nested = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    ParseInto(nested);
                    target[key] = nested;
                }
                else if (text[index] == '"')
                {
                    target[key] = ReadString();
                }
                else
                {
                    index++;
                }
                SkipWhitespace();
            }
            if (index < text.Length && text[index] == '}') index++;
        }

        private void SkipWhitespace()
        {
            while (index < text.Length)
            {
                char c = text[index];
                if (char.IsWhiteSpace(c)) { index++; continue; }
                if (c == '/' && index + 1 < text.Length && text[index + 1] == '/')
                {
                    index += 2;
                    while (index < text.Length && text[index] != '\n') index++;
                    continue;
                }
                break;
            }
        }

        private string ReadString()
        {
            SkipWhitespace();
            if (index >= text.Length || text[index] != '"') return "";
            index++; // opening quote
            var builder = new StringBuilder();
            while (index < text.Length && text[index] != '"')
            {
                if (text[index] == '\\' && index + 1 < text.Length)
                {
                    char next = text[index + 1];
                    switch (next)
                    {
                        case 'n': builder.Append('\n'); break;
                        case 't': builder.Append('\t'); break;
                        case 'r': builder.Append('\r'); break;
                        default: builder.Append(next); break;
                    }
                    index += 2;
                    continue;
                }
                builder.Append(text[index]);
                index++;
            }
            if (index < text.Length) index++; // closing quote
            return builder.ToString();
        }
    }
}
