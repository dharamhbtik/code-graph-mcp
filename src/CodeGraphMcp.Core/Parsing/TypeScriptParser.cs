using CodeGraphMcp.Core.Domain;
using Microsoft.Extensions.Logging;

namespace CodeGraphMcp.Core.Parsing;

public sealed class TypeScriptParser(NodeProcessRunner runner, ILogger<TypeScriptParser> _logger) : ILanguageParser
{
    public Language Language => Language.TypeScript;

    public async Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
    {
        _logger.LogDebug("Parsing TypeScript file {File}", filePath);
        return await runner.ParseAsync(filePath, ct);
    }
}
