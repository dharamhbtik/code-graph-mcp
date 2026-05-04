namespace CodeGraphMcp.Core.Utilities;

using CodeGraphMcp.Core.Domain;

public static class LanguageDetector
{
    private static readonly Dictionary<string, Language> _map = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"]     = Language.CSharp,
        [".xaml"]   = Language.Xaml,
        [".js"]     = Language.JavaScript,
        [".mjs"]    = Language.JavaScript,
        [".cjs"]    = Language.JavaScript,
        [".ts"]     = Language.TypeScript,
        [".tsx"]    = Language.TypeScript,
        [".json"]   = Language.Json,
        [".sql"]    = Language.Sql,
        [".md"]     = Language.Markdown,
        [".csproj"] = Language.ProjectFile,
        [".sln"]    = Language.ProjectFile,
    };

    public static Language Detect(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (_map.TryGetValue(ext, out var lang)) return lang;

        // Angular-specific: check for .component.ts, .module.ts, .service.ts
        var name = Path.GetFileName(filePath);
        if (name.EndsWith(".component.ts", StringComparison.OrdinalIgnoreCase)) return Language.Angular;
        if (name.EndsWith(".module.ts", StringComparison.OrdinalIgnoreCase)) return Language.Angular;
        if (name.EndsWith(".service.ts", StringComparison.OrdinalIgnoreCase)) return Language.Angular;

        return Language.Unknown;
    }

    public static bool IsTracked(string filePath)
        => Detect(filePath) != Language.Unknown;
}
