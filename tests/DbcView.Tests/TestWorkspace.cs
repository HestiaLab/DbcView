#nullable enable
using DbcView.Application;
using DbcView.Domain;
using DbcView.Infrastructure;

namespace DbcView.Tests;

internal static class TestWorkspace
{
    public static DbcFileWorkspace Create()
    {
        return new DbcFileWorkspace(new InMemoryDbcFileRepository(), new DbcCodec());
    }

    public static DbcFileEntry AddFile(
        this DbcFileWorkspace workspace,
        string folder,
        string fileName,
        string content)
    {
        if (!workspace.Folders.Contains(folder, StringComparer.Ordinal))
        {
            workspace.CreateFolder(folder);
        }

        return workspace.Upload(new DbcDocumentId(folder, fileName), content);
    }
}
