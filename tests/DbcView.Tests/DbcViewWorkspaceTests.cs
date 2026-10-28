#nullable enable
using DbcView.Components;
using DbcView.Pages;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DbcView.Tests;

public sealed class DbcViewWorkspaceTests : BunitContext
{
    private const string ValidDbc =
        "VERSION \"\"\nBU_: XXX\n\nBO_ 100 EmptyMsg: 1 XXX\n";

    private static readonly string[] LogicalRegions =
    {
        "InlineStartUpper",
        "InlineStartLower",
        "InlineEndUpper",
        "InlineEndLower",
        "BlockEndInlineStart",
        "BlockEndInlineEnd",
    };

    public DbcViewWorkspaceTests()
    {
        Services.AddDbcViewServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        AtlasJsInteropSetup.Setup(JSInterop);
    }

    [Fact]
    public void Workspace_projects_required_spatial_hierarchy()
    {
        var cut = Render<DbcViewWorkspace>();

        cut.WaitForAssertion(
            () =>
            {
                Assert.Equal(6, cut.FindAll("[data-atlas-group-kind='tool']").Count);
                Assert.Single(cut.FindAll("[data-atlas-group-kind='document']"));
                Assert.Equal(6, cut.FindAll(".atlas-v2-splitter").Count);
                Assert.Single(cut.FindAll("[data-atlas-fixed-slot='top-toolbar']"));
                Assert.Single(cut.FindAll("[data-atlas-fixed-slot='status-bar']"));
                Assert.Single(cut.FindAll("[data-atlas-toolbar-side='inline-start']"));
                Assert.Single(cut.FindAll("[data-atlas-toolbar-side='inline-end']"));
                Assert.Single(cut.FindAll("[data-testid='dbc-view-top-toolbar']"));
                Assert.Single(cut.FindAll("[data-testid='dbc-view-status-bar']"));

                foreach (var logicalRegion in LogicalRegions)
                {
                    Assert.Single(
                        cut.FindAll($"[data-atlas-logical-region='{logicalRegion}']"));
                }
            });
    }

    [Fact]
    public void Editor_hosts_dbc_document_as_active_tab()
    {
        var cut = Render<DbcViewWorkspace>();

        cut.WaitForAssertion(
            () => Assert.Single(
                cut.FindAll("[data-atlas-group-kind='document'] [role='tab']")));

        // Upload and open a document. FR-06: the tab title follows the full
        // document identity (folder/file) instead of the fixed "DBC Editor".
        cut.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromText(ValidDbc, "engine.dbc"));

        cut.WaitForAssertion(
            () => Assert.Single(cut.FindAll("[data-testid='dbc-file']")));
        cut.Find("[data-testid='dbc-file']").Click();

        cut.WaitForAssertion(
            () => Assert.Equal(
                "DBC/engine.dbc",
                cut.Find("[data-atlas-group-kind='document'] [role='tab']")
                    .GetAttribute("aria-label")));

