namespace DiagramGenerator.Application.Interfaces;

public interface ISolutionLoader
{
    Task<IReadOnlyList<string>> GetProjectPathsAsync(
        string solutionPath,
        CancellationToken cancellationToken = default
    );
}
