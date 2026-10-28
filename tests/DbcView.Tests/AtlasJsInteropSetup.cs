#nullable enable
using Bunit;

namespace DbcView.Tests;

/// <summary>
/// Explicit Atlas module interop setup. Interaction tests run with strict
/// JSInterop, so a missing or renamed Atlas JS call fails the test instead of
/// being silently swallowed. Every void handler completes its invocation so
/// Atlas render-sync calls can repeat safely.
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
        return module;
    }
}
