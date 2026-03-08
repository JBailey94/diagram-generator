using DiagramGenerator.Application.Interfaces;
using DiagramGenerator.Application.Models;
using DiagramGenerator.Domain.Models;
using Serilog;

namespace DiagramGenerator.Application.Services;

public sealed class DiagramGenerationService : IDiagramGenerationService
{
    private readonly ISolutionLoader _solutionLoader;
    private readonly IProjectAnalyzer _projectAnalyzer;
    private readonly IMermaidRenderer _renderer;
    private readonly IOutputWriter _outputWriter;
    private readonly ILogger _logger;

    public DiagramGenerationService(
        ISolutionLoader solutionLoader,
        IProjectAnalyzer projectAnalyzer,
        IMermaidRenderer renderer,
        IOutputWriter outputWriter,
        ILogger logger
    )
    {
        _solutionLoader = solutionLoader;
        _projectAnalyzer = projectAnalyzer;
        _renderer = renderer;
        _outputWriter = outputWriter;
        _logger = logger;
    }

    public async Task GenerateAsync(
        GenerationOptions options,
        CancellationToken cancellationToken = default
    )
    {
        var startedAt = DateTimeOffset.UtcNow;
        var scope = _logger
            .ForContext("SolutionPath", options.SolutionPath)
            .ForContext("OutputDirectory", options.OutputDirectory)
            .ForContext("Compact", options.Compact)
            .ForContext("SplitByNamespace", options.SplitByNamespace)
            .ForContext("ShowHeuristics", options.ShowHeuristics)
            .ForContext("HideMembers", options.HideMembers)
            .ForContext("MaxDependenciesPerType", options.MaxDependenciesPerType);

        scope.Information("Starting diagram generation");

        var projectPaths = await _solutionLoader.GetProjectPathsAsync(
            options.SolutionPath,
            cancellationToken
        );
        scope.Information("Found {ProjectCount} project(s) in solution", projectPaths.Count);

        var model = await _projectAnalyzer.AnalyzeAsync(projectPaths, cancellationToken);
        scope.Information(
            "Analysis complete: {TypeCount} type(s), {RelationshipCount} relationship(s)",
            model.Types.Count,
            model.Relationships.Count
        );

        var transformedModel = TransformForOutput(model, options);
        var renderOptions = new MermaidRenderOptions(
            options.Compact,
            TopToBottom: true,
            ShowHeuristics: options.ShowHeuristics
        );
        var outputBaseName = Path.GetFileNameWithoutExtension(options.SolutionPath);

        var outputFileName = $"{outputBaseName}.mmd";
        var mermaid = _renderer.Render(transformedModel, renderOptions);

        var outputPath = await _outputWriter.WriteAsync(
            options.OutputDirectory,
            outputFileName,
            mermaid,
            cancellationToken
        );

        if (options.SplitByNamespace)
        {
            var namespaceDiagrams = SplitByNamespace(transformedModel).ToList();
            var expectedSplitFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            scope.Information(
                "Generating {NamespaceCount} namespace-split diagram(s)",
                namespaceDiagrams.Count
            );

            foreach (var (namespaceName, namespaceModel) in namespaceDiagrams)
            {
                var suffix = MakeFileSafeNamespaceSuffix(namespaceName);
                var fileName = $"{outputBaseName}.{suffix}.mmd";
                expectedSplitFiles.Add(fileName);
                var namespaceMermaid = _renderer.Render(namespaceModel, renderOptions);
                await _outputWriter.WriteAsync(
                    options.OutputDirectory,
                    fileName,
                    namespaceMermaid,
                    cancellationToken
                );
            }

            DeleteStaleSplitFiles(
                options.OutputDirectory,
                outputBaseName,
                expectedSplitFiles,
                scope
            );
        }
        var elapsedMs = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;

        scope
            .ForContext("ElapsedMs", elapsedMs)
            .Information(
                "Diagram generation completed successfully. Output: {OutputPath}",
                outputPath
            );
    }

    private static DiagramModel TransformForOutput(DiagramModel model, GenerationOptions options)
    {
        var filteredModel = FilterByNamespace(
            model,
            options.IncludeNamespacePrefixes,
            options.ExcludeNamespacePrefixes
        );

        var types = filteredModel
            .Types.Select(type =>
                type with
                {
                    Members =
                        options.HideMembers ? []
                        : options.Compact
                            ? type
                                .Members.Where(m =>
                                    m.Visibility
                                        is VisibilityModel.Public
                                            or VisibilityModel.Protected
                                )
                                .ToList()
                        : type.Members,
                }
            )
            .ToList();

        var relationships = options.Compact
            ? CompactRelationships(filteredModel.Relationships)
            : filteredModel.Relationships;

        if (options.MaxDependenciesPerType is int maxDeps)
        {
            relationships = LimitDependenciesPerType(relationships, maxDeps);
        }

        return new DiagramModel(types, relationships);
    }

    private static DiagramModel FilterByNamespace(
        DiagramModel model,
        IReadOnlyList<string> includePrefixes,
        IReadOnlyList<string> excludePrefixes
    )
    {
        var filteredTypes = model
            .Types.Where(t =>
                ShouldIncludeNamespace(t.NamespaceName, includePrefixes, excludePrefixes)
            )
            .ToList();

        var keptIds = filteredTypes.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var filteredRelationships = model
            .Relationships.Where(r =>
                keptIds.Contains(r.SourceTypeId) && keptIds.Contains(r.TargetTypeId)
            )
            .ToHashSet();

        return new DiagramModel(filteredTypes, filteredRelationships);
    }

