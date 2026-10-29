using Atlas.Core;
using Atlas.Core.Definitions;
using Atlas.Core.Items;
using Atlas.Core.Layout;
using Atlas.Core.Placement;

namespace DbcView.Web.Components;

internal static class DbcViewWorkspaceDefinition
{
    internal static AtlasWorkspaceDefinition Create(string workspaceName)
    {
        if (string.IsNullOrWhiteSpace(workspaceName))
        {
            throw new ArgumentException("A workspace name is required.", nameof(workspaceName));
        }

        var dbcTree = Tool("dbc-tree", "DBC Explorer", contentKind: DbcContentKinds.DbcTree);
        var dbcProperty = Tool("dbc-property", "Properties", contentKind: DbcContentKinds.DbcProperty);
        var dbcProblems = Tool("dbc-problems", "Problems", contentKind: DbcContentKinds.DbcProblems);
        var dbcEditor = Document("dbc-editor", "DBC Editor", isPinned: true, contentKind: DbcContentKinds.DbcEditor);

        // The six LogicalRegion slots are preserved as tool groups. The
        // explorer, properties and bottom Problems group carry content; the
        // remaining three groups are intentionally empty (no DockItems),
        // keeping the Atlas six-region toolbar demonstration intact.
        var leftUpper = ToolGroup("left-upper", LogicalRegion.InlineStartUpper, dbcTree.Id);
        var leftLower = EmptyToolGroup("left-lower", LogicalRegion.InlineStartLower);
        var editor = new GroupNode(
            Id("editor"),
            DockItemKind.Document,
            GroupRetentionPolicy.Persistent,
            GroupVisibility.Expanded,
            new[] { dbcEditor.Id },
            dbcEditor.Id);
        var rightUpper = ToolGroup("right-upper", LogicalRegion.InlineEndUpper, dbcProperty.Id);
        var rightLower = EmptyToolGroup("right-lower", LogicalRegion.InlineEndLower);
        var bottomLeft = ToolGroup(
            "bottom-left",
            LogicalRegion.BlockEndInlineStart,
            dbcProblems.Id);
        var bottomRight = EmptyToolGroup("bottom-right", LogicalRegion.BlockEndInlineEnd);

        var leftDock = Split(
            "left-dock",
            SplitAxis.BlockChildren,
            0.56d,
            leftUpper.Id,
            leftLower.Id,
            new SplitConstraints(100d, null, 100d, null));
        var rightDock = Split(
            "right-dock",
            SplitAxis.BlockChildren,
            0.54d,
            rightUpper.Id,
            rightLower.Id,
            new SplitConstraints(100d, null, 100d, null));
        var editorAndRight = Split(
            "editor-right",
            SplitAxis.InlineChildren,
            SplitBasis.FixedPixels(300d, SplitAnchor.Second),
            editor.Id,
            rightDock.Id,
            new SplitConstraints(280d, null, 160d, 420d));
        var mainArea = Split(
            "main-area",
            SplitAxis.InlineChildren,
            SplitBasis.FixedPixels(280d, SplitAnchor.First),
            leftDock.Id,
            editorAndRight.Id,
            new SplitConstraints(150d, 380d, 360d, null));
        var bottomDock = Split(
            "bottom-dock",
            SplitAxis.InlineChildren,
            0.62d,
            bottomLeft.Id,
            bottomRight.Id,
            new SplitConstraints(180d, null, 160d, null));
        var root = Split(
            "root",
            SplitAxis.BlockChildren,
            SplitBasis.FixedPixels(220d, SplitAnchor.Second),
            mainArea.Id,
            bottomDock.Id,
            new SplitConstraints(300d, null, 120d, 360d));

        LayoutNode[] nodes =
        {
            root,
            mainArea,
            leftDock,
            editorAndRight,
            rightDock,
            bottomDock,
            leftUpper,
            leftLower,
            editor,
            rightUpper,
            rightLower,
            bottomLeft,
            bottomRight,
        };
        DockItem[] items =
        {
            dbcTree,
            dbcProperty,
            dbcProblems,
            dbcEditor,
        };
        var toolBars = new[]
        {
            new ToolBarState(LogicalSide.InlineStart, ToolBarVisibility.Expanded),
            new ToolBarState(LogicalSide.InlineEnd, ToolBarVisibility.Expanded),
        };
        var toolBarOrders = new[]
        {
            new ToolBarOrderState(LogicalRegion.InlineStartUpper, new[] { dbcTree.Id }),
            new ToolBarOrderState(LogicalRegion.InlineEndUpper, new[] { dbcProperty.Id }),
            new ToolBarOrderState(LogicalRegion.BlockEndInlineStart, new[] { dbcProblems.Id }),
        };

        return new AtlasWorkspaceDefinition(
            new WorkspaceId(workspaceName),
            root.Id,
            nodes,
            items,
            toolBars,
            toolBarOrders);
    }

    private static GroupNode ToolGroup(
        string id,
        LogicalRegion logicalRegion,
        params DockItemId[] itemIds)
    {
        return new GroupNode(
            Id(id),
            DockItemKind.Tool,
            GroupRetentionPolicy.Persistent,
            GroupVisibility.Expanded,
            itemIds,
            itemIds[0],
            logicalRegion: logicalRegion);
    }

    private static GroupNode EmptyToolGroup(string id, LogicalRegion logicalRegion)
    {
        // A persistent tool group with no items still owns its LogicalRegion,
        // so the toolbar slot renders while the panel is collapsed: the group
        // keeps its slot but contributes no content extent (atlas-v2-group-
        // collapsed collapses the split track to 0).
        return new GroupNode(
            Id(id),
            DockItemKind.Tool,
            GroupRetentionPolicy.Persistent,
            GroupVisibility.Collapsed,
            Array.Empty<DockItemId>(),
            selectedItemId: null,
            logicalRegion: logicalRegion);
    }

    private static SplitNode Split(
        string id,
        SplitAxis axis,
        double firstRatio,
        LayoutNodeId firstId,
        LayoutNodeId secondId,
        SplitConstraints constraints)
    {
        return Split(
            id,
            axis,
            SplitBasis.Proportional(firstRatio),
            firstId,
            secondId,
            constraints);
    }

    private static SplitNode Split(
        string id,
        SplitAxis axis,
        SplitBasis basis,
        LayoutNodeId firstId,
        LayoutNodeId secondId,
        SplitConstraints constraints)
    {
        return new SplitNode(
            Id(id),
            axis,
            basis,
            constraints,
            firstId,
            secondId);
    }

    private static DockItem Tool(string id, string title, string contentKind = "dbc-view-tool")
    {
        return new DockItem(
            new DockItemId(id),
            DockItemKind.Tool,
            new ContentReference(contentKind, id),
            DockItemCapabilityPresets.Tool,
            title,
            id);
    }

    private static DockItem Document(
        string id,
        string title,
        bool isPinned = false,
        string contentKind = "dbc-view-editor")
    {
        return new DockItem(
            new DockItemId(id),
            DockItemKind.Document,
            new ContentReference(contentKind, id),
            DockItemCapabilityPresets.Document,
            title,
            id,
            new DocumentState(isPinned, isPreview: false));
    }

    private static LayoutNodeId Id(string value) => new(value);
}
