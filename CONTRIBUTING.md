# Contributing

DbcView is a standalone Blazor WebAssembly application for browsing and
editing CAN database (DBC) files. It is also the consumer showcase for the
[Atlas](https://github.com/HestiaLab/Atlas) dock layout component library:
it consumes the published `Atlas.Blazor` NuGet package from nuget.org and
never references Atlas source projects.

## Consumer Boundary

- The only dependency on Atlas is the published `Atlas.Blazor` NuGet package;
  `Atlas.Core` is embedded in that package (single-package distribution).
- `src/DbcView.Domain`, `src/DbcView.Application`, and
  `src/DbcView.Infrastructure` have no Atlas dependency. `src/DbcView`
  (presentation and composition root) is the only project that references
  `Atlas.Blazor`.
- The test project uses internal `ProjectReference` entries to the product
  projects. It must never reference prebuilt DLLs by `HintPath`.
- `tests/DbcView.Tests/DemoPackageBoundaryTests.cs` verifies the Atlas
  boundary, the inward dependency direction, and that no Atlas source files
  are linked into this repository.
- Do not add linked Atlas source files, internal APIs, or test helpers.

## Architecture

```text
src/
├── DbcView.Domain/          DbcDocument, DbcMessage, DbcSignal, DbcDocumentId,
│                            parse diagnostics, ordered document sections
├── DbcView.Application/     use cases (upload/open/save), IDbcCodec and
│                            IDbcFileRepository ports, DbcFileEntry
├── DbcView.Infrastructure/  DBC parser/serializer adapter, in-memory repository
└── DbcView/                 Razor components, ViewModels, Atlas workspace,
                             composition root (src/DbcView.Web/DemoServiceCollectionExtensions.cs)
```

Dependencies point inward only:

- Domain → nothing
- Application → Domain
- Infrastructure → Application + Domain
- Presentation → Application + Infrastructure (composition root only) + Domain
- Tests → any product project

View models never construct concrete parsers or repositories; only the
composition root binds `DbcCodec` and `InMemoryDbcFileRepository`.

## Documentation Language

English is the canonical documentation language. `README.zh.md` mirrors
`README.md`: keep the same section structure and update both when the
English version changes.

## Local Checks

Requires the .NET 10 SDK (pinned by `global.json`).

```sh
# Restore .NET dependencies
dotnet restore DbcView.sln --configfile NuGet.Config

# Build all projects
dotnet build DbcView.sln --no-restore

# Run all tests
dotnet test DbcView.sln --no-build --no-restore

# Check code style
dotnet format DbcView.sln --verify-no-changes --no-restore
```

## Testing Unpublished Atlas Packages

To test against unpublished Atlas builds, uncomment the `atlas-local` source
in `NuGet.Config` and point it at a feed containing the `Atlas.Blazor`
package (for example the Atlas repository's `artifacts/packages`).
`Atlas.Core` is embedded in `Atlas.Blazor`, so the feed only needs that one
package. Update the version in `Directory.Packages.props` when needed.

## Branches

Use short branch names:

- `feature/<topic>`
- `fix/<topic>`
- `refactor/<topic>`
- `infra/<topic>`

## Commits And PRs

Use Conventional Commit style with a Chinese description:

```text
feat(editor): 新增信号编辑功能
fix(parser): 处理多路复用消息
refactor(store): 从 DbcFileStore 提取 DbcDocument 加载
infra(ci): 新增格式校验任务
docs(readme): 记录仓库布局
```

Write commit messages, PR titles, and PR descriptions in Chinese.

Before opening a PR:

- Run the Local Checks above.
- Run `dotnet format` to ensure consistent code style.
- Fill in the PR template sections that apply to the change.
- Add tests for behaviour changes.
- Keep unrelated cleanup out of feature or fix PRs.

Use the issue templates for bug reports, feature proposals, and
infrastructure maintenance requests.
