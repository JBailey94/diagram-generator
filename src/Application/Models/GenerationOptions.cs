namespace DiagramGenerator.Application.Models;

public sealed record GenerationOptions(
    string SolutionPath,
    string OutputDirectory,
    bool Compact,
    bool SplitByNamespace,
    bool ShowHeuristics,
    bool HideMembers,
    int? MaxDependenciesPerType,
    IReadOnlyList<string> IncludeNamespacePrefixes,
    IReadOnlyList<string> ExcludeNamespacePrefixes
);
