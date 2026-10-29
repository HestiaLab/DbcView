#nullable enable
using Bunit;

namespace DbcView.Tests;

/// <summary>
/// Explicit Atlas and Astrea module interop setup. Interaction tests run with
/// strict JSInterop, so a missing or renamed JS call fails the test instead of
/// being silently swallowed. Every void handler completes its invocation so
/// render-sync calls can repeat safely.
/// </summary>
internal static class AtlasJsInteropSetup
{
    public static BunitJSModuleInterop Setup(BunitJSInterop jsInterop)
    {
        var module = jsInterop.SetupModule("./_content/Atlas.Blazor/atlas-v2/atlas.js");
        var controller = module.SetupModule("attachAtlasController", _ => true);
        controller.SetupVoid("dispose", _ => true).SetVoidResult();
        module.SetupVoid("updateAtlasController", _ => true).SetVoidResult();
        module.SetupVoid("disposeAtlasController", _ => true).SetVoidResult();
        module.SetupVoid("clearAtlasSplitPreview", _ => true).SetVoidResult();

        var astrea = jsInterop.SetupModule("./_content/Astrea.Components/astrea-tree.js");
        // Astrea initializes its keyboard helper from OnAfterRenderAsync with a
        // generic IJSObjectReference result that bUnit cannot shape as void;
        // keep the rest of the runtime strict and let this third-party module
        // answer with defaults.
        astrea.Mode = JSRuntimeMode.Loose;

        var resourceTable = jsInterop.SetupModule(
            "./_content/Astrea.Components/astrea-resource-table.js");
        resourceTable.Mode = JSRuntimeMode.Loose;
        return module;
    }
}
