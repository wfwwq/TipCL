# progress/M4-H-R7 · AI 限流 / 5xx 重试(2026-11-12)

## 结论

参考版有、Linux 侧漏掉的「限流 / 5xx 退避重试」已补上,并配了 6 个不联网的回归用例。
Debug + Release **0 警告 0 错误**;全量 `dotnet test` **324 项:通过 323、跳过 1(既有 smoke 跳过)、失败 0**
(基线 317 通过 + 1 跳过 → 新增 6 项,老用例全绿)。

## 改了什么

| 文件 | 改动 |
| --- | --- |
| `src/TipCL.Core/Launcher/Ai/AiAgent.cs` | 新增重试逻辑:常量/退避/判据/提示 4 个私有成员(AiAgent.cs:456–506),`StreamOnceAsync` 改成「重试驱动循环」(:516),把原来那个「发一次流式请求」的整段抽成 `StreamAttemptAsync`(:603)。头注释补了一段 R7 说明。 |
| `tests/TipCL.Tests/Core/AiRetryTests.cs` | **新建**,6 个用例 + 一个本机假网关 `ScriptedChatServer`(TcpListener 手写 HTTP,随机空闲端口)。 |

未动 `Client.cs`(不需要:`Client.ChatStream` 在 HTTP >= 400 时已经抛带 `StatusCode` 的 `AIClientError`,信息足够判断重试)。
未动 `PROGRESS.md` / `DECISIONS.md` / `tests/TipCL.Tests/App/`,未 commit。

## 参考版对照(搬的是哪一段)

参考版 `windos/tipcl/src/TipCL/Core/Launcher/Ai/Agent.cs`:

- `:759` `int num4 = 0;` —— 重试计数**每轮重置**(声明在回合循环内)。
- `:829` `if (aIClientError.Retryable() && string.Concat(list7).Length == 0 && list7.Count == 0 && list6.Count == 0 && num4 < 3)`
  —— 可重试 + **还没产出内容** + 重试次数 < 3。
- `:831–842` `num4++` → `_status("retry", {attempt, error})` → `Trace.Record("retry", ...)` → `_sleep_backoff(num4)` → 重发。
- `:1697` `_sleep_backoff`: `Math.Min(4.0, Math.Pow(2.0, attempt-1))` 秒 × `(0.8 + 0.4*rnd)` 抖动,`_check()` 前后各一次取消检查。
- `:1574` 的类型判定(`429/500/502/503/504` 或 `Category == "rate_limited"`)在参考版里是 `_maybe_fallback`(**换模型兜底**)的入口;真正的「原地重发」判据是 `AIClientError.Retryable()`(`:829`)。Linux 侧没有 fallback model 这套东西,所以只搬了「原地重试」这一半(见偏离 1)。

搬到 Linux 后的行为:

- **触发**:`AIClientError.Retryable()` 为真且 `!Fatal()` —— 即 `429 / 500 / 502 / 503 / 504`、分类 `rate_limited`,以及参考版口径里的 `provider_timeout / provider_network_error / provider_stream_error`。
- **不触发**:401/403(认证,Fatal)、上下文超长、参数错、未知错误 —— 一次都不重试,照旧直接失败。
- **上限**:每轮最多 3 次重试(合计最多 4 次请求),不是无限重试。
- **退避**:`min(4, 2^(n-1))` 秒 × 0.8~1.2 抖动 ⇒ 第 1 次约 1 秒、第 2 次约 2 秒、第 3 次约 4 秒。
- **产出内容就不重发**:`state.Text.Length == 0 && state.Calls.Count == 0 && attempt.PendingTools.Count == 0` —— 已经吐给用户正文/思考段或拿到 `tool_calls` 就绝不重发,否则用户会看到重复内容。
- **可观察**:重试时发 `AiAgentEvent{Kind="status", Text="接口HTTP 429,正在重试(第 1/3 次,约 1.2 秒后)"}`
  (AiPage/AiFixView 的 `case "status"` 直接渲染到状态行,界面不会静默卡住);同时写会话事件 `Store.LogEvent(ChatId, "ModelRetry", {turn_id,round,attempt,category,error})` 和轨迹 `Trace.Record("retry", …)`。
- **可取消**:退避用 `await Task.Delay(wait, ct)`,取消直接抛 `OperationCanceledException`,由 `RunAsync` 的既有分支记 `stop_reason = cancelled`。

## 偏离参考版的地方(3 处,都有理由)

1. **只搬重试、不搬换模型兜底**。参考版的顺序是「先退避重试 3 次,再 `_maybe_fallback` 换模型」;Linux 的 `AiAgent` 没有 `fallbackModel` 概念,没有可换的目标。重试用尽后仍按原样失败(`stop_reason = stream_failed`),与本次改动前一致。
2. **退避改成 `Task.Delay(..., ct)`**。参考版用 `Thread.Sleep` + 前后 `_check()`,取消发生在睡眠中间时要睡满才抛。任务书要求「点停止立刻断」,所以改异步等待;也在驱动循环开头加了一次 `linked.IsCancellationRequested` 短路(与参考版 `_check()` 等价)。
3. **把「实际要等多久」先算出来再报**。参考版把抖动算在 `_sleep_backoff` 内部、状态里只报 attempt。这里 `BackoffSeconds()` 只算一次,状态文案里的秒数与真实等待一致(便于用户/排查判断)。

