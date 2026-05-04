using CodeGraphMcp.Core.Domain;
using Microsoft.Extensions.Logging;

namespace CodeGraphMcp.Core.Parsing;

public sealed class JavaScriptParser(NodeProcessRunner runner, ILogger<JavaScriptParser> _logger) : ILanguageParser
{
    public Language Language => Language.JavaScript;

    public async Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
    {
        _logger.LogDebug("Parsing JavaScript file {File}", filePath);
        return await runner.ParseAsync(filePath, ct);
    }
}