        cut.WaitForAssertion(
            () => Assert.Contains(
                "DBC/engine.dbc",
                cut.Markup,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Editor_opens_two_same_kind_documents_with_independent_state()
    {
        // FR-01: two documents of the same Kind must keep independent editor
        // state instead of sharing one singleton ViewModel.
        var cut = Render<DbcViewWorkspace>();

        // Open the first document.
        cut.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromText(ValidDbc, "a.dbc"));
        cut.WaitForAssertion(
            () => Assert.Single(cut.FindAll("[data-testid='dbc-file']")));
        // Flush pending renders before interacting: a stale element whose
        // event handler was replaced by a queued render makes bUnit throw
        // UnknownEventHandlerIdException.
        await cut.InvokeAsync(() => cut.Find("[data-testid='dbc-file']").Click());
        cut.WaitForAssertion(
            () => Assert.Equal(
                "DBC/a.dbc",
                cut.Find("[data-atlas-group-kind='document'] [role='tab']")
                    .GetAttribute("aria-label")));

        // Open the second document.
        cut.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromText(ValidDbc, "b.dbc"));
        cut.WaitForAssertion(
            () => Assert.Equal(2, cut.FindAll("[data-testid='dbc-file']").Count));
        await cut.InvokeAsync(() => cut.Find("[data-file-key='DBC/b.dbc']").Click());

        cut.WaitForAssertion(
            () =>
            {
                var tabs = cut.FindAll("[data-atlas-group-kind='document'] [role='tab']");
                Assert.Equal(2, tabs.Count);
                Assert.Equal("DBC/a.dbc", tabs[0].GetAttribute("aria-label"));
                Assert.Equal("DBC/b.dbc", tabs[1].GetAttribute("aria-label"));
            });

        // Switch to the first document, switch its editor to text view, and
        // type unsaved content into its buffer.
        await cut.InvokeAsync(() => cut.FindAll("[data-atlas-group-kind='document'] [role='tab']")
            .Single(tab => tab.GetAttribute("aria-label") == "DBC/a.dbc")
            .Click());
        cut.WaitForAssertion(
            () => Assert.NotEmpty(cut.FindAll("[data-testid='dbc-mode-text']")));
        await cut.InvokeAsync(() => cut.Find("[data-testid='dbc-mode-text']").Click());
        cut.WaitForAssertion(
            () => Assert.Single(cut.FindAll("[data-testid='dbc-editor'] textarea")));
        await cut.InvokeAsync(() => cut.Find("[data-testid='dbc-editor'] textarea").Change("unsaved-A"));
        // The editor's fire-and-forget StateHasChanged re-render must settle
        // before the next tab click, otherwise the click targets a stale DOM
        // element whose event handler was replaced.
        cut.WaitForAssertion(
            () => Assert.Equal(
                "unsaved-A",
                cut.Find("[data-testid='dbc-editor'] textarea").GetAttribute("value")));

        // Switch to the second document and back to the first: the first
        // document keeps its unsaved buffer because each tab resolves its own
        // model through the content state scope.
        await cut.InvokeAsync(() => cut.FindAll("[data-atlas-group-kind='document'] [role='tab']")
            .Single(tab => tab.GetAttribute("aria-label") == "DBC/b.dbc")
            .Click());
        cut.WaitForAssertion(
            () => Assert.Equal(
                "DBC/b.dbc",
                cut.Find("[data-atlas-group-kind='document'] [role='tab'][aria-selected]")
                    .GetAttribute("aria-label")));
        await cut.InvokeAsync(() => cut.FindAll("[data-atlas-group-kind='document'] [role='tab']")
            .Single(tab => tab.GetAttribute("aria-label") == "DBC/a.dbc")
            .Click());
        cut.WaitForAssertion(
            () =>
            {
                var textarea = cut.Find("[data-testid='dbc-editor'] textarea");
                Assert.Equal("unsaved-A", textarea.GetAttribute("value"));
            });
    }

    [Fact]
    public void Workspace_opens_same_file_name_in_two_folders_as_separate_tabs()
    {
        var cut = Render<DbcViewWorkspace>();

        cut.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromText(
                "VERSION \"A\"\nBU_: XXX\n\nBO_ 100 MessageA: 1 XXX\n",
                "x.dbc"));
        cut.WaitForAssertion(
            () => Assert.Single(cut.FindAll("[data-file-key='DBC/x.dbc']")));

        cut.Find("[data-testid='dbc-new-folder']").Click();
        cut.WaitForAssertion(
            () => Assert.Equal(2, cut.FindAll("[data-testid='dbc-folder']").Count));

