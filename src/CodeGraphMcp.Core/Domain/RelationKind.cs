namespace CodeGraphMcp.Core.Domain;

public enum RelationKind
{
    Contains,
    Calls,
    Inherits,
    Implements,
    References,
    Imports,
    Binds,          // XAML x:Class → ViewModel
    DependsOn,      // project → project reference
    Declares,
}
