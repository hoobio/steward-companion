using System.Text;

namespace Steward.Core.Tests;

public sealed class SyncDocumentGoldenTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static LuaValue Unwrap(string rendered)
    {
        var start = rendered.IndexOf('(', StringComparison.Ordinal) + 1;
        return LuaSavedVariables.Parse("X = " + rendered[start..rendered.LastIndexOf(')')])["X"];
    }

    private static string Canonical(LuaValue value)
    {
        if (value.Kind != LuaKind.Table)
        {
            return LuaWriter.Serialize(value);
        }

        var position = 0;
        var parts = new List<string>();
        foreach (var entry in value.Table)
        {
            var key = entry.Key ?? LuaValue.FromNumber(++position);
            if (entry.Value.Kind != LuaKind.Nil)
            {
                parts.Add($"[{LuaWriter.Serialize(key)}]={Canonical(entry.Value)}");
            }
        }

        return "{" + string.Join(",", parts) + "}";
    }

    [Fact]
    public void TheConverter_OnTheServerPayload_GivesTheTypedRenderersGoldenAsLuaData()
    {
        var expected = File.ReadAllText(Fixture("steward-sync-document.expected.json"), new UTF8Encoding(false));
        var payload = SyncDocument.Parse(
            """{"version":1,"signOut":"dropMe","payload":""" + expected + ""","images":{"guild":null,"people":{},"discord":{}}}""",
            DateTimeOffset.FromUnixTimeSeconds(1760000000));

        var golden = Unwrap(File.ReadAllText(Fixture("steward-sync-officer.golden.lua"), new UTF8Encoding(false)));
        var converted = Unwrap(StewardSyncFile.Render(payload));

        Assert.Equal(Canonical(golden), Canonical(converted));
    }
}
