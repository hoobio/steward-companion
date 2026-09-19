using System.Globalization;
using System.Text;

namespace Steward.Core;

public enum LuaKind
{
    Nil,
    Boolean,
    Number,
    Text,
    Table,
}

public sealed record LuaEntry(LuaValue? Key, LuaValue Value);

public sealed record LuaValue
{
    private static readonly IReadOnlyList<LuaEntry> NoEntries = [];

    private LuaValue(LuaKind kind) => Kind = kind;

    public LuaKind Kind { get; }

    public string? Text { get; private init; }

    public double Number { get; private init; }

    public bool Boolean { get; private init; }

    public IReadOnlyList<LuaEntry> Table { get; private init; } = NoEntries;

    public IReadOnlyList<LuaValue> Items => [.. Table.Where(e => e.Key is null).Select(e => e.Value)];

    public static LuaValue Nil { get; } = new(LuaKind.Nil);

    public static LuaValue FromBoolean(bool value) => new(LuaKind.Boolean) { Boolean = value };

    public static LuaValue FromNumber(double value) => new(LuaKind.Number) { Number = value };

    public static LuaValue FromString(string value) => new(LuaKind.Text) { Text = value };

    public static LuaValue FromTable(IReadOnlyList<LuaEntry> entries) => new(LuaKind.Table) { Table = entries };

    public static LuaValue FromTable(params LuaEntry[] entries) => FromTable((IReadOnlyList<LuaEntry>)entries);

    public static LuaValue Array(IEnumerable<LuaValue> items) => FromTable([.. items.Select(item => new LuaEntry(null, item))]);

    public LuaValue? Get(string key) =>
        Table.FirstOrDefault(e => e.Key is { Kind: LuaKind.Text } k && string.Equals(k.Text, key, StringComparison.Ordinal))?.Value;

    public string? GetString(string key) => Get(key) is { Kind: LuaKind.Text } value ? value.Text : null;

    public double? GetNumber(string key) => Get(key) is { Kind: LuaKind.Number } value ? value.Number : null;

    public LuaValue? GetTable(string key) => Get(key) is { Kind: LuaKind.Table } value ? value : null;
}

public static class LuaSavedVariables
{
    public static IReadOnlyDictionary<string, LuaValue> Parse(string text) => new LuaReader(text).ReadGlobals();
}

internal sealed class LuaReader(string text)
{
    private int _index;
    private int _line = 1;
    private int _column = 1;

    public Dictionary<string, LuaValue> ReadGlobals()
    {
        var globals = new Dictionary<string, LuaValue>(StringComparer.Ordinal);
        SkipTrivia();
        while (!AtEnd)
        {
            var name = ReadName();
            SkipTrivia();
            Expect('=');
            globals[name] = ReadValue();
            SkipTrivia();
            while (!AtEnd && Peek is ';')
            {
                Advance();
                SkipTrivia();
            }
        }

        return globals;
    }

    private bool AtEnd => _index >= text.Length;

    private char Peek => text[_index];

    private LuaValue ReadValue()
    {
        SkipTrivia();
        if (AtEnd)
        {
            throw Fail("unexpected end of input");
        }

        return Peek switch
        {
            '{' => ReadTable(),
            '"' or '\'' => LuaValue.FromString(ReadQuotedString()),
            '-' or '.' or (>= '0' and <= '9') => LuaValue.FromNumber(ReadNumber()),
            _ => Keyword(ReadName()),
        };
    }

    private LuaValue ReadTable()
    {
        Expect('{');
        var entries = new List<LuaEntry>();
        while (true)
        {
            SkipTrivia();
            if (AtEnd)
            {
                throw Fail("unterminated table");
            }

            if (Peek is '}')
            {
                Advance();
                return LuaValue.FromTable(entries);
            }

            entries.Add(ReadEntry());
            SkipTrivia();
            if (!AtEnd && Peek is ',' or ';')
            {
                Advance();
                continue;
            }

            if (!AtEnd && Peek is '}')
            {
                Advance();
                return LuaValue.FromTable(entries);
            }

            throw Fail("expected ',' or '}'");
        }
    }