另外一处**对齐而非偏离**:流里的 `error` 事件(如 `{"error":{"message":"额度不足"}}`)参考版会包成 `AIClientError(message)`(:819)再走同一套分类;Linux 原来只留 message 字符串。现在同样包一层,于是**没有 HTTP 状态码、只靠文案的限流错误也能被识别重试**(`Categorize` 认 `rate limit` / `限制` / `额度`)。副作用:`AssembleStream` 的「接口超时,正在换一次非流式」属 `provider_timeout`(可重试),在没有产出内容时现在会先重试最多 3 次——与参考版「先重试、后兜底」的顺序一致,而且 Linux 本来也没有那个非流式兜底。已在 `ContentAlreadyStreamed_IsNeverResent` 里锁住「有产出时不重发」这条边界。

## 测试(全部走本机假网关,不联网)

`tests/TipCL.Tests/Core/AiRetryTests.cs`(`ScriptedChatServer` = `TcpListener(IPAddress.Loopback, 0)` 随机空闲端口,按脚本回状态码;非 200 回真实网关那种 `当前分组上游负载已饱和…(rate limit exceeded)` 错误体,200 回最小 SSE):

| # | 用例 | 断言 |
| --- | --- | --- |
| 1 | `RateLimitedOnce_ThenSucceeds_RetriesAndKeepsTheAnswer` | 429 → 200:请求数 **= 2**、`stop_reason = no_tool_call`、正文 `你好`、有 status 重试事件、会话事件流里有 `ModelRetry` |
| 2 | `RateLimitedForever_FailsAfterRetryCap` | 连续 429:请求数 **= 4**(首次 + 上限 3 次重试)、3 条重试提示、最终 `error` 事件带 429 文案,`AIClientError.Category == "rate_limited"` 且 `Retryable()` 为真,`stop_reason = stream_failed` |
| 3 | `Unauthorized_DoesNotRetryAtAll` | 401:请求数 **= 1**,无任何重试提示 |
| 4 | `BadGateway503_IsRetriedToo` | 503 → 200:请求数 = 2、最终成功 |
| 5 | `ContentAlreadyStreamed_IsNeverResent` | 先吐正文再报限流:请求数 **= 1**,正文只出现一次,直接失败(防重复内容) |
| 6 | `CancelDuringBackoff_ThrowsImmediately` | 收到「正在重试」后取消:抛 `OperationCanceledException`,`取消→抛` 耗时 < 700 ms(退避本来 ≥ 800 ms),请求数仍是 1,`stop_reason = cancelled` |

用例 2 是唯一慢的(≈7 秒,真实退避 1+2+4 秒),其余都是毫秒级;全量套件总时长 23 秒(此前 23 秒量级,没明显变化)。

## 实跑记录

```powershell
cd D:\项目\tipcl\linux
dotnet build-server shutdown
dotnet build TipCL.Linux.sln -c Debug   --nologo   # 0 警告 0 错误
dotnet build TipCL.Linux.sln -c Release --nologo   # 0 警告 0 错误
dotnet test tests/TipCL.Tests/TipCL.Tests.csproj --nologo -- RunConfiguration.DotNetHostPath=C:\Users\Administrator\.dotnet\dotnet.exe
# 已通过! - 失败: 0,通过: 323,已跳过: 1,总计: 324
```

- `dotnet test` 的 restore 阶段会打两条 `NU1900`(华为云 nuget 源取漏洞数据失败),是**环境/网络噪声**,与本次改动无关;编译本身 0 警告。
- 退出码用 `*>` 重定向取,没走 `Select-Object` 截断管道。

## 遗留 / 风险

1. **`AIClientError` 没有 `IsRateLimited` 属性**。任务书里提到 `AIClientError.cs:36` 的 `IsRateLimited`,实际该文件 `:36` 是 `Retryable()` 里的 `return Category == "rate_limited";`(参考版同位置也一样)。要在断言里判「按限流失败」只能用 `Category == "rate_limited"`;若主 agent 想补一个 `IsRateLimited` 属性,该文件不在本包的写入范围内,未动。
2. **`StopReason.RATE_LIMITED` 仍然没人用**——参考版(Windows)也只在定义处出现,`_maybe_fallback` 之外没有任何消费点。为保持两边一致,重试用尽的失败原因仍是 `stream_failed`。若要在 UI/日志里区分「限流失败」,现在可以读会话事件流的 `ModelRetry`(`category` 字段)或 `Trace` 的 `retry` 行。
3. 重试是**原地重发同一请求**,没读 `Retry-After` 响应头(参考版也没读)。公益网关的 429 通常 1 秒内就恢复,现有退避足够;若要更省额度可以做,但会偏离参考版。
4. `StreamOnceAsync` 现在一次调用最多发 4 个请求,`Usage.Add` 每次失败尝试都会记一条(与改动前的「一次失败记一条」口径一致,只是次数可能变多)。
