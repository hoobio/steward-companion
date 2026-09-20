using System.Globalization;
using System.Text;

namespace Steward.Core;

public static class LuaWriter
{
    public static string Serialize(LuaValue value)
    {
        var builder = new StringBuilder();
        WriteValue(builder, value, 0);
        return builder.ToString();
    }

    public static string SerializeString(string text)
    {
        var builder = new StringBuilder();
        WriteString(builder, text);
        return builder.ToString();
    }

    private static void WriteValue(StringBuilder builder, LuaValue value, int depth)
    {
        switch (value.Kind)
        {
            case LuaKind.Table:
                WriteTable(builder, value, depth);
                break;
            case LuaKind.Text:
                WriteString(builder, value.Text!);
                break;
            case LuaKind.Number:
                builder.Append(value.Number.ToString("R", CultureInfo.InvariantCulture));
                break;
            case LuaKind.Boolean:
                builder.Append(value.Boolean ? "true" : "false");
                break;
            case LuaKind.Nil:
                builder.Append("nil");
                break;
        }
    }

    private static void WriteTable(StringBuilder builder, LuaValue table, int depth)
    {
        builder.Append('{').Append('\n');
        var indent = new string('\t', depth + 1);
        foreach (var entry in table.Table)
        {
            builder.Append(indent);
            if (entry.Key is { } key)
            {
                builder.Append('[');
                WriteValue(builder, key, depth + 1);
                builder.Append("] = ");
            }

            WriteValue(builder, entry.Value, depth + 1);
            builder.Append(",\n");
        }

        builder.Append('\t', depth).Append('}');
    }

    private static void WriteString(StringBuilder builder, string text)
    {
        builder.Append('"');
        foreach (var rune in text.EnumerateRunes())
        {
            switch (rune.Value)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (rune.Value < 0x20 || rune.Value is 0x7F)
                    {
                        builder.Append('\\').Append(rune.Value.ToString("D3", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(rune);
                    }

                    break;
            }
        }

        builder.Append('"');
    }
}
