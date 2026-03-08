using DiagramGenerator.Application.Models;

namespace DiagramGenerator.Application.Services;

public static class CliArgumentParser
{
    public const string Usage =
        "Usage: DiagramGenerator <solution.slnx> -o <output-directory> [--compact] [--split-by-namespace] [--show-heuristics] [--hide-members] [--max-dependencies-per-type <n>] [--include-namespace <prefix>] [--exclude-namespace <prefix>]";

    public static bool TryParse(string[] args, out GenerationOptions options, out string error)
    {
        options = null!;
        error = string.Empty;

        if (args.Length < 3)
        {
            error = "Missing required arguments.";
            return false;
        }

        var solutionPath = args[0];
        if (!solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            error = "The input file must be a .slnx file.";
            return false;
        }

        if (!File.Exists(solutionPath))
        {
            error = $"Solution file not found: {solutionPath}";
            return false;
        }

        var outputArgIndex = Array.FindIndex(
            args,
            a =>
                string.Equals(a, "-o", StringComparison.OrdinalIgnoreCase)
                || string.Equals(a, "--output", StringComparison.OrdinalIgnoreCase)
        );
        if (outputArgIndex < 0 || outputArgIndex + 1 >= args.Length)
        {
            error = "Missing output directory. Use -o <output-directory>.";
            return false;
        }

        var outputDirectory = args[outputArgIndex + 1];
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            error = "Output directory cannot be empty.";
            return false;
        }

        var compact = args.Any(a =>
            string.Equals(a, "--compact", StringComparison.OrdinalIgnoreCase)
        );
        var splitByNamespace = args.Any(a =>
            string.Equals(a, "--split-by-namespace", StringComparison.OrdinalIgnoreCase)
        );
        var showHeuristics = args.Any(a =>
            string.Equals(a, "--show-heuristics", StringComparison.OrdinalIgnoreCase)
        );
        var hideMembers = args.Any(a =>
            string.Equals(a, "--hide-members", StringComparison.OrdinalIgnoreCase)
        );

        if (
            !TryParseIntOption(
                args,
                "--max-dependencies-per-type",
                out var maxDependencies,
                out error
            )
        )
        {
            return false;
        }

        if (maxDependencies is <= 0)
        {
            error = "--max-dependencies-per-type must be greater than zero.";
            return false;
        }

        var includeNamespacePrefixes = ParseNamespacePrefixes(args, "--include-namespace");
        var excludeNamespacePrefixes = ParseNamespacePrefixes(args, "--exclude-namespace");

        options = new GenerationOptions(
            Path.GetFullPath(solutionPath),
            Path.GetFullPath(outputDirectory),
            compact,
            splitByNamespace,
            showHeuristics,
            hideMembers,
            maxDependencies,
            includeNamespacePrefixes,
            excludeNamespacePrefixes
        );
        return true;
    }

    private static bool TryParseIntOption(
        string[] args,
        string optionName,
        out int? value,
        out string error
    )
    {
        value = null;
        error = string.Empty;

        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Length)
            {
                error = $"Missing value for {optionName}.";
                return false;
            }

            if (!int.TryParse(args[i + 1], out var parsed))
            {
                error = $"Invalid integer value for {optionName}: {args[i + 1]}";
                return false;
            }

            value = parsed;
            i++;
        }

        return true;
    }

    private static IReadOnlyList<string> ParseNamespacePrefixes(string[] args, string optionName)
    {
        var values = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Length)
            {
                break;
            }

            var raw = args[i + 1];
            values.AddRange(
                raw.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            );
            i++;
        }

        return values;
    }
}
