<div align="center">

<img src="https://raw.githubusercontent.com/SECTL/SecRandom/master/resources/secrandom-icon-paper.png" width="128" height="128" alt="SecRandom" />

# SecRandom 集成 SDK

**为应用提供名单管理、随机与公平抽取，以及 SecRandom 本地联动能力**

[![GitHub Issues](https://img.shields.io/github/issues-search/SECTL/SecRandom-Integration-SDK?query=is%3Aopen&style=for-the-badge&color=00b4ab&logo=github&label=问题)](https://github.com/SECTL/SecRandom-Integration-SDK/issues)
[![最新版本](https://img.shields.io/github/v/release/SECTL/SecRandom-Integration-SDK?style=for-the-badge&color=00b4ab&label=最新正式版)](https://github.com/SECTL/SecRandom-Integration-SDK/releases/latest)
[![最后更新](https://img.shields.io/github/last-commit/SECTL/SecRandom-Integration-SDK?style=for-the-badge&color=00b4ab&label=最后更新时间)](https://github.com/SECTL/SecRandom-Integration-SDK/commits/main)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg?style=for-the-badge)](LICENSE)

[![QQ群](https://img.shields.io/badge/-QQ%E7%BE%A4%20%7C%20833875216-blue?style=for-the-badge&logo=QQ)](https://qm.qq.com/q/iWcfaPHn7W)
[![Bilibili](https://img.shields.io/badge/-Bilibili%20%7C%20%E9%BB%8E%E6%B3%BD%E6%87%BF-%23FB7299?style=for-the-badge&logo=bilibili)](https://space.bilibili.com/520571577)

</div>

> [!NOTE]
> SecRandom 集成 SDK 以 [GNU GPLv3](LICENSE) 开源。使用和再分发时请遵守许可证，并注明抽取功能的来源。

## SecRandom 集成 SDK

在自己的应用中独立管理名单、历史记录并进行随机或公平抽取，也可以通过本地 IPC 联动 [SecRandom](https://github.com/SECTL/SecRandom)。SDK 支持 Windows、Linux 和 macOS。

## 下载与开始使用

在 .NET 8 或更高版本的项目中，按需安装：

```sh
dotnet add package SecRandom.DrawKit --version 1.2.0
dotnet add package SecRandom.Integration.Client --version 1.2.0
```

- `SecRandom.DrawKit`：独立名单与历史管理、随机抽取、公平抽取。
- `SecRandom.Integration.Client`：SecRandom 自动化 IPC、通知接收和浮窗联动。
- C++、Python、Rust 自动化适配器可从 [GitHub Releases](https://github.com/SECTL/SecRandom-Integration-SDK/releases) 获取。
- 安装、使用示例和 API 说明见 [集成 SDK 官方文档](https://secrandom.sectl.cn/dev/integration/)。

## 联动与兼容性

本体联动需要桌面版 SecRandom **3.2.0 或更高版本**；部分带参数的调用和 macOS 通知联动还需要包含对应 IPC 修复的后续版本。独立抽取不需要安装本体。

结果通知和浮窗联动需要未经修改的官方客户端包及其签名证明；自行编译或修改 SDK 通常无法通过验证。你的应用可以自行构建，独立抽取和自动化 IPC 不受此限制。

## 许可证与来源标注

使用 SDK 时，请在应用的关于页面、文档或其他适当位置标注来源，例如：**抽取功能由 SecRandom 集成 SDK 提供**。通过本体抽取时，可注明：**抽取功能由 SecRandom 提供，通过 SecRandom 集成 SDK 联动**。

## 支持与社区

- [问题反馈](https://github.com/SECTL/SecRandom-Integration-SDK/issues)
- [SecRandom 官方文档](https://secrandom.sectl.cn/dev/integration/)
- [QQ群 833875216](https://qm.qq.com/q/iWcfaPHn7W)

**Copyright © 2025-2026 SECTL**
