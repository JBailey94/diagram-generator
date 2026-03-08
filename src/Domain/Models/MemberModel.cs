namespace DiagramGenerator.Domain.Models;

public enum TypeKindModel
{
    Class,
    Interface,
    Struct,
}

public enum MemberKindModel
{
    Field,
    Property,
    Method,
    Constructor,
}

public enum VisibilityModel
{
    Public,
    Protected,
    Internal,
    Private,
}

public enum RelationshipKindModel
{
    Inheritance,
    Realization,
    Dependency,
    Association,
    Aggregation,
    Composition,
}

public enum RelationshipConfidenceModel
{
    Low,
    Medium,
    High
}

public sealed record TypeModel(
    string Id,
    string NamespaceName,
    string FullName,
    string ShortName,
    TypeKindModel Kind,
    bool IsAbstract,
    IReadOnlyList<MemberModel> Members
);

public sealed record MemberModel(
    MemberKindModel Kind,
    VisibilityModel Visibility,
    string Signature
);

public sealed record RelationshipModel(
    string SourceTypeId,
    string TargetTypeId,
    RelationshipKindModel Kind,
    RelationshipConfidenceModel Confidence = RelationshipConfidenceModel.Medium,
    string Evidence = ""
);
