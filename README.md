<div align="center">

<img src="https://raw.githubusercontent.com/SECTL/SecRandom/master/resources/secrandom-icon-paper.png" width="128" height="128" alt="SecRandom" />

# SecRandom 集成 SDK

**为第三方应用提供名单、历史记录、公平抽取与 SecRandom 本地联动能力**

[![GitHub Issues](https://img.shields.io/github/issues-search/SECTL/SecRandom-Integration-SDK?query=is%3Aopen&style=for-the-badge&color=00b4ab&logo=github&label=问题)](https://github.com/SECTL/SecRandom-Integration-SDK/issues)
[![最新版本](https://img.shields.io/github/v/release/SECTL/SecRandom-Integration-SDK?style=for-the-badge&color=00b4ab&label=最新正式版)](https://github.com/SECTL/SecRandom-Integration-SDK/releases/latest)
[![测试版本](https://img.shields.io/github/v/release/SECTL/SecRandom-Integration-SDK?include_prereleases&style=for-the-badge&label=测试版)](https://github.com/SECTL/SecRandom-Integration-SDK/releases)
[![最后更新](https://img.shields.io/github/last-commit/SECTL/SecRandom-Integration-SDK?style=for-the-badge&color=00b4ab&label=最后更新时间)](https://github.com/SECTL/SecRandom-Integration-SDK/commits/main)
[![累计下载](https://img.shields.io/github/downloads/SECTL/SecRandom-Integration-SDK/total?style=for-the-badge&color=00b4ab&label=累计下载)](https://github.com/SECTL/SecRandom-Integration-SDK/releases)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg?style=for-the-badge)](LICENSE)

[![QQ群](https://img.shields.io/badge/-QQ%E7%BE%A4%20%7C%20833875216-blue?style=for-the-badge&logo=QQ)](https://qm.qq.com/q/iWcfaPHn7W)
[![Bilibili](https://img.shields.io/badge/-Bilibili%20%7C%20%E9%BB%8E%E6%B3%BD%E6%87%BF-%23FB7299?style=for-the-badge&logo=bilibili)](https://space.bilibili.com/520571577)

**语言** [ **简体中文** ]

</div>

> [!NOTE]
> 本 SDK 以 [GNU GPLv3](LICENSE) 协议发布。使用和再发布时请遵守该协议

## SecRandom 集成 SDK

SecRandom 集成 SDK 面向希望在自己的应用中使用名单和抽取能力，或通过本地 IPC 控制并接收 [SecRandom](https://github.com/SECTL/SecRandom) 抽取结果的开发者。协议核心保持语言无关；C# 程序集继续面向 .NET 8，另提供 C++、Python 和 Rust 的薄适配层，使用 .NET 10 SDK 构建和测试

## SDK 功能

### 名单与抽取

- 使用稳定 ID 管理学生名单、奖品名单及成员信息
- 将名单和历史记录保存为本地 JSON 或 SQLite，并通过历史记录参与公平抽取
- 支持随机抽取与公平抽取；公平模式按成员权重和历史抽中次数计算权重
- 公平抽取支持均匀优先和随机优先；独立模式提供 SQLite 名单、成员、历史管理和快照导入导出
- 抽取支持性别、分组筛选和单轮不重复；本 SDK 暂不包含编排抽取

### 本地联动

- 需要桌面版 SecRandom **3.2.0 或更高版本**；独立抽取不需要安装 SecRandom
- 部分带参数的自动化调用和 macOS 通知联动还需要包含相应 IPC 修复的更新版本；3.2.0 不包含这些后续修复
- 通过本机当前用户的 IPC 发现接收端，并接收 SecRandom 的抽取结果
- 在 SecRandom 中经用户批准后保存通知渠道；可申请浮窗联动租约，退出或释放租约后恢复浮窗

### 多语言自动化

- 通过同一套 v2 本地 IPC 发送 `点名`、`闪抽`、`抽奖` 指令，支持可选名单、人数、性别和分组
- 可选在抽取前打开主页面；默认不改变窗口状态
- 可以通过 IPC 读取名单/奖池、名单成员、性别和小组选项，由接入方自己的 UI 让用户选择
- 未提供的参数不会被 SDK 补齐，SecRandom 会继续使用软件当前设置，因此 SDK 不会固定随机/公平模式或人数
- C# 复用 `SecRandom.Integration.Client`；C++、Python、Rust 适配器位于 [`bindings/`](bindings/)，协议说明见 [`docs/automation-v2.md`](docs/automation-v2.md)

### 组件

| 包 | 用途 |
| --- | --- |
| `SecRandom.Integration.Abstractions` | 协议、名单、历史与结果的数据契约 |
| `SecRandom.Integration.Client` | 本地发现、通知接收与浮窗联动 IPC |
| `SecRandom.DrawKit` | 名单和历史存储、随机与公平抽取 |

| 语言适配器 | 用途 |
| --- | --- |
| `bindings/cpp` | C++17 头文件，返回原始 JSON 响应 |
| `bindings/python` | Python 3.10+、标准库实现 |
| `bindings/rust/secrandom-integration` | 同步 Rust crate |

## 技术演进

| 版本 | 技术栈 | 阶段 |
| --- | --- | --- |
| **v1** | **C# + .NET 8** | 首个公开 SDK，提供名单、历史、随机/公平抽取与本地 IPC 联动 |
| **v2** | **C# / C++ / Python / Rust** | 共享 JSON IPC 契约；各语言只负责传输和参数适配，抽取策略继续由 SecRandom 管理 |

## 下载与更新

完整的安装、调用、事件和常见问题请参阅 [集成 SDK 文档](https://secrandom.sectl.cn/dev/integration/)。当前源码中的 1.2 便捷 API 尚未随 1.1.0 发行包提供；使用前请确认相应版本已经发布。

从 [GitHub Releases](https://github.com/SECTL/SecRandom-Integration-SDK/releases) 下载发行包并解压，包内包含三个 `.nupkg`、官方 proof，以及 `bindings/` 下的 C++、Python、Rust 适配器。将解压目录作为本地 NuGet 源，按需添加 `SecRandom.DrawKit` 或 `SecRandom.Integration.Client`；其依赖的 `SecRandom.Integration.Abstractions` 也需要保留在该源中

例如，解压到 `D:\packages\secrandom` 后，在目标项目目录执行：

```powershell
dotnet nuget add source D:\packages\secrandom --name SecRandomLocal
dotnet add package SecRandom.DrawKit --version 1.1.0
dotnet add package SecRandom.Integration.Client --version 1.1.0
```

### 通过软件默认设置抽取

```csharp
using SecRandom.Integration;
using SecRandom.Integration.Client;

var client = new AutomationClient();
var result = await client.DrawAsync(
    DrawChannel.RollCall,
    new DrawOptions(List: "一班", OpenMainWindow: true));
```

`List`、`Count`、`Gender` 和 `Group` 都是可选的一次性覆盖；全部省略时，抽取名单、人数、随机/公平/编排模式和其他策略均由 SecRandom 当前设置决定。`OpenMainWindow` 默认关闭，不会因为 SDK 调用而改变窗口状态。需要修改软件的默认名单时才调用 `ActivateRosterAsync`。

用户选择所需的数据可以通过 `GetRosterListsAsync`、`ReadRosterAsync` 和
`GetRosterSelectionOptionsAsync` 获取；这些方法只读本体，不会切换默认名单。Python、Rust
和 C++ 适配器分别提供 `roster_lists` / `read_roster`，返回同一份 IPC JSON 数据。

### 本地抽取示例

```csharp
using SecRandom.DrawKit;
using SecRandom.Integration;

var rosters = new JsonRosterStore("data/rosters");
var history = new JsonHistoryStore("data/history.json");
rosters.Save(new Roster("class-a", "一班", RosterKind.Students,
[
    new RosterMember("student-1", "小明"),
    new RosterMember("student-2", "小红")
]));

var draws = new IntegrationDrawService(rosters, history, new DrawEngine());
var result = draws.Draw(new DrawRequest("class-a", 1, DrawMode.Fair));
Console.WriteLine(result.Members[0].Name);
```

`IntegrationDrawService.Draw` 在成功抽取后写入历史。SDK 的名单和历史存储由接入应用管理，不会自动读取或修改 SecRandom 本体的数据

1.2 开发版提供事务式本地数据库：

```csharp
var local = new LocalDrawStore("data/draw.sqlite");
local.SaveRoster(new Roster("class-a", "一班", RosterKind.Students,
    [new RosterMember("student-1", "小明"), new RosterMember("student-2", "小红")]));
var result = local.Draw(new DrawRequest("class-a", 1, DrawMode.Fair)
    { FairStrategy = FairDrawStrategy.RandomFirst });
var history = local.QueryHistory(new HistoryQuery(RosterId: "class-a"));
```

`LocalDrawStore` 的名单、抽取及历史写入使用 SQLite 事务，多实例共享同一数据库；删除名单默认保留历史。依赖 `Microsoft.Data.Sqlite`，NuGet 还原时需要可用的 nuget.org 或镜像源。独立抽取的两种公平策略采用 SDK 文档中定义的规则，不代表完整复现本体全部公平配置、证明或编排功能。

### 接收本体结果

```csharp
using SecRandom.Integration.Client;

await using var receiver = new NotificationReceiver(new NotificationReceiverOptions(
    AppId: "example.classroom.app",
    DisplayName: "课堂应用",
    SdkVersion: "1.1.0.0",
    PublisherProofPath: "SecRandom.Integration.Client.official-proof.json"));

receiver.NotificationReceived += (result, cancellationToken) =>
{
    Console.WriteLine(string.Join(", ", result.Members.Select(member => member.Name)));
    return Task.FromResult(true);
};

await receiver.StartAsync();
// 保持接收器及应用进程运行；退出时释放 receiver
```

将官方 proof 文件部署到示例中的路径，`SdkVersion` 须与 proof 中的程序集版本一致。1.2 官方客户端包会将 proof 复制到应用输出目录，可使用 `Path.Combine(AppContext.BaseDirectory, "SecRandom.Integration.Client.official-proof.json")` 指定路径。SecRandom 检测到接收器后会请求用户批准；具体可用性取决于运行中的 SecRandom 版本。更多协议细节见 [集成协议 v1](docs/integration-v1.md)

## 验证边界

| 验证内容 | 作用与限制 |
| --- | --- |
| SDK 官方 proof | ECDSA P-256 签名绑定官方客户端程序集的版本和 SHA-256；修改或重编译的程序集不能复用该 proof |
| 进程和 IPC 对端 | 宿主发现时检查进程已加载与 proof 匹配的程序集，通信时检查 IPC 对端进程 |
| 用户批准 | 发现通过验证的接收器后，由用户决定是否保存通知渠道 |
| 应用身份 | proof 只证明 SDK 客户端程序集，不证明使用 SDK 的第三方应用是官方软件；`app_id` 和显示名称由接入应用提供 |

签名私钥仅保存在 SDK 仓库的 GitHub Actions Secret 与本地忽略的 `.env` 中；公钥作为 `SECRANDOM_INTEGRATION_PUBLIC_KEY_SPKI_BASE64` 变量注入 SDK 和本体构建

## 构建与测试

```powershell
dotnet test SecRandom.Integration.slnx -c Release
```

SDK 主分支的受信任构建会生成 NuGet 包和官方 proof，作为 Actions 构建产物。发布包从 [GitHub Releases](https://github.com/SECTL/SecRandom-Integration-SDK/releases) 下载；使用 SDK 时请优先选择带有官方 proof 的同版本发行包

## 许可证与第三方声明

- SDK 使用 [GNU GPLv3](LICENSE) 协议发布
- 抽取结果取决于接入应用提供的名单、权重及历史记录，SDK 无法验证现实名单的真实性或完整性

## 贡献者和特别感谢

<a href="https://github.com/SECTL/SecRandom-Integration-SDK/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=SECTL/SecRandom-Integration-SDK" alt="SDK contributors" />
</a>

感谢每一位为 SecRandom 集成 SDK 提交代码、报告问题、完善文档和提供反馈的贡献者

## 支持与社区

- [问题反馈](https://github.com/SECTL/SecRandom-Integration-SDK/issues)
- [SecRandom 本体仓库](https://github.com/SECTL/SecRandom)
- [SecRandom 官方文档](https://secrandom.sectl.cn/doc/overview.html)
- [QQ群 833875216](https://qm.qq.com/q/iWcfaPHn7W)
- [Bilibili 主页](https://space.bilibili.com/520571577)

## Star History

<a href="https://www.star-history.com/?repos=SECTL%2FSecRandom-Integration-SDK&type=date&legend=top-left">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=SECTL/SecRandom-Integration-SDK&type=date&theme=dark&legend=top-left" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=SECTL/SecRandom-Integration-SDK&type=date&legend=top-left" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=SECTL/SecRandom-Integration-SDK&type=date&legend=top-left" />
 </picture>
</a>

**Copyright © 2025-2026 SECTL**
