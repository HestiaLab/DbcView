# DbcView

[English](README.md) | 简体中文

**一个用于浏览和编辑 CAN 数据库（DBC）文件的独立 Blazor WebAssembly 应用。**

## 功能特性

| 能力        | 说明                       |
| --------- | ------------------------ |
| DBC 文件浏览器 | 文件夹/文件树，支持本地 `.dbc` 文件上传 |
| 消息与信号编辑器  | 表格视图与原始文本视图，每个文档可切换      |
| 属性面板      | 即时查看所选文件、消息或信号的属性        |

```mermaid
flowchart LR
    subgraph WS["DbcView 工作区"]
        direction LR
        EXP["文件浏览器<br/>工具面板"] --- EDT["编辑器<br/>文档标签"]
        EDT --- PRP["属性面板<br/>工具面板"]
    end
```

## 界面截图

![DbcView 工作区](assets/dbcview.png)

## 在线试用

<https://HestiaLab.github.io/DbcView/> — 无需安装，直接访问即可使用。

## 快速开始

需要 .NET 10 SDK（仓库通过 `global.json` 固定版本）。

```bash
dotnet restore DbcView.sln --configfile NuGet.Config
dotnet build DbcView.sln --no-restore
dotnet test DbcView.sln --no-build --no-restore
dotnet run --project src/DbcView.Web/DbcView.Web.csproj --no-restore
```

打开 `dotnet run` 输出的 URL。

## 使用方法

```mermaid
flowchart TD
    UP[选择上传目标文件夹] --> SEL[上传 .dbc 文件]
    SEL --> OPEN[在文件树中打开]
    OPEN --> EDT[表格 / 文本编辑]
    EDT --> SAV[保存]
    OPEN --> PRP[在属性面板中查看]
```

- 上传上限为 4 MB，文件会进入界面上显示的 **Upload target** 文件夹。
- 文档标识由目录与文件名共同组成，因此 `A/x.dbc` 与 `B/x.dbc` 会作为
  两个独立标签打开并分别保存。
- 非法 DBC 文本会保留为**未验证草稿**：保存始终保留原始文本，并显示带行号
  和稳定错误码的诊断信息。合法空文件仍显示 “No messages in this DBC file”。
- 文件、文件夹和上传选择器均可仅用键盘操作；宽度低于 700 px 时，通过
  Explorer / Editor / Properties 切换器保证所有面板可达。
- 中性的工具型界面会跟随操作系统的浅色或深色配色。

## 项目结构

```text
DbcView/
├── src/DbcView.Domain/          文档模型、标识与诊断
├── src/DbcView.Application/     用例、仓储与编解码端口
├── src/DbcView.Infrastructure/  DBC 解析/序列化、内存仓储
├── src/DbcView.Web/             Blazor WASM 表现层与组合根
│   ├── Components/              工作区宿主与面板
│   ├── Services/                浏览器上传读取
│   ├── ViewModels/              文件树/编辑器/属性视图模型
│   └── Pages/                   Index (/)、Verification (/verification)
├── tests/DbcView.Tests/         xUnit + bUnit 测试
├── samples/                     样例 CAN 数据库文件
└── assets/                      README 截图
```

## 样例数据

`samples/bmw_e9x_e8x.dbc` 是社区维护的 BMW E9x/E8x 车型 CAN 数据库文件，来源为
<https://github.com/dzid26/opendbc-BMW-E8x-E9x>（MIT 许可；见文件头
`CM_ "License MIT"` 注释）。

## 免责声明

样例文件仅用于演示和测试。与 BMW 无任何关联或认可关系；"BMW" 及车辆名称均为
各自所有者的商标。数据可能不准确或已过时，请勿用于生产车辆。
