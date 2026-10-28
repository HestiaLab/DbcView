#nullable enable
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace DbcView.Tests;

internal static class TreeTestHelpers
{
    public static void ClickFile<TComponent>(
        IRenderedComponent<TComponent> cut,
        string? fileName = null)
        where TComponent : IComponent
    {
        ClickTreeItem(cut, "dbc-file", fileName);
    }

    public static void ClickFileByKey<TComponent>(
        IRenderedComponent<TComponent> cut,
        string fileKey)
        where TComponent : IComponent
    {
        var label = cut.FindAll($"[data-testid='dbc-file'][data-file-key='{fileKey}']")
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Tree file '{fileKey}' was not found.");
        ClickTreeItem(label);
    }

    public static void ClickFolder<TComponent>(
        IRenderedComponent<TComponent> cut,
        string? folderName = null)
        where TComponent : IComponent
    {
        ClickTreeItem(cut, "dbc-folder", folderName);
    }

    private static void ClickTreeItem<TComponent>(
        IRenderedComponent<TComponent> cut,
        string testId,
        string? text)
        where TComponent : IComponent
    {
        var label = cut.FindAll($"[data-testid='{testId}']")
            .FirstOrDefault(element => text is null
                || element.TextContent.Contains(text, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Tree item '{testId}' was not found.");
        ClickTreeItem(label);
    }

    private static void ClickTreeItem(IElement label)
    {
        IElement? item = label;
        while (item is not null && item.GetAttribute("role") != "treeitem")
        {
            item = item.ParentElement;
        }

        var content = item?.QuerySelector(".tree-node-content__main")
            ?? throw new InvalidOperationException("Tree item has no clickable content.");
        content.Click();
    }
}
