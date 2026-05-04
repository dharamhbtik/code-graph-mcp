using CodeGraphMcp.Core.Graph;
using CodeGraphMcp.Core.Orchestration;
using CodeGraphMcp.Core.Parsing;
using CodeGraphMcp.Core.Scanning;
using CodeGraphMcp.Watcher;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeGraphMcp.Startup;

public sealed class AppSettings
{
    public string RootPath            { get; set; } = string.Empty;
    public string DbPath              { get; set; } = "codegraph.db";
    public int    MaxTokens           { get; set; } = 80_000;
    public int    DebounceMs          { get; set; } = 800;
    public int    MaxParserConcurrency { get; set; } = 8;
    public bool   EnableWatcher       { get; set; } = true;
}

public static class ServiceRegistration
{
    public static IServiceCollection AddCodeGraphMcp(
        this IServiceCollection services,
        string rootPath,
        string dbPath)
    {
        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<GraphStore>>();
            return new GraphStore(dbPath, logger);
        });

        services.AddSingleton<RepositoryScanner>();
        services.AddSingleton<NodeProcessRunner>();
        services.AddSingleton<RegexParser>();

        // Register all parsers
        services.AddSingleton<ILanguageParser, CSharpParser>();
        services.AddSingleton<ILanguageParser, XamlParser>();
        services.AddSingleton<ILanguageParser, JavaScriptParser>();
        services.AddSingleton<ILanguageParser, TypeScriptParser>();
        services.AddSingleton<ILanguageParser, AngularParser>();
        services.AddSingleton<ILanguageParser, ProjectFileParser>();
        services.AddSingleton<ILanguageParser, MarkdownParser>();

        // Regex-based parsers for additional languages
        services.AddSingleton<ILanguageParser, JavaParser>();
        services.AddSingleton<ILanguageParser, KotlinParser>();
        services.AddSingleton<ILanguageParser, SwiftParser>();
        services.AddSingleton<ILanguageParser, CParser>();
        services.AddSingleton<ILanguageParser, CppParser>();
        services.AddSingleton<ILanguageParser, ObjectiveCParser>();
        services.AddSingleton<ILanguageParser, PhpParser>();
        services.AddSingleton<ILanguageParser, GoParser>();
        services.AddSingleton<ILanguageParser, RustParser>();
        services.AddSingleton<ILanguageParser, PythonParser>();
        services.AddSingleton<ILanguageParser, RubyParser>();
        services.AddSingleton<ILanguageParser, DartParser>();
        services.AddSingleton<ILanguageParser, SqlParser>();
        services.AddSingleton<ILanguageParser, HtmlParser>();
        services.AddSingleton<ILanguageParser, CssParser>();
        services.AddSingleton<ILanguageParser, ScssParser>();
        services.AddSingleton<ILanguageParser, ShellParser>();
        services.AddSingleton<ILanguageParser, YamlParser>();

        services.AddSingleton<GraphOrchestrator>();
        services.AddSingleton<FileChangeWatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<FileChangeWatcher>());

        services.AddSingleton(new AppSettings { RootPath = rootPath, DbPath = dbPath });

        return services;
    }
}
