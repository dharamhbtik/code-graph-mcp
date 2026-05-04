using CodeGraphMcp.Core.Domain;
using Microsoft.Extensions.Logging;

namespace CodeGraphMcp.Core.Parsing;

public sealed class AngularParser(NodeProcessRunner runner, ILogger<AngularParser> _logger) : ILanguageParser
{
    public Language Language => Language.Angular;

    public async Task<ParseResult> ParseAsync(string filePath, CancellationToken ct = default)
    {
        _logger.LogDebug("Parsing Angular file {File}", filePath);
        return await runner.ParseAsync(filePath, ct);
    }
}
