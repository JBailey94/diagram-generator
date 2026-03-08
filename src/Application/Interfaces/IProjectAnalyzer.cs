using DiagramGenerator.Domain.Models;

namespace DiagramGenerator.Application.Interfaces;

public interface IProjectAnalyzer
{
    Task<DiagramModel> AnalyzeAsync(
        IReadOnlyList<string> projectPaths,
        CancellationToken cancellationToken = default
    );
}
