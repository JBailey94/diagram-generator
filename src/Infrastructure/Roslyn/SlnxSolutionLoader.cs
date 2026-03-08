using System.Xml.Linq;
using DiagramGenerator.Application.Interfaces;
using Serilog;

namespace DiagramGenerator.Infrastructure.Roslyn;

public sealed class SlnxSolutionLoader : ISolutionLoader
{
    private readonly ILogger _logger;

    public SlnxSolutionLoader(ILogger logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<string>> GetProjectPathsAsync(
        string solutionPath,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.Debug("Loading solution file {SolutionPath}", solutionPath);
        var document = XDocument.Load(solutionPath);

        var solutionDirectory =
            Path.GetDirectoryName(solutionPath) ?? Directory.GetCurrentDirectory();
        var projects = document
            .Descendants("Project")
            .Select(x => x.Attribute("Path")?.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => Path.GetFullPath(Path.Combine(solutionDirectory, x!)))
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (projects.Count == 0)
        {
            throw new InvalidOperationException(
                $"No C# projects were found in solution file: {solutionPath}"
            );
        }

        _logger.Information("Resolved {ProjectCount} C# project(s) from solution", projects.Count);
        return Task.FromResult<IReadOnlyList<string>>(projects);
    }
}