    private static bool ShouldIncludeNamespace(
        string namespaceName,
        IReadOnlyList<string> includePrefixes,
        IReadOnlyList<string> excludePrefixes
    )
    {
        var included =
            includePrefixes.Count == 0
            || includePrefixes.Any(prefix =>
                namespaceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            );

        if (!included)
        {
            return false;
        }

        var excluded = excludePrefixes.Any(prefix =>
            namespaceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        );

        return !excluded;
    }

    private static IReadOnlySet<RelationshipModel> LimitDependenciesPerType(
        IReadOnlySet<RelationshipModel> relationships,
        int maxDependenciesPerType
    )
    {
        var result = relationships
            .Where(r => r.Kind != RelationshipKindModel.Dependency)
            .ToHashSet();

        foreach (
            var dependencyGroup in relationships
                .Where(r => r.Kind == RelationshipKindModel.Dependency)
                .GroupBy(r => r.SourceTypeId)
        )
        {
            foreach (
                var dependency in dependencyGroup
                    .OrderBy(r => r.TargetTypeId, StringComparer.Ordinal)
                    .Take(maxDependenciesPerType)
            )
            {
                result.Add(dependency);
            }
        }

        return result;
    }

    private static IReadOnlySet<RelationshipModel> CompactRelationships(
        IReadOnlySet<RelationshipModel> relationships
    )
    {
        var grouped = relationships.GroupBy(r => (r.SourceTypeId, r.TargetTypeId)).ToList();

        var result = new HashSet<RelationshipModel>();

        foreach (var group in grouped)
        {
            var kinds = group.Select(g => g.Kind).ToHashSet();

            if (kinds.Contains(RelationshipKindModel.Inheritance))
            {
                var best = SelectBest(group, RelationshipKindModel.Inheritance);
                if (best is not null)
                {
                    result.Add(best);
                }
                continue;
            }

            if (kinds.Contains(RelationshipKindModel.Realization))
            {
                var best = SelectBest(group, RelationshipKindModel.Realization);
                if (best is not null)
                {
                    result.Add(best);
                }
                continue;
            }

            // Drop method-only dependency noise in compact mode unless it is the only relation available.
            var structural = group
                .Where(r =>
                    r.Kind
                        is RelationshipKindModel.Composition
                            or RelationshipKindModel.Aggregation
                            or RelationshipKindModel.Association
                )
                .OrderByDescending(r => GetRelationshipStrength(r.Kind))
                .FirstOrDefault();

            if (structural is not null)
            {
                result.Add(structural);
            }
            else if (kinds.Contains(RelationshipKindModel.Dependency))
            {
                var best = SelectBest(group, RelationshipKindModel.Dependency);
                if (best is not null)
                {
                    result.Add(best);
                }
            }
        }

        return result;

        static RelationshipModel? SelectBest(
            IEnumerable<RelationshipModel> relations,
            RelationshipKindModel kind
        )
        {
            return relations
                .Where(r => r.Kind == kind)
                .OrderByDescending(r => r.Confidence)
                .ThenBy(r => r.Evidence, StringComparer.Ordinal)
                .FirstOrDefault();
        }
    }

    private static int GetRelationshipStrength(RelationshipKindModel kind)
    {
        return kind switch
        {
            RelationshipKindModel.Composition => 3,
            RelationshipKindModel.Aggregation => 2,
            RelationshipKindModel.Association => 1,
            _ => 0,
        };
    }

    private static IEnumerable<(string NamespaceName, DiagramModel Model)> SplitByNamespace(
        DiagramModel model
    )
    {
        foreach (
            var group in model
                .Types.GroupBy(t => GetNamespaceGroup(t.NamespaceName))
                .OrderBy(g => g.Key, StringComparer.Ordinal)
        )
        {
            var groupTypes = group.OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();
            var ids = groupTypes.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);

            var groupRelationships = model
                .Relationships.Where(r =>
                    ids.Contains(r.SourceTypeId) && ids.Contains(r.TargetTypeId)
                )
                .ToHashSet();

            yield return (group.Key, new DiagramModel(groupTypes, groupRelationships));
        }
    }

    private static string GetNamespaceGroup(string namespaceName)
    {
        if (
            string.IsNullOrWhiteSpace(namespaceName)
            || namespaceName.StartsWith("<", StringComparison.Ordinal)
        )
        {
            return "Global";
        }

        var parts = namespaceName.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return $"{parts[0]}.{parts[1]}";
        }

        return parts[0];
    }

    private static string MakeFileSafeNamespaceSuffix(string namespaceName)
    {
        var safe = namespaceName.Replace(".", "_", StringComparison.Ordinal);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(invalid.ToString(), "_", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(safe) ? "Global" : safe;
    }

    private static void DeleteStaleSplitFiles(
        string outputDirectory,
        string outputBaseName,
        ISet<string> expectedSplitFiles,
        ILogger logger
    )
    {
        var splitFiles = Directory.EnumerateFiles(outputDirectory, $"{outputBaseName}.*.mmd");
        foreach (var path in splitFiles)
        {
            var fileName = Path.GetFileName(path);
            if (!expectedSplitFiles.Contains(fileName))
            {
                File.Delete(path);
                logger.Debug("Removed stale split artifact {FileName}", fileName);
            }
        }
    }
}
