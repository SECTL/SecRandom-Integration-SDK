# SecRandom 集成 SDK

本仓库包含三个用于与 SecRandom 进行本地集成的 NuGet 包：

- `SecRandom.Integration.Abstractions`：协议与数据契约。
- `SecRandom.Integration.Client`：IPC 发现与结果通知接收器。
- `SecRandom.DrawKit`：本地名单、历史记录、随机抽取与公平抽取的基础原语。

公开的抽取模式为随机抽取和公平抽取。编排式抽取有意不在本 SDK 的范围内。

## 官方 SDK 验证

发布工作流会使用仓库的 ECDSA P-256 私钥，对构建出的 `SecRandom.Integration.Client.dll` 的 SHA-256 哈希和程序集版本进行签名。匹配的公钥会被编译进 SDK 和 SecRandom 两个构建中。SecRandom 会检查签名证明，并确认证明哈希与所通告进程加载的某个模块相匹配，然后才会将该进程作为通知通道提供。因此，本地重新构建或修改过的客户端程序集无法复用官方证明。

私有签名密钥只应存放在本仓库的 Actions secret 以及未纳入版本控制的本地 `.env` 中。切勿将私钥添加到应用程序构建或仓库变量中。公钥可以安全地作为 `SECRANDOM_INTEGRATION_PUBLIC_KEY_SPKI_BASE64` Actions 变量在两个仓库中公开。

## 本地 IPC

SDK 宿主会使用其稳定的应用程序 ID、显示名称、程序集版本以及随包提供的官方证明 JSON 来创建 `NotificationReceiver`。该接收器会在 `%LOCALAPPDATA%/SecRandom/integrations` 下发布一个已签名的清单，并通过当前用户的命名管道接受 `notification.result` 请求。SecRandom 在保存新发现的通道之前仍会询问用户。该管道仅限当前账户本地使用；此协议不暴露网络监听器。

线格式与版本控制规则见 [docs/integration-v1.md](docs/integration-v1.md)。

## 构建

```powershell
dotnet test SecRandom.Integration.slnx -c Release
```

在受信任的 SDK 构建中，Actions 会创建一个捆绑包，其中包含三个 NuGet 包和 `SecRandom.Integration.Client.official-proof.json`，作为可下载的工作流工件。