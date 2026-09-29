using System.Text;

namespace Steward.Core;

public sealed record AddonIconPlan(bool WriteTexture, IReadOnlyList<string> TocsToEdit);

public static class GeneratedAddonIcon
{
    public const string FileName = "StewardIcon.tga";
    public const int Size = 64;

    private const string TextureName = "StewardIcon";

    public static string TexturePath(string folderName) => $@"Interface\AddOns\{folderName}\{TextureName}";

    public static IReadOnlyList<string> TopLevelTocs(string folderPath, string folderName) =>
    [
        .. new[] { Path.Combine(folderPath, folderName + ".toc") }.Where(File.Exists),
        .. Directory.EnumerateFiles(folderPath, folderName + "_*.toc").Order(StringComparer.Ordinal),
    ];

    public static AddonIconPlan? Plan(string addOnsPath, string folderName)
    {
        var folderPath = Path.Combine(addOnsPath, folderName);
        if (!Directory.Exists(folderPath))
        {
            return null;
        }

        var tocs = TopLevelTocs(folderPath, folderName)
            .Select(toc => (Path: toc, Icon: TocFile.ReadDirective(toc, "IconTexture")))
            .ToList();
        if (tocs.Count == 0 || tocs.Any(toc => toc.Icon is not null && !string.Equals(toc.Icon, TexturePath(folderName), StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var edit = tocs.Where(toc => toc.Icon is null).Select(toc => toc.Path).ToList();
        var writeTexture = !File.Exists(Path.Combine(folderPath, FileName));
        return edit.Count == 0 && !writeTexture ? null : new AddonIconPlan(writeTexture, edit);
    }

    public static void Apply(string addOnsPath, string folderName, AddonIconPlan plan, byte[]? texture)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var folderRoot = Path.GetFullPath(Path.Combine(addOnsPath, folderName) + Path.DirectorySeparatorChar);
        if (folderRoot.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("WTF", StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"refusing to write a path containing a WTF segment: {folderRoot}");
        }

        lock (AddonUpdater.AddOnsWriteLock)
        {
            if (plan.WriteTexture)
            {
                WriteAtomic(Path.Combine(folderRoot, FileName), texture ?? throw new ArgumentNullException(nameof(texture)));
            }

            var directive = $"## IconTexture: {TexturePath(folderName)}";
            foreach (var toc in plan.TocsToEdit)
            {
                var target = Path.GetFullPath(toc);
                if (!string.Equals(Path.GetDirectoryName(target) + Path.DirectorySeparatorChar, folderRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"refusing to edit a TOC outside {folderRoot}: {target}");
                }

                WriteAtomic(target, InsertDirective(File.ReadAllBytes(target), directive));
            }
        }
    }

    public static byte[] InsertDirective(byte[] toc, string directive)
    {
        ArgumentNullException.ThrowIfNull(toc);
        var bomLength = toc.AsSpan().StartsWith("﻿"u8) ? 3 : 0;
        var newline = toc.AsSpan().IndexOf("\r\n"u8) >= 0 ? "\r\n"u8.ToArray() : "\n"u8.ToArray();

        var insertAt = bomLength;
        var terminated = true;
        for (var lineStart = bomLength; lineStart < toc.Length;)
        {
            var lf = Array.IndexOf(toc, (byte)'\n', lineStart);
            var lineEnd = lf < 0 ? toc.Length : lf + 1;
            if (toc.AsSpan(lineStart).StartsWith("##"u8))
            {
                insertAt = lineEnd;
                terminated = lf >= 0;
            }

            lineStart = lineEnd;
        }

        var line = Encoding.UTF8.GetBytes(directive);
        return terminated
            ? [.. toc.AsSpan(0, insertAt), .. line, .. newline, .. toc.AsSpan(insertAt)]
            : [.. toc, .. newline, .. line];
    }

    private static void WriteAtomic(string target, byte[] contents)
    {
        var tempPath = target + ".tmp";
        File.WriteAllBytes(tempPath, contents);
        File.Move(tempPath, target, overwrite: true);
    }
}
