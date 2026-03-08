namespace DiagramGenerator.Domain.Models;

public sealed record DiagramModel(
    IReadOnlyList<TypeModel> Types,
    IReadOnlySet<RelationshipModel> Relationships
);
