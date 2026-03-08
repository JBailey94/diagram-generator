using DiagramGenerator.Application.Interfaces;
using DiagramGenerator.Application.Services;
using DiagramGenerator.Infrastructure.IO;
using DiagramGenerator.Infrastructure.Logging;
using DiagramGenerator.Infrastructure.Mermaid;
using DiagramGenerator.Infrastructure.Roslyn;
using Serilog;

var logger = LoggingConfiguration.CreateLogger();
Log.Logger = logger;

try
{
    if (!CliArgumentParser.TryParse(args, out var options, out var parseError))
    {
        Console.Error.WriteLine(parseError);
        Console.Error.WriteLine();
        Console.Error.WriteLine(CliArgumentParser.Usage);
        return 1;
    }

    ISolutionLoader solutionLoader = new SlnxSolutionLoader(
        logger.ForContext<SlnxSolutionLoader>()
    );
    IProjectAnalyzer analyzer = new RoslynProjectAnalyzer(
        logger.ForContext<RoslynProjectAnalyzer>()
    );
    IMermaidRenderer renderer = new MermaidClassDiagramRenderer();
    IOutputWriter outputWriter = new FileOutputWriter(logger.ForContext<FileOutputWriter>());
    IDiagramGenerationService service = new DiagramGenerationService(
        solutionLoader,
        analyzer,
        renderer,
        outputWriter,
        logger.ForContext<DiagramGenerationService>()
    );

    await service.GenerateAsync(options);
    return 0;
}
catch (Exception ex)
{
    logger.Fatal(ex, "Diagram generation failed unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
