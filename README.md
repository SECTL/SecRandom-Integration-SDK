# SecRandom 集成 SDK

在自己的应用中独立管理名单、历史记录并进行随机或公平抽取，也可以通过本地 IPC 联动 [SecRandom](https://github.com/SECTL/SecRandom)。

## 安装

在 .NET 8 或更高版本的项目中，按需安装：

```sh
dotnet add package SecRandom.DrawKit --version 1.2.0
dotnet add package SecRandom.Integration.Client --version 1.2.0
```

- `SecRandom.DrawKit`：独立名单与历史管理、随机抽取、公平抽取。
- `SecRandom.Integration.Client`：SecRandom 自动化 IPC、通知接收和浮窗联动。
- C++、Python、Rust 自动化适配器可从 [GitHub Releases](https://github.com/SECTL/SecRandom-Integration-SDK/releases) 获取。

SDK 支持 Windows、Linux 和 macOS。本体联动需要桌面版 SecRandom **3.2.0 或更高版本**；部分带参数的调用和 macOS 通知联动还需要包含对应 IPC 修复的后续版本。独立抽取不需要安装本体。

## 文档

安装、使用示例、API 和兼容性说明见 [集成 SDK 文档](https://secrandom.sectl.cn/dev/integration/)。

## 许可证

[GNU GPLv3](LICENSE)