    private LuaEntry ReadEntry()
    {
        if (Peek is '[')
        {
            Advance();
            var key = ReadValue();
            SkipTrivia();
            Expect(']');
            SkipTrivia();
            Expect('=');
            return new LuaEntry(key, ReadValue());
        }

        if (Peek is '_' || char.IsAsciiLetter(Peek))
        {
            var word = ReadName();
            SkipTrivia();
            if (!AtEnd && Peek is '=')
            {
                Advance();
                return new LuaEntry(LuaValue.FromString(word), ReadValue());
            }

            return new LuaEntry(null, Keyword(word));
        }

        return new LuaEntry(null, ReadValue());
    }

    private LuaValue Keyword(string word) => word switch
    {
        "true" => LuaValue.FromBoolean(true),
        "false" => LuaValue.FromBoolean(false),
        "nil" => LuaValue.Nil,
        _ => throw Fail($"unexpected '{word}'"),
    };

    private string ReadName()
    {
        if (AtEnd || (Peek is not '_' && !char.IsAsciiLetter(Peek)))
        {
            throw Fail("expected a name");
        }

        var start = _index;
        while (!AtEnd && (Peek is '_' || char.IsAsciiLetterOrDigit(Peek)))
        {
            Advance();
        }

        return text[start.._index];
    }

    private double ReadNumber()
    {
        var start = _index;
        if (Peek is '-' or '+')
        {
            Advance();
        }

        while (!AtEnd && (char.IsAsciiDigit(Peek) || Peek is '.'))
        {
            Advance();
        }

        if (!AtEnd && Peek is 'e' or 'E')
        {
            Advance();
            if (!AtEnd && Peek is '-' or '+')
            {
                Advance();
            }

            while (!AtEnd && char.IsAsciiDigit(Peek))
            {
                Advance();
            }
        }

        var literal = text[start.._index];
        return double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw Fail($"'{literal}' is not a number");
    }

    private string ReadQuotedString()
    {
        var quote = Peek;
        Advance();
        var builder = new StringBuilder();
        while (true)
        {
            if (AtEnd)
            {
                throw Fail("unterminated string");
            }

            var c = Peek;
            Advance();
            if (c == quote)
            {
                return builder.ToString();
            }

            if (c != '\\')
            {
                builder.Append(c);
                continue;
            }

            if (AtEnd)
            {
                throw Fail("unterminated escape");
            }

            builder.Append(ReadEscape());
        }
    }

    private string ReadEscape()
    {
        var c = Peek;
        Advance();
        if (char.IsAsciiDigit(c))
        {
            var code = c - '0';
            for (var digits = 1; digits < 3 && !AtEnd && char.IsAsciiDigit(Peek); digits++)
            {
                code = (code * 10) + (Peek - '0');
                Advance();
            }

            return ((char)code).ToString(CultureInfo.InvariantCulture);
        }

        return c switch
        {
            'n' => "\n",
            'r' => "\r",
            't' => "\t",
            'a' => "\a",
            'b' => "\b",
            'f' => "\f",
            'v' => "\v",
            _ => c.ToString(CultureInfo.InvariantCulture),
        };
    }

    private void SkipTrivia()
    {
        while (!AtEnd)
        {
            if (char.IsWhiteSpace(Peek))
            {
                Advance();
                continue;
            }

            if (Peek is '-' && _index + 1 < text.Length && text[_index + 1] is '-')
            {
                while (!AtEnd && Peek is not '\n')
                {
                    Advance();
                }

                continue;
            }

            return;
        }
    }

    private void Expect(char expected)
    {
        if (AtEnd || Peek != expected)
        {
            throw Fail($"expected '{expected}'");
        }

        Advance();
    }

    private void Advance()
    {
        if (text[_index] is '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }

        _index++;
    }

    private FormatException Fail(string message) =>
        new(string.Create(CultureInfo.InvariantCulture, $"Lua parse error at line {_line}, column {_column}: {message}"));
}
