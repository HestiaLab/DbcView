using Atlas.Blazor.DependencyInjection;
using DbcView.Application;
using DbcView.Infrastructure;

namespace DbcView;

/// <summary>
/// Composition root contract: creates one isolated application workspace per
/// Atlas host. Implementations bind concrete infrastructure (codec and
/// repository); view models and components only see application types.
/// </summary>
internal interface IDbcWorkspaceComposition
{
    DbcFileWorkspace CreateWorkspace();
}

internal sealed class DbcWorkspaceComposition : IDbcWorkspaceComposition
{
    public DbcFileWorkspace CreateWorkspace()
    {
        return new DbcFileWorkspace(new InMemoryDbcFileRepository(), new DbcCodec());
    }
}

/// <summary>
/// Registers the Atlas services and the DBC workspace composition root. Every
/// content panel (the DBC explorer, editor, and properties views) is a
/// Host-local AtlasContentRoute contribution declared by DbcViewWorkspace.
/// </summary>
public static class DemoServiceCollectionExtensions
{
    public static IServiceCollection AddDbcViewServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddAtlasWorkspace();
        services.AddSingleton<IDbcWorkspaceComposition, DbcWorkspaceComposition>();
        return services;
    }
}
