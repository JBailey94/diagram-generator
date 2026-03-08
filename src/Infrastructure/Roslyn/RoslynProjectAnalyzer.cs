using DiagramGenerator.Application.Interfaces;
using DiagramGenerator.Domain.Models;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Serilog;

namespace DiagramGenerator.Infrastructure.Roslyn;

public sealed class RoslynProjectAnalyzer : IProjectAnalyzer
{
    private static readonly SymbolDisplayFormat FullTypeDisplay = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
    );

    private readonly ILogger _logger;

    public RoslynProjectAnalyzer(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<DiagramModel> AnalyzeAsync(
        IReadOnlyList<string> projectPaths,
        CancellationToken cancellationToken = default
    )
    {
        EnsureMsBuildRegistered();

        using var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (_, args) =>
        {
            _logger.Warning("Roslyn workspace warning: {Message}", args.Diagnostic.Message);
        };

        var symbolsById = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);

        foreach (var projectPath in projectPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.Information("Analyzing project {ProjectPath}", projectPath);
            var project = await workspace.OpenProjectAsync(
                projectPath,
                cancellationToken: cancellationToken
            );
            var compilation = await project.GetCompilationAsync(cancellationToken);

            if (compilation is null)
            {
                _logger.Warning(
                    "Skipping project {ProjectPath}: compilation could not be created",
                    projectPath
                );
                continue;
            }

            foreach (var type in EnumerateNamedTypes(compilation.Assembly.GlobalNamespace))
            {
                if (!ShouldIncludeType(type))
                {
                    continue;
                }

                var id = GetTypeId(type);
                symbolsById.TryAdd(id, type);
            }
        }

        var typeModels = new List<TypeModel>(symbolsById.Count);
        var relationships = new HashSet<RelationshipModel>();

        foreach (var pair in symbolsById.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceId = pair.Key;
            var symbol = pair.Value;

            typeModels.Add(CreateTypeModel(symbol, sourceId));
            ExtractRelationships(symbol, sourceId, symbolsById.Keys, relationships);
        }

        return new DiagramModel(typeModels, relationships);
    }

    private static void EnsureMsBuildRegistered()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }

    private static TypeModel CreateTypeModel(INamedTypeSymbol symbol, string typeId)
    {
        var members = new List<MemberModel>();

        foreach (var field in symbol.GetMembers().OfType<IFieldSymbol>())
        {
            if (field.IsImplicitlyDeclared)
            {
                continue;
            }

            var signature = $"{FormatTypeName(field.Type)} {field.Name}";
            members.Add(
                new MemberModel(
                    MemberKindModel.Field,
                    MapVisibility(field.DeclaredAccessibility),
                    signature
                )
            );
        }

        foreach (var property in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsImplicitlyDeclared)
            {
                continue;
            }

            var signature = $"{FormatTypeName(property.Type)} {property.Name}";
            members.Add(
                new MemberModel(
                    MemberKindModel.Property,
                    MapVisibility(property.DeclaredAccessibility),
                    signature
                )
            );
        }

        foreach (var method in symbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.IsImplicitlyDeclared)
            {
                continue;
            }

            if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor))
            {
                continue;
            }

            var parameters = string.Join(
                ", ",
                method.Parameters.Select(p => $"{FormatTypeName(p.Type)} {p.Name}")
            );

            if (method.MethodKind == MethodKind.Constructor)
            {
                var constructorSignature = $"{symbol.Name}({parameters})";
                members.Add(
                    new MemberModel(
                        MemberKindModel.Constructor,
                        MapVisibility(method.DeclaredAccessibility),
                        constructorSignature
                    )
                );
            }
            else
            {
                var methodName = method.Name;
                if (method.TypeParameters.Length > 0)
                {
                    methodName +=
                        $"<{string.Join(", ", method.TypeParameters.Select(p => p.Name))}>";
                }

                var signature = $"{FormatTypeName(method.ReturnType)} {methodName}({parameters})";
                members.Add(
                    new MemberModel(
                        MemberKindModel.Method,
                        MapVisibility(method.DeclaredAccessibility),
                        signature
                    )
                );
            }
        }

        return new TypeModel(
            typeId,
            GetNamespaceName(symbol),
            FormatTypeName(symbol),
            FormatShortTypeName(symbol),
            MapTypeKind(symbol.TypeKind),
            symbol.IsAbstract && symbol.TypeKind == Microsoft.CodeAnalysis.TypeKind.Class,
            members.OrderBy(m => m.Signature, StringComparer.Ordinal).ToList()
        );
    }

    private static string GetNamespaceName(INamedTypeSymbol symbol)
    {
        return symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
    }

    private static string FormatShortTypeName(INamedTypeSymbol symbol)
    {
        if (symbol.TypeParameters.Length == 0)
        {
            return symbol.Name;
        }

        return $"{symbol.Name}<{string.Join(", ", symbol.TypeParameters.Select(t => t.Name))}>";
    }

    private static void ExtractRelationships(
        INamedTypeSymbol symbol,
        string sourceId,
        IEnumerable<string> knownTypeIds,
        ISet<RelationshipModel> relationships
    )
    {
        var knownTypeSet = knownTypeIds is HashSet<string> hashSet
            ? hashSet
            : new HashSet<string>(knownTypeIds, StringComparer.Ordinal);

        AddInheritanceAndInterfaces(symbol, sourceId, knownTypeSet, relationships);
        AddFieldAndPropertyAssociations(symbol, sourceId, knownTypeSet, relationships);
        AddMethodAssociations(symbol, sourceId, knownTypeSet, relationships);
    }

    private static void AddInheritanceAndInterfaces(
        INamedTypeSymbol symbol,
        string sourceId,
        ISet<string> knownTypeSet,
        ISet<RelationshipModel> relationships
    )
    {
        if (symbol.BaseType is { SpecialType: SpecialType.None } baseType)
        {
            var baseId = GetTypeId(baseType);
            if (
                !string.Equals(sourceId, baseId, StringComparison.Ordinal)
                && knownTypeSet.Contains(baseId)
            )
            {
                relationships.Add(
                    new RelationshipModel(
                        sourceId,
                        baseId,
                        RelationshipKindModel.Inheritance,
                        RelationshipConfidenceModel.High,
                        "base type"
                    )
                );
            }
        }

        foreach (var iface in symbol.Interfaces)
        {
            var ifaceId = GetTypeId(iface);
            if (
                !string.Equals(sourceId, ifaceId, StringComparison.Ordinal)
                && knownTypeSet.Contains(ifaceId)
            )
            {
                relationships.Add(
                    new RelationshipModel(
                        sourceId,
                        ifaceId,
                        RelationshipKindModel.Realization,
                        RelationshipConfidenceModel.High,
                        "implemented interface"
                    )
                );
            }
        }
    }

    private static void AddFieldAndPropertyAssociations(
        INamedTypeSymbol symbol,
        string sourceId,
        ISet<string> knownTypeSet,
        ISet<RelationshipModel> relationships
    )
    {
        var memberRelations = symbol
            .GetMembers()
            .Where(m => !m.IsImplicitlyDeclared)
            .SelectMany(ToRelationCandidates)
            .Where(x => !string.Equals(sourceId, x.TargetTypeId, StringComparison.Ordinal));

        foreach (var relation in memberRelations)
        {
            if (knownTypeSet.Contains(relation.TargetTypeId))
            {
                relationships.Add(
                    new RelationshipModel(
                        sourceId,
                        relation.TargetTypeId,
                        relation.Kind,
                        relation.Confidence,
                        relation.Evidence
                    )
                );
            }
        }

        static IEnumerable<(
            string TargetTypeId,
            RelationshipKindModel Kind,
            RelationshipConfidenceModel Confidence,
            string Evidence
        )> ToRelationCandidates(ISymbol symbol)
        {
            return symbol switch
            {
                IFieldSymbol field => ExtractReferencedTypes(field.Type)
                    .Select(t =>
                    {
                        var c = ClassifyFieldRelationship(field, t);
                        return (GetTypeId(t), c.Kind, c.Confidence, c.Evidence);
                    }),
                IPropertySymbol property => ExtractReferencedTypes(property.Type)
                    .Select(t =>
                    {
                        var c = ClassifyPropertyRelationship(property, t);
                        return (GetTypeId(t), c.Kind, c.Confidence, c.Evidence);
                    }),
                _ => Enumerable.Empty<(
                    string TargetTypeId,
                    RelationshipKindModel Kind,
                    RelationshipConfidenceModel Confidence,
                    string Evidence
                )>(),
            };
        }
    }

    private static void AddMethodAssociations(
        INamedTypeSymbol symbol,
        string sourceId,
        ISet<string> knownTypeSet,
        ISet<RelationshipModel> relationships
    )
    {
        var methodSymbols = symbol
            .GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m =>
                !m.IsImplicitlyDeclared
                && m.MethodKind is MethodKind.Ordinary or MethodKind.Constructor
            );

        foreach (var method in methodSymbols)
        {
            var referencedTypes = ExtractReferencedTypes(method.ReturnType)
                .Concat(method.Parameters.SelectMany(p => ExtractReferencedTypes(p.Type)))
                .Select(GetTypeId)
                .Where(targetId => !string.Equals(sourceId, targetId, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal);

            foreach (var targetId in referencedTypes)
            {
                if (knownTypeSet.Contains(targetId))
                {
                    relationships.Add(
                        new RelationshipModel(
                            sourceId,
                            targetId,
                            RelationshipKindModel.Dependency,
                            RelationshipConfidenceModel.Low,
                            "method signature"
                        )
                    );
                }
            }
        }
    }

    private static (
        RelationshipKindModel Kind,
        RelationshipConfidenceModel Confidence,
        string Evidence
    ) ClassifyFieldRelationship(IFieldSymbol field, INamedTypeSymbol targetType)
    {
        if (IsCollectionType(field.Type))
        {
            return (
                RelationshipKindModel.Aggregation,
                RelationshipConfidenceModel.Medium,
                "collection field"
            );
        }

        // Readonly private fields are a strong ownership hint, closest to composition.
        if (
            field.IsReadOnly
            && !field.IsStatic
            && field.DeclaredAccessibility == Accessibility.Private
            && IsConcreteType(targetType)
        )
        {
            return (
                RelationshipKindModel.Composition,
                RelationshipConfidenceModel.High,
                "private readonly field"
            );
        }

        return (
            RelationshipKindModel.Association,
            RelationshipConfidenceModel.Medium,
            "field reference"
        );
    }

    private static (
        RelationshipKindModel Kind,
        RelationshipConfidenceModel Confidence,
        string Evidence
    ) ClassifyPropertyRelationship(IPropertySymbol property, INamedTypeSymbol targetType)
    {
        if (IsCollectionType(property.Type))
        {
            return (
                RelationshipKindModel.Aggregation,
                RelationshipConfidenceModel.Medium,
                "collection property"
            );
        }

        var isInitOrPrivateSet =
            property.SetMethod is null
            || property.SetMethod.DeclaredAccessibility == Accessibility.Private
            || property.SetMethod.IsInitOnly;

        if (isInitOrPrivateSet && IsConcreteType(targetType))
        {
            return (
                RelationshipKindModel.Composition,
                RelationshipConfidenceModel.Medium,
                "init/private-set property"
            );
        }

        return (
            RelationshipKindModel.Association,
            RelationshipConfidenceModel.Low,
            "property reference"
        );
    }

    private static bool IsConcreteType(INamedTypeSymbol type)
    {
        return type.TypeKind == Microsoft.CodeAnalysis.TypeKind.Class && !type.IsAbstract;
    }

    private static bool IsCollectionType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol)
        {
            return true;
        }

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (named.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        if (
            named.Name
            is "IEnumerable"
                or "ICollection"
                or "IList"
                or "List"
                or "HashSet"
                or "Dictionary"
        )
        {
            return true;
        }

        return named.AllInterfaces.Any(i =>
            i.Name == "IEnumerable" || i.Name == "ICollection" || i.Name == "IList"
        );
    }

    private static IEnumerable<INamedTypeSymbol> ExtractReferencedTypes(ITypeSymbol type)
    {
        switch (type)
        {
            case INamedTypeSymbol named:
            {
                yield return named.OriginalDefinition;

                foreach (var typeArgument in named.TypeArguments)
                {
                    foreach (var nested in ExtractReferencedTypes(typeArgument))
                    {
                        yield return nested;
                    }
                }

                break;
            }
            case IArrayTypeSymbol array:
            {
                foreach (var nested in ExtractReferencedTypes(array.ElementType))
                {
                    yield return nested;
                }

                break;
            }
            case IPointerTypeSymbol pointer:
            {
                foreach (var nested in ExtractReferencedTypes(pointer.PointedAtType))
                {
                    yield return nested;
                }

                break;
            }
            default:
                yield break;
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNamedTypes(
        INamespaceSymbol namespaceSymbol
    )
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            if (member is INamespaceSymbol nestedNamespace)
            {
                foreach (var nested in EnumerateNamedTypes(nestedNamespace))
                {
                    yield return nested;
                }

                continue;
            }

            if (member is INamedTypeSymbol namedType)
            {
                foreach (var nested in EnumerateTypeAndNested(namedType))
                {
                    yield return nested;
                }
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypeAndNested(INamedTypeSymbol type)
    {
        yield return type;

        foreach (var nestedType in type.GetTypeMembers())
        {
            foreach (var nested in EnumerateTypeAndNested(nestedType))
            {
                yield return nested;
            }
        }
    }

    private static bool ShouldIncludeType(INamedTypeSymbol type)
    {
        if (type.IsImplicitlyDeclared)
        {
            return false;
        }

        return type.TypeKind
            is Microsoft.CodeAnalysis.TypeKind.Class
                or Microsoft.CodeAnalysis.TypeKind.Interface
                or Microsoft.CodeAnalysis.TypeKind.Struct;
    }

    private static string GetTypeId(INamedTypeSymbol type)
    {
        var value = type.OriginalDefinition.ToDisplayString(FullTypeDisplay);
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray();
        var sanitized = new string(chars);

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "UnknownType";
        }

        if (char.IsDigit(sanitized[0]))
        {
            return $"T_{sanitized}";
        }

        return sanitized;
    }

    private static string FormatTypeName(ITypeSymbol type)
    {
        return type.ToDisplayString(FullTypeDisplay);
    }

    private static TypeKindModel MapTypeKind(Microsoft.CodeAnalysis.TypeKind kind)
    {
        return kind switch
        {
            Microsoft.CodeAnalysis.TypeKind.Interface => TypeKindModel.Interface,
            Microsoft.CodeAnalysis.TypeKind.Struct => TypeKindModel.Struct,
            _ => TypeKindModel.Class,
        };
    }

    private static VisibilityModel MapVisibility(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => VisibilityModel.Public,
            Accessibility.Protected => VisibilityModel.Protected,
            Accessibility.Internal => VisibilityModel.Internal,
            Accessibility.Private => VisibilityModel.Private,
            Accessibility.ProtectedOrInternal => VisibilityModel.Protected,
            Accessibility.ProtectedAndInternal => VisibilityModel.Internal,
            _ => VisibilityModel.Private,
        };
    }
}
