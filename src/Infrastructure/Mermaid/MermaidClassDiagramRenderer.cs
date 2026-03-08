using System.Text;
using System.Text.RegularExpressions;
using DiagramGenerator.Application.Interfaces;
using DiagramGenerator.Application.Models;
using DiagramGenerator.Domain.Models;

namespace DiagramGenerator.Infrastructure.Mermaid;

public sealed class MermaidClassDiagramRenderer : IMermaidRenderer
{
    public string Render(DiagramModel diagram, MermaidRenderOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("classDiagram");
        if (options.TopToBottom)
        {
            sb.AppendLine("direction TB");
        }

        var orderedTypes = diagram.Types.OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();

        foreach (var type in orderedTypes)
        {
            var label = options.Compact ? type.ShortName : type.FullName;
            sb.AppendLine($"class {type.Id}[\"{EscapeLabel(label)}\"]");

            var stereotype = GetStereotype(type);
            if (stereotype is not null)
            {
                sb.AppendLine($"<<{stereotype}>> {type.Id}");
            }

            if (type.Members.Count > 0)
            {
                foreach (
                    var member in type.Members.OrderBy(m => m.Signature, StringComparer.Ordinal)
                )
                {
                    var signature = options.Compact
                        ? SimplifySignature(member.Signature)
                        : member.Signature;
                    sb.AppendLine($"{type.Id} : {MapVisibility(member.Visibility)} {signature}");
                }
            }
        }

        foreach (
            var relationship in diagram
                .Relationships.OrderBy(r => r.SourceTypeId, StringComparer.Ordinal)
                .ThenBy(r => r.TargetTypeId, StringComparer.Ordinal)
                .ThenBy(r => r.Kind)
        )
        {
            sb.AppendLine(RenderRelationship(relationship, options));
        }

        return sb.ToString();
    }

    private static string RenderRelationship(
        RelationshipModel relationship,
        MermaidRenderOptions options
    )
    {
        var label = FormatRelationshipLabel(relationship, options);

        return relationship.Kind switch
        {
            RelationshipKindModel.Inheritance =>
                $"{relationship.SourceTypeId} --|> {relationship.TargetTypeId} : {label}",
            RelationshipKindModel.Realization =>
                $"{relationship.SourceTypeId} ..|> {relationship.TargetTypeId} : {label}",
            RelationshipKindModel.Dependency =>
                $"{relationship.SourceTypeId} ..> {relationship.TargetTypeId} : {label}",
            RelationshipKindModel.Association =>
                $"{relationship.SourceTypeId} --> {relationship.TargetTypeId} : {label}",
            RelationshipKindModel.Aggregation =>
                $"{relationship.SourceTypeId} o-- {relationship.TargetTypeId} : {label}",
            RelationshipKindModel.Composition =>
                $"{relationship.SourceTypeId} *-- {relationship.TargetTypeId} : {label}",
            _ => throw new InvalidOperationException(
                $"Unsupported relationship kind: {relationship.Kind}"
            ),
        };
    }

    private static string FormatRelationshipLabel(
        RelationshipModel relationship,
        MermaidRenderOptions options
    )
    {
        var baseLabel = relationship.Kind.ToString().ToLowerInvariant();
        if (!options.ShowHeuristics)
        {
            return baseLabel;
        }

        var confidence = relationship.Confidence.ToString().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(relationship.Evidence))
        {
            return $"{baseLabel} [{confidence}]";
        }

        return $"{baseLabel} [{confidence}; {relationship.Evidence}]";
    }

    private static string? GetStereotype(TypeModel model)
    {
        return model.Kind switch
        {
            TypeKindModel.Interface => "interface",
            TypeKindModel.Struct => "struct",
            TypeKindModel.Class when model.IsAbstract => "abstract",
            _ => null,
        };
    }

    private static string MapVisibility(VisibilityModel visibility)
    {
        return visibility switch
        {
            VisibilityModel.Public => "+",
            VisibilityModel.Protected => "#",
            VisibilityModel.Internal => "~",
            VisibilityModel.Private => "-",
            _ => "~",
        };
    }

    private static string EscapeLabel(string text)
    {
        return text.Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string SimplifySignature(string signature)
    {
        // Trim namespace prefixes to reduce node width in compact mode.
        return Regex.Replace(
            signature,
            @"\b(?:[A-Za-z_][A-Za-z0-9_]*\.)+([A-Za-z_][A-Za-z0-9_]*)",
            "$1"
        );
    }
}
