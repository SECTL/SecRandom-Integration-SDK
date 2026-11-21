# SecRandom 集成协议 v1

v1 协议仅限本地使用。各实现使用当前用户的 IPC 端点以及一个经过签名的发现清单。ECDSA P-256 签名涵盖包 ID、程序集版本以及 SDK 客户端程序集的 SHA-256 哈希值。宿主还会检查所通告的进程是否已加载具有该哈希值的程序集。在这些检查通过且用户批准该通知通道之前，清单均不可信。批准结果由 SecRandom 宿主持久化保存。

SDK 抽取 API 仅公开 `random` 和 `fair`；它有意不包含编排模式。通知接收器接受 `notification.result`。宿主集成还保留了 `linkage.acquire` 和 `linkage.release`，用于浮动窗口抑制租约。除当前操作系统账户外，不暴露任何端点。