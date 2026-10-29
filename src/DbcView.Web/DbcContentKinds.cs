namespace DbcView.Web;

/// <summary>
/// Central content Kind constants shared between the workspace definition
/// and the AtlasContentRoute declarations (FR-03). Keeping the ordinal
/// strings in one place prevents a rename on one side from silently
/// producing a "Content is not registered" runtime failure.
/// </summary>
public static class DbcContentKinds
{
    public const string DbcTree = "dbc-tree";
    public const string DbcEditor = "dbc-editor";
    public const string DbcProperty = "dbc-property";
    public const string DbcProblems = "dbc-problems";
}
