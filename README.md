# DiagramGenerator

Roslyn-powered Mermaid UML class diagram generator for C# solutions.

`DiagramGenerator` analyzes a `.slnx` solution, infers class relationships, and writes Mermaid `classDiagram` files you can render anywhere Mermaid is supported.

## Features

- Analyzes C# projects from a `.slnx` solution.
- Uses `Microsoft.CodeAnalysis` (Roslyn) for semantic analysis.
- Emits UML relationship types with labels:
- `inheritance`, `realization`, `dependency`, `association`, `aggregation`, `composition`.
- Supports compact output for large codebases.
- Supports namespace-split output files.
- Supports namespace include/exclude filtering.
- Supports dependency edge throttling for readability.
- Supports optional heuristic details in edge labels.
- Uses Serilog structured logs for diagnostics.

## Requirements

- .NET SDK 10.0 (project currently targets `net10.0`).
- A valid `.slnx` file as input.

## Quick Start (Release Build, No `dotnet run`)

### 1. Build Release

From repository root:

```powershell
dotnet build .\DiagramGenerator.slnx -c Release
```

Linux/macOS:

```bash
dotnet build ./DiagramGenerator.slnx -c Release
```

### 2. Run the compiled app

#### Windows (framework-dependent executable)

```powershell
.\src\bin\Release\net10.0\DiagramGenerator.exe .\DiagramGenerator.slnx -o .\generated
```

#### Linux (framework-dependent executable)

```bash
./src/bin/Release/net10.0/DiagramGenerator ./DiagramGenerator.slnx -o ./generated
```

#### Any OS (DLL host)

```bash
dotnet ./src/bin/Release/net10.0/DiagramGenerator.dll ./DiagramGenerator.slnx -o ./generated
```

## Publish a Portable Release Artifact

### Framework-dependent publish

```powershell
dotnet publish .\src\DiagramGenerator.csproj -c Release -o .\artifacts\publish
```

Linux/macOS:

```bash
dotnet publish ./src/DiagramGenerator.csproj -c Release -o ./artifacts/publish
```

Run:

```powershell
.\artifacts\publish\DiagramGenerator.exe .\DiagramGenerator.slnx -o .\generated
```

Linux/macOS run:

```bash
./artifacts/publish/DiagramGenerator ./DiagramGenerator.slnx -o ./generated
```

### Self-contained single-file publish (Windows x64)

```powershell
dotnet publish .\src\DiagramGenerator.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o .\artifacts\publish-win-x64
```

Run:

```powershell
.\artifacts\publish-win-x64\DiagramGenerator.exe .\DiagramGenerator.slnx -o .\generated
```

### Self-contained single-file publish (Linux x64)

```bash
dotnet publish ./src/DiagramGenerator.csproj -c Release -r linux-x64 --self-contained true /p:PublishSingleFile=true -o ./artifacts/publish-linux-x64
```

Run:

```bash
chmod +x ./artifacts/publish-linux-x64/DiagramGenerator
./artifacts/publish-linux-x64/DiagramGenerator ./DiagramGenerator.slnx -o ./generated
```

## CLI Usage

```text
DiagramGenerator <solution.slnx> -o <output-directory> [--compact] [--split-by-namespace] [--show-heuristics] [--hide-members] [--max-dependencies-per-type <n>] [--include-namespace <prefix>] [--exclude-namespace <prefix>]
```

### Arguments

- `<solution.slnx>`: Required. Path to the input solution.
- `-o`, `--output`: Required. Output directory for generated `.mmd` files.

### Options

- `--compact`: Simplifies output for readability.
- `--split-by-namespace`: Creates additional per-namespace diagram files.
- `--show-heuristics`: Appends confidence/evidence to relationship labels.
- `--hide-members`: Hides properties/fields/methods and shows type-level architecture only.
- `--max-dependencies-per-type <n>`: Limits dependency edges per source type.
- `--include-namespace <prefix>`: Include only types whose namespace starts with prefix.
- Can be repeated or comma-separated.
- `--exclude-namespace <prefix>`: Exclude types whose namespace starts with prefix.
- Can be repeated or comma-separated.

## Examples

### Basic

```powershell
.\src\bin\Release\net10.0\DiagramGenerator.exe .\MySolution.slnx -o .\diagrams
```

### Large solution, readability-focused

```powershell
.\src\bin\Release\net10.0\DiagramGenerator.exe .\MySolution.slnx -o .\diagrams --compact --split-by-namespace --hide-members --max-dependencies-per-type 3
```

### Domain-focused view only

