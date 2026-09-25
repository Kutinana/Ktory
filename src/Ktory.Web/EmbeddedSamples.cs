using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Ktory.Web;

public static class EmbeddedSamples
{
    private static readonly Lazy<Dictionary<string, string>> _catalog = new(LoadCatalog);

    public static IReadOnlyDictionary<string, string> Catalog => _catalog.Value;

    private static Dictionary<string, string> LoadCatalog()
    {
        var result = new Dictionary<string, string>();
        var assembly = typeof(EmbeddedSamples).Assembly;
        var names = assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".ktr", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".ktory", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n);

        foreach (var name in names)
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream == null) continue;
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();
            var fallback = Path.GetFileNameWithoutExtension(name);
            var title = ExtractTitle(content, fallback);
            result[title] = content;
        }

        return result;
    }

    private static string ExtractTitle(string content, string fallback)
    {
        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.StartsWith("//") && line.Contains("title:", StringComparison.OrdinalIgnoreCase))
            {
                var idx = line.IndexOf("title:", StringComparison.OrdinalIgnoreCase);
                var title = line.Substring(idx + 6).Trim();
                if (!string.IsNullOrEmpty(title))
                {
                    return title;
                }
            }
            if (!string.IsNullOrEmpty(line) && !line.StartsWith("//"))
            {
                break;
            }
        }
        return fallback;
    }
}
