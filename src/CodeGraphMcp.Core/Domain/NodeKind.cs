namespace CodeGraphMcp.Core.Domain;

public enum NodeKind
{
    File,
    Namespace,
    Class,
    Interface,
    Enum,
    Struct,
    Record,
    Method,
    Property,
    Field,
    Function,
    Module,
    Component,      // Angular @Component
    Injectable,     // Angular @Injectable
    NgModule,       // Angular @NgModule
    XamlView,
    XamlResource,
    ConfigKey,
    SqlTable,
    SqlProcedure,
    DocumentSection,
}