```powershell
.\src\bin\Release\net10.0\DiagramGenerator.exe .\MySolution.slnx -o .\diagrams --compact --include-namespace MyCompany.Domain --exclude-namespace MyCompany.Domain.Tests
```

### Audit inferred relationships

```powershell
.\src\bin\Release\net10.0\DiagramGenerator.exe .\MySolution.slnx -o .\diagrams --compact --show-heuristics
```

## Output Files

By default:

- `<SolutionName>.mmd`

With `--split-by-namespace` enabled:

- `<SolutionName>.<NamespaceGroup>.mmd` (for each namespace group)

The generator also removes stale split files from previous runs when split output is enabled.

## Use From Another Project (Auto-Generate In Development)

If you want another solution/project to regenerate diagrams automatically during local development, the most reliable approach is:

1. Build/publish `DiagramGenerator` once.
2. Call the compiled executable from an MSBuild target that runs only for Debug (or when an opt-in property is set).

This works on Linux too; use an OS-specific path for `DiagramGeneratorExe`.

### Example: call published executable from your app `.csproj`

Add this to the consuming project file:

```xml
<PropertyGroup>
	<!-- Set path to your compiled DiagramGenerator executable -->
	<DiagramGeneratorExe Condition="'$(OS)'=='Windows_NT'">C:\tools\DiagramGenerator\DiagramGenerator.exe</DiagramGeneratorExe>
	<DiagramGeneratorExe Condition="'$(OS)'!='Windows_NT'">/opt/diagramgenerator/DiagramGenerator</DiagramGeneratorExe>

	<!-- Optional: path to the solution you want analyzed -->
	<DiagramGeneratorSolution>$(SolutionDir)MySolution.slnx</DiagramGeneratorSolution>

	<!-- Optional: output folder for diagrams -->
	<DiagramGeneratorOutput>$(SolutionDir)generated</DiagramGeneratorOutput>

	<!-- Enable in Debug by default; can be overridden via /p:GenerateDiagrams=true|false -->
	<GenerateDiagrams Condition="'$(GenerateDiagrams)'=='' and '$(Configuration)'=='Debug'">true</GenerateDiagrams>
</PropertyGroup>

<Target Name="GenerateUmlDiagrams" AfterTargets="Build" Condition="'$(GenerateDiagrams)'=='true' and Exists('$(DiagramGeneratorExe)') and Exists('$(DiagramGeneratorSolution)')">
	<Message Importance="high" Text="Generating Mermaid UML diagrams..." />
	<Exec Command="&quot;$(DiagramGeneratorExe)&quot; &quot;$(DiagramGeneratorSolution)&quot; -o &quot;$(DiagramGeneratorOutput)&quot; --compact --split-by-namespace --hide-members" />
</Target>
```

### Notes

- This runs after each build when enabled.
- For CI or Release builds, disable with:

```powershell
dotnet build .\MySolution.slnx -c Release /p:GenerateDiagrams=false
```

- You can keep diagrams optional for developers by not committing the executable path and passing it from environment-specific MSBuild props.
- On Linux, make sure the executable has permission to run: `chmod +x /opt/diagramgenerator/DiagramGenerator`.

## Relationship Heuristics (Summary)

- Inheritance and interface realization come from semantic type hierarchy.
- Field/property references infer structural relationships.
- Collection fields/properties infer aggregation.
- Private readonly concrete fields are treated as high-confidence composition.
- Method signature type usage infers dependency (lower confidence).

When `--show-heuristics` is enabled, labels include confidence and evidence.

## Troubleshooting

### Build warnings about MSBuild packages

You may see vulnerability warnings from transitive MSBuild dependencies used by Roslyn workspace loading. Current behavior is non-blocking.

### No output or empty diagrams

- Verify input path points to a valid `.slnx`.
- Confirm projects are C# projects and restore/build successfully.
- Remove restrictive filters (`--include-namespace`, `--exclude-namespace`) and re-run.

### Logs

Serilog logs progress and diagnostics to console. Typical milestones:

- solution loaded
- project count discovered
- analysis type/relationship counts
- file write paths

## Development

### Run tests/build

```powershell
dotnet build .\DiagramGenerator.slnx
```

### Local debug run

```powershell
dotnet run --project .\src\DiagramGenerator.csproj -- .\DiagramGenerator.slnx -o .\generated --compact --split-by-namespace
```

Linux/macOS:

```bash
dotnet run --project ./src/DiagramGenerator.csproj -- ./DiagramGenerator.slnx -o ./generated --compact --split-by-namespace
```
