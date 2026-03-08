using DiagramGenerator.Application.Models;

namespace DiagramGenerator.Application.Interfaces;

public interface IDiagramGenerationService
{
    Task GenerateAsync(GenerationOptions options, CancellationToken cancellationToken = default);
}
