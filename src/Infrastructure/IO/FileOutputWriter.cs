using DiagramGenerator.Application.Interfaces;
using Serilog;

namespace DiagramGenerator.Infrastructure.IO;

public sealed class FileOutputWriter : IOutputWriter
{
    private readonly ILogger _logger;

    public FileOutputWriter(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<string> WriteAsync(
        string outputDirectory,
        string fileName,
        string content,
        CancellationToken cancellationToken = default
    )
    {
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, fileName);

        _logger.Debug("Writing Mermaid output to {OutputPath}", path);
        await File.WriteAllTextAsync(path, content, cancellationToken);

        return path;
    }
}
