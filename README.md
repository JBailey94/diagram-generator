# DiagramGenerator

Roslyn-powered Mermaid UML class diagram generator for C# `.slnx` solutions.

This README is release-first: use the prebuilt binaries from GitHub Releases instead of building from source.

## TL;DR

1. Download release zip (`win-x64` or `linux-x64`).
2. Run the binary against your `.slnx`.
3. Open generated `.mmd` files.

Windows:

```powershell
.\DiagramGenerator.exe .\MySolution.slnx -o .\generated --compact --split-by-namespace
```

Linux:

```bash
chmod +x ./DiagramGenerator
./DiagramGenerator ./MySolution.slnx -o ./generated --compact --split-by-namespace
```

## Install

From the latest GitHub Release, download one of these assets:

- `DiagramGenerator-win-x64.zip`
- `DiagramGenerator-linux-x64.zip`

## Usage

### Windows

1. Extract `DiagramGenerator-win-x64.zip`.
2. Run from the folder containing `DiagramGenerator.exe`:

```powershell
.\DiagramGenerator.exe .\MySolution.slnx -o .\generated
```

### Linux

1. Extract `DiagramGenerator-linux-x64.zip`.
2. Make executable (once):

```bash
chmod +x ./DiagramGenerator
```

3. Run:

```bash
./DiagramGenerator ./MySolution.slnx -o ./generated
```

### Common Commands

Readable architecture view:

Windows:

```powershell
.\DiagramGenerator.exe .\MySolution.slnx -o .\generated --compact --split-by-namespace --hide-members --max-dependencies-per-type 3
```

Linux:

```bash
./DiagramGenerator ./MySolution.slnx -o ./generated --compact --split-by-namespace --hide-members --max-dependencies-per-type 3
```

Show heuristic confidence/evidence:

```text
--show-heuristics
```

Focus on specific namespaces:

```text
--include-namespace MyCompany.Domain --exclude-namespace MyCompany.Domain.Tests
```

## CLI Reference

```text
DiagramGenerator <solution.slnx> -o <output-directory> [--compact] [--split-by-namespace] [--show-heuristics] [--hide-members] [--max-dependencies-per-type <n>] [--include-namespace <prefix>] [--exclude-namespace <prefix>]
```

Arguments:

- `<solution.slnx>`: required input solution.
- `-o`, `--output`: required output directory.

Options:

- `--compact`: Simplifies output for readability.
- `--split-by-namespace`: Creates additional per-namespace diagrams.
- `--show-heuristics`: Appends confidence/evidence to relationship labels.
- `--hide-members`: Hides type members for architecture-only diagrams.
- `--max-dependencies-per-type <n>`: Caps dependency edges per source type.
- `--include-namespace <prefix>`: Include only namespaces starting with prefix.
- Can be repeated or comma-separated.
- `--exclude-namespace <prefix>`: Exclude namespaces starting with prefix.
- Can be repeated or comma-separated.

## Output

- Main file: `<SolutionName>.mmd`
- With split mode: `<SolutionName>.<NamespaceGroup>.mmd`

When `--split-by-namespace` is used, stale split files from previous runs are automatically removed.

## Dev Integration

Add this to your consuming project's `.csproj` (or `Directory.Build.targets`) to run after Debug builds.

```xml
<PropertyGroup>
  <DiagramGeneratorExe Condition="'$(OS)'=='Windows_NT'">C:\tools\DiagramGenerator\DiagramGenerator.exe</DiagramGeneratorExe>
  <DiagramGeneratorExe Condition="'$(OS)'!='Windows_NT'">/opt/diagramgenerator/DiagramGenerator</DiagramGeneratorExe>
  <DiagramGeneratorSolution>$(SolutionDir)MySolution.slnx</DiagramGeneratorSolution>
  <DiagramGeneratorOutput>$(SolutionDir)generated</DiagramGeneratorOutput>
  <GenerateDiagrams Condition="'$(GenerateDiagrams)'=='' and '$(Configuration)'=='Debug'">true</GenerateDiagrams>
</PropertyGroup>

<Target Name="GenerateUmlDiagrams" AfterTargets="Build" Condition="'$(GenerateDiagrams)'=='true' and Exists('$(DiagramGeneratorExe)') and Exists('$(DiagramGeneratorSolution)')">
  <Exec Command="&quot;$(DiagramGeneratorExe)&quot; &quot;$(DiagramGeneratorSolution)&quot; -o &quot;$(DiagramGeneratorOutput)&quot; --compact --split-by-namespace --hide-members" />
</Target>
```

Tips:

- Disable on demand: `dotnet build /p:GenerateDiagrams=false`
- Linux binary usually needs execute permission:

```bash
chmod +x /opt/diagramgenerator/DiagramGenerator
```

## FAQ

### Should I add this to `.slnx`?

No. Add the target to a `.csproj` or `Directory.Build.targets`. That is where MSBuild build hooks belong.

### Why do I only get one `.mmd` file?

That is expected unless `--split-by-namespace` is enabled.

### Why do dependency lines still look busy?

Use a stricter command:

```text
--compact --hide-members --max-dependencies-per-type 2 --split-by-namespace
```

You can also scope with `--include-namespace` and `--exclude-namespace`.

## Build From Source (Optional)

```bash
dotnet build ./DiagramGenerator.slnx -c Release
```

Local dev run:

```bash
dotnet run --project ./src/DiagramGenerator.csproj -- ./DiagramGenerator.slnx -o ./generated --compact --split-by-namespace
```
