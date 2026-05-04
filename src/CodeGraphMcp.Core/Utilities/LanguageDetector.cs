namespace CodeGraphMcp.Core.Utilities;

using CodeGraphMcp.Core.Domain;

public static class LanguageDetector
{
    private static readonly Dictionary<string, Language> _map = new(StringComparer.OrdinalIgnoreCase)
    {
        // .NET
        [".cs"]      = Language.CSharp,
        [".xaml"]    = Language.Xaml,

        // JVM
        [".java"]    = Language.Java,
        [".kt"]      = Language.Kotlin,
        [".kts"]     = Language.Kotlin,

        // Apple
        [".swift"]   = Language.Swift,
        [".m"]       = Language.ObjectiveC,
        [".mm"]      = Language.ObjectiveC,
        [".h"]       = Language.C,           // Could be C, C++, or Obj-C — default to C

        // Systems
        [".c"]       = Language.C,
        [".cpp"]     = Language.Cpp,
        [".cxx"]     = Language.Cpp,
        [".cc"]      = Language.Cpp,
        [".hpp"]     = Language.Cpp,
        [".hxx"]     = Language.Cpp,

        // Web / JS ecosystem
        [".js"]      = Language.JavaScript,
        [".mjs"]     = Language.JavaScript,
        [".cjs"]     = Language.JavaScript,
        [".jsx"]     = Language.JavaScript,
        [".ts"]      = Language.TypeScript,
        [".tsx"]     = Language.TypeScript,

        // Server-side
        [".php"]     = Language.Php,
        [".go"]      = Language.Go,
        [".rs"]      = Language.Rust,
        [".py"]      = Language.Python,
        [".rb"]      = Language.Ruby,
        [".dart"]    = Language.Dart,

        // Data / Config
        [".json"]    = Language.Json,
        [".sql"]     = Language.Sql,
        [".yaml"]    = Language.Yaml,
        [".yml"]     = Language.Yaml,

        // Markup / Style
        [".md"]      = Language.Markdown,
        [".html"]    = Language.Html,
        [".htm"]     = Language.Html,
        [".css"]     = Language.Css,
        [".scss"]    = Language.Scss,

        // Shell
        [".sh"]      = Language.Shell,
        [".bash"]    = Language.Shell,
        [".zsh"]     = Language.Shell,

        // Project files
        [".csproj"]  = Language.ProjectFile,
        [".sln"]     = Language.ProjectFile,
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
