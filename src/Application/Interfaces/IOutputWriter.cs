namespace DiagramGenerator.Application.Interfaces;

public interface IOutputWriter
{
    Task<string> WriteAsync(
        string outputDirectory,
        string fileName,
        string content,
        CancellationToken cancellationToken = default
    );
}