        cut.Render();
        cut.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromText(
                "VERSION \"B\"\nBU_: XXX\n\nBO_ 200 MessageB: 1 XXX\n",
                "x.dbc"));
        cut.WaitForAssertion(
            () => Assert.Equal(
                2,
                cut.FindAll("[data-testid='dbc-file']")
                    .Count(button => button.GetAttribute("data-file-key")!.EndsWith("/x.dbc", StringComparison.Ordinal))));

        var secondKey = cut.FindAll("[data-testid='dbc-file']")
            .Select(button => button.GetAttribute("data-file-key")!)
            .Single(key => key.StartsWith("folder-", StringComparison.Ordinal));

        cut.Render();
        cut.Find("[data-file-key='DBC/x.dbc']").Click();
        cut.WaitForAssertion(
            () => Assert.Single(
                cut.FindAll("[data-atlas-group-kind='document'] [role='tab']")));

        cut.Render();
        cut.Find($"[data-file-key='{secondKey}']").Click();
        cut.WaitForAssertion(
            () =>
            {
                var tabs = cut.FindAll("[data-atlas-group-kind='document'] [role='tab']");
                Assert.Equal(2, tabs.Count);
                Assert.Contains(tabs, tab => tab.GetAttribute("aria-label") == "DBC/x.dbc");
                Assert.Contains(tabs, tab => tab.GetAttribute("aria-label") == secondKey);
            });

        // Both tabs resolve their own editor model and show their own content.
        cut.Render();
        cut.FindAll("[data-atlas-group-kind='document'] [role='tab']")
            .Single(tab => tab.GetAttribute("aria-label") == "DBC/x.dbc")
            .Click();
        cut.WaitForAssertion(
            () =>
            {
                var selected = cut.Find(
                    "[data-atlas-group-kind='document'] [role='tab'][aria-selected]");
                Assert.Equal("DBC/x.dbc", selected.GetAttribute("aria-label"));
                var panel = cut.Find($"#{selected.GetAttribute("aria-controls")}");
                Assert.Contains("MessageA", panel.TextContent, StringComparison.Ordinal);
            });

        cut.Render();
        cut.FindAll("[data-atlas-group-kind='document'] [role='tab']")
            .Single(tab => tab.GetAttribute("aria-label") == secondKey)
            .Click();
        cut.WaitForAssertion(
            () =>
            {
                var selected = cut.Find(
                    "[data-atlas-group-kind='document'] [role='tab'][aria-selected]");
                Assert.Equal(secondKey, selected.GetAttribute("aria-label"));
                var panel = cut.Find($"#{selected.GetAttribute("aria-controls")}");
                Assert.Contains("MessageB", panel.TextContent, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void Workspace_compact_panel_switcher_selects_reachable_panels()
    {
        var cut = Render<DbcViewWorkspace>();

        cut.WaitForAssertion(
            () => Assert.Equal(3, cut.FindAll(".dbc-compact-nav button").Count));

        var shell = cut.Find("[data-testid='dbc-view-shell']");
        Assert.Equal("editor", shell.GetAttribute("data-compact-active"));
        Assert.Equal(
            "true",
            cut.Find("[data-testid='dbc-compact-editor']").GetAttribute("aria-pressed"));

        cut.Find("[data-testid='dbc-compact-explorer']").Click();
        Assert.Equal(
            "explorer",
            cut.Find("[data-testid='dbc-view-shell']").GetAttribute("data-compact-active"));

        cut.Find("[data-testid='dbc-compact-properties']").Click();
        Assert.Equal(
            "properties",
            cut.Find("[data-testid='dbc-view-shell']").GetAttribute("data-compact-active"));
        Assert.Equal(
            "true",
            cut.Find("[data-testid='dbc-compact-properties']").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void Workspace_folder_actions_do_not_reopen_or_change_active_panel()
    {
        var cut = Render<DbcViewWorkspace>();
        cut.FindComponent<InputFile>()
            .UploadFiles(InputFileContent.CreateFromText(ValidDbc, "engine.dbc"));
        cut.WaitForAssertion(
            () => Assert.Single(cut.FindAll("[data-testid='dbc-file']")));
        cut.Render();
        cut.Find("[data-testid='dbc-file']").Click();
        cut.WaitForAssertion(
            () => Assert.Single(cut.FindAll("[data-atlas-group-kind='document'] [role='tab']")));

        cut.Render();
        cut.Find("[data-testid='dbc-compact-explorer']").Click();
        Assert.Equal(
            "explorer",
            cut.Find("[data-testid='dbc-view-shell']").GetAttribute("data-compact-active"));

        cut.Render();
        cut.Find("[data-testid='dbc-new-folder']").Click();
        cut.WaitForAssertion(
            () => Assert.Equal(2, cut.FindAll("[data-testid='dbc-folder']").Count));
        Assert.Equal(
            "explorer",
            cut.Find("[data-testid='dbc-view-shell']").GetAttribute("data-compact-active"));

        cut.Render();
        cut.FindAll("[data-testid='dbc-folder']")
            .Single(button => button.TextContent.Contains("DBC", StringComparison.Ordinal))
            .Click();
        Assert.Equal(
            "explorer",
            cut.Find("[data-testid='dbc-view-shell']").GetAttribute("data-compact-active"));
        Assert.Single(cut.FindAll("[data-atlas-group-kind='document'] [role='tab']"));
    }

    [Fact]
    public void Workspace_chrome_exposes_only_real_actions_and_state()
    {
        var cut = Render<DbcViewWorkspace>();

        cut.WaitForAssertion(
            () => Assert.NotEmpty(cut.FindAll("[data-testid='dbc-view-top-toolbar']")));

        var toolbar = cut.Find("[data-testid='dbc-view-top-toolbar']");
        Assert.Equal(2, toolbar.QuerySelectorAll("button").Length);
        Assert.DoesNotContain("feature/dbc-view", toolbar.TextContent, StringComparison.Ordinal);

        foreach (var placeholder in new[] { "File", "Edit", "View", "Navigate", "Code", "Run", "Search", "Debug" })
        {
            Assert.DoesNotContain(
                toolbar.QuerySelectorAll("button"),
                button => button.TextContent.Trim() == placeholder);
        }

        var status = cut.Find("[data-testid='dbc-view-status-bar']");
        Assert.Contains("Ready", status.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("UTF-8", status.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("4 spaces", status.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("LF", status.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_slots_keep_toolbar_regions_without_panels()
    {
        var cut = Render<DbcViewWorkspace>();

        cut.WaitForAssertion(
            () =>
            {
                // The four empty tool groups still own their LogicalRegion slot.
                Assert.Equal(4, cut.FindAll("[data-atlas-region-empty='true']").Count);
                // Their panels are collapsed: hidden with no content extent.
                Assert.Equal(4, cut.FindAll(".atlas-v2-group-collapsed").Count);
                Assert.Equal(4, cut.FindAll(".atlas-v2-group-collapsed .atlas-v2-empty-state").Count);
            });
    }

    [Fact]
    public void Workspace_exposes_named_aria_relationships()
    {
        var cut = Render<DbcViewWorkspace>();

        cut.WaitForAssertion(
            () =>
            {
                var groups = cut.FindAll("[data-atlas-group-id]")
                    .Where(element => element.GetAttribute("role") == "region")
                    .ToArray();
                Assert.Equal(7, groups.Length);

                // Content-bearing groups (explorer, editor, properties) expose
                // a named header; empty groups intentionally render no header.
                var contentGroups = groups
                    .Where(group => !string.IsNullOrWhiteSpace(group.GetAttribute("aria-labelledby")))
                    .ToArray();
                Assert.Equal(3, contentGroups.Length);
                Assert.Equal(4, groups.Length - contentGroups.Length);

                var documentTabList = cut.Find("[data-atlas-group-kind='document'] [role='tablist']");
                Assert.False(string.IsNullOrWhiteSpace(documentTabList.GetAttribute("aria-label")));

                var tabs = cut.FindAll("[data-atlas-group-kind='document'] [role='tab']");
                Assert.Single(tabs);
                Assert.All(tabs, tab => Assert.False(string.IsNullOrWhiteSpace(tab.GetAttribute("aria-controls"))));

                var selectedTab = cut.Find(
                    "[data-atlas-group-kind='document'] [role='tab'][aria-selected]");
                var panel = cut.Find($"#{selectedTab.GetAttribute("aria-controls")}");
                Assert.Equal("tabpanel", panel.GetAttribute("role"));
                Assert.Equal(selectedTab.Id, panel.GetAttribute("aria-labelledby"));
            });
    }

    [Fact]
    public void Workspace_uses_the_required_nested_split_tree()
    {
        var cut = Render<DbcViewWorkspace>();

        cut.WaitForAssertion(
            () =>
            {
                Assert.Single(cut.FindAll("[data-atlas-node-id='root'] > [data-atlas-node-id='main-area']"));
                Assert.Single(cut.FindAll("[data-atlas-node-id='root'] > [data-atlas-node-id='bottom-dock']"));
                Assert.Single(cut.FindAll("[data-atlas-node-id='main-area'] > [data-atlas-node-id='left-dock']"));
                Assert.Single(cut.FindAll("[data-atlas-node-id='main-area'] > [data-atlas-node-id='editor-right']"));
                Assert.Single(cut.FindAll("[data-atlas-node-id='left-dock'] > [data-atlas-group-id='left-upper']"));
                Assert.Single(cut.FindAll("[data-atlas-node-id='left-dock'] > [data-atlas-group-id='left-lower']"));
                Assert.Single(cut.FindAll("[data-atlas-node-id='editor-right'] > [data-atlas-group-id='editor']"));
                Assert.Single(cut.FindAll("[data-atlas-node-id='editor-right'] > [data-atlas-node-id='right-dock']"));
                Assert.Single(cut.FindAll("[data-atlas-node-id='right-dock'] > [data-atlas-group-id='right-upper']"));
                Assert.Single(cut.FindAll("[data-atlas-node-id='right-dock'] > [data-atlas-group-id='right-lower']"));
                Assert.Single(cut.FindAll("[data-atlas-node-id='bottom-dock'] > [data-atlas-group-id='bottom-left']"));
                Assert.Single(cut.FindAll("[data-atlas-node-id='bottom-dock'] > [data-atlas-group-id='bottom-right']"));
            });
    }

    [Fact]
    public void Workspace_exposes_all_supported_interaction_sources()
    {
        var cut = Render<DbcViewWorkspace>();

        cut.WaitForAssertion(
            () =>
            {
                Assert.Equal(2, cut.FindAll("[data-atlas-panel-header='true'][data-atlas-drag-kind='panel-header']").Count);
                Assert.Equal(2, cut.FindAll("[data-atlas-toolbar-entry='true'][data-atlas-drag-kind='toolbar-entry']").Count);
                Assert.Equal(6, cut.FindAll("[role='separator'][data-atlas-split-id]").Count);
                Assert.All(
                    cut.FindAll("[role='separator'][data-atlas-split-id]"),
                    splitter =>
                    {
                        Assert.Equal("0", splitter.GetAttribute("tabindex"));
                        Assert.False(string.IsNullOrWhiteSpace(splitter.GetAttribute("aria-label")));
                    });
            });
    }

    [Fact]
    public void Verification_page_creates_two_isolated_hosts()
    {
        var cut = Render<Verification>();

        cut.WaitForAssertion(
            () =>
            {
                var hosts = cut.FindAll(".atlas-v2-workspace");
                Assert.Equal(2, hosts.Count);
                Assert.Equal(2, hosts.Select(host => host.GetAttribute("data-atlas-host-id")).Distinct().Count());
            });
    }
}
