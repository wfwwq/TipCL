# TipCL 插件系统设计（契约文档）

> 本文是插件系统各部分之间的**接口契约**。C# 内核、Node 宿主、WPF 界面三部分按本文对接；改动契约时必须同步更新本文。
> 验收标准：把 [luckui/dsh-game](https://github.com/luckui/dsh-game)（DSH 插件）移植到 TipCL 时，**不缺任何宿主能力**。参考副本在项目的 `ref/dsh-game/`。

## 0. dsh-game 用到的 DSH 宿主能力 → TipCL 对应

| dsh-game 用法 | 位置 | TipCL 对应 |
|---|---|---|
| `ctx.effect(fn, label)` 可撤销副作用 | 处处 | Node ctx `effect`（插件卸载时逆序执行） |
| `ctx.connection.fetch.register({path, methods, fetch})` 同源 HTTP 路由（含 SSE 长连接、二进制） | index.ts | Node 宿主内置 Web 服务器 `ctx.connection.fetch.register`（Fetch API Request/Response，支持 ReadableStream） |
| `ctx.tools.register(defineTool({...}))`，参数 DSH 风格，`execute(args, {callId, agent:{id}, signal})`，`output.render` 返回 text/image 片段 | agent.ts | `ctx.tools.register`（兼容 DSH 风格和 JSON Schema 两种参数写法），结果可带图片 |
| 工具执行最长阻塞 10 分钟（game_continue）、可被取消 | agent.ts | 插件工具无超时上限（默认 30 分钟，可在工具上声明），对话停止时 `signal` abort |
| agent 预设：人设前缀 `complete:true`、只带游戏工具、不带文件/shell、不注入运行时上下文 | cordis.patch.yml | manifest `contributes.agentPresets`（或运行时 `ctx.presets.register`）：`systemPrompt` + `replaceSystemPrompt` + 工具白名单 + `maxRounds:0`（不限步数） |
| `ctx.get('llm')`：`listProviders()`、`listModels(provider)`、`stream({provider, model, system, messages, maxTokens, reasoningEffort, signal})` 流式 `text-delta`/`finish` | agent.ts / index.ts | `ctx.get('llm')` 同名同形；供应商 = TipCL 主模型配置 `tipcl` + 用户在插件设置里添加的额外 OpenAI 兼容供应商 |
| `ctx.get('credentials').resolve(name)` → `{value}`；网页端 `remote.credentials.set(ref, value)`、`credentials/reference-updated` 事件 | agent.ts / settings.tsx | `ctx.get('credentials')`（DPAPI 加密，按插件隔离）；网页端 `tipcl.credentials.set/status`，事件 `credentials.updated` |
| `ctx.get('sessions').get(id)` → `{seq, snapshotEvents(from)}` **同步**读取会话事件（assistant/message 带 text 与 tool-call、turn/end） | agent.ts | Node 宿主维护会话事件镜像，同步可读；事件模型见 §5 |
| `ctx.attachments.saveImages([{data, mediaType, name}])` → 附件，工具结果里 `{type:'image', attachment}` 交给模型看 | agent.ts | `ctx.attachments.saveImages`；Agent 把工具结果里的图片作为视觉输入交给模型 |
| `ctx.get('liveBody')`：别的插件（dsh-livestream）提供的服务 | index.ts | 跨插件服务：`ctx.provide(name, obj)` / `ctx.get(name)`（所有 Node 插件同一进程，直接拿对象） |
| `ctx.logger.warn` | agent.ts | `ctx.logger.{debug,info,warn,error}` → `plugin-logs/<id>.log` + 开发者面板 |
| `process.env.DSH_HOME` 数据目录 | setup.ts | `ctx.paths.data`，并设置环境变量 `TIPCL_PLUGIN_DATA`；为兼容也设 `DSH_HOME` = 插件数据根 |
| 插件配置（schemastery，volatile 热更新，`config.x.get()`） | index.ts | manifest `config` 声明；`apply(ctx, config)` 收到 `{key: {get()}}` 单元格，改了不用重启；`ctx.config.onChange` |
| 子进程 / 文件 / 网络 / PowerShell / 启动 Minecraft 客户端 | games/* | Node 原生能力（Node 插件不做沙箱，安装时明示“完整电脑权限”） |
| 网页端：侧栏入口 + 主区面板（游戏库） | client.tsx | `ui.page`（侧栏入口，WebView2 页面） |
| 网页端：全局浮层（右下角大肥鱼） | stage.tsx | `ui.overlay`（贴在主窗口上的无边框 WebView 浮窗，可设大小/位置/点击穿透） |
| 网页端：会话里的「游戏」标签页，可读会话事件、往会话发消息（steer/queue）、停止 | gametab.tsx | `ui.aiView`（AI 页对话上方的标签），props 带 `chatId`；`tipcl.ai.events/send/cancel` |
| 网页端：新建会话、选预设、发第一句、切到该会话和指定标签 | library.tsx | `tipcl.ai.createChat({preset, view, prompt})` 一步完成；`tipcl.ai.open(chatId, {view})` |
| 网页端：`layout.selectPanel(id)` 跳页面 | library.tsx | `tipcl.nav.open(pageId)` |
| 网页端：设置页（表单 + 密钥字段） | settings.tsx | 自动生成的设置页（manifest config + credentials），也可以注册自定义 `ui.settings` 视图 |
| 网页端：localStorage、Web Audio、`<video>` 透明 WebM、Canvas、EventSource | stage/wolf | WebView2（Chromium）全部支持；每个插件页面同源（Node 宿主的端口） |
| 首页卡片（DSH 没有，TipCL 特有） | — | `ui.dashCard` |
| 启动流程钩子 / 游戏日志 / 实例操作（TipCL 特有） | — | `ctx.launcher.call(method, kwargs)` 可调用全部 BackendApi；`ctx.hooks` 启动前改参数、启动后、退出、崩溃；`ctx.events.on('game.log', ...)` |

## 1. 目录与文件

```
%TIPCL_HOME%/
  plugins/<id>/                 已安装的插件包（解压后的内容，含 tipcl-plugin.json）
  plugin-data/<id>/             插件自己的数据目录（ctx.paths.data）
  plugin-logs/<id>.log          插件日志
  plugin-secrets/<id>.bin       凭据，DPAPI(CurrentUser) 加密的 JSON
  plugins.json                  插件状态（见下）
  runtime/node/                 便携 Node（没有系统 Node ≥ 22 时按需下载）
  runtime/plugin-host/          内置 Node 宿主脚本（每次启动从 exe 内嵌资源释放，版本不同才覆盖）
```

`plugins.json`：
```json
{
  "version": 1,
  "plugins": {
    "dsh-game": {
      "enabled": true,
      "version": "0.1.0",
      "source": "market" | "local" | "dev",
      "devPath": "D:/code/dsh-game/packages/dsh-game",
      "grantedPermissions": ["launcher", "ai.tools"],
      "installedAt": "2026-11-01T00:00:00Z",
      "market": "https://.../index.json"
    }
  },
  "providers": [ { "id": "openrouter", "name": "OpenRouter", "baseUrl": "https://openrouter.ai/api/v1", "apiKeyRef": "provider:openrouter" } ]
}
```
`source: "dev"` 的插件不复制，直接从 `devPath` 加载，文件变化时自动重载（开发者模式）。

## 2. 清单 `tipcl-plugin.json`

```json
{
  "id": "dsh-game",                       // [a-z0-9][a-z0-9-_.]{1,63}
  "name": "大肥鱼玩游戏",
  "version": "0.1.0",                     // semver
  "author": "luckui",
  "description": "让 AI 自己玩游戏",
  "icon": "assets/icon.png",              // 相对插件目录；也可以是一个 Fluent 字形 "\ue7fc"
  "homepage": "https://github.com/...",
  "engines": { "tipcl": ">=3.3.0", "node": ">=22" },
  "runtime": "node",                      // node | dotnet
  "main": "lib/index.js",                 // node：宿主端模块（ESM 或 CJS）
  "client": "lib/client.js",              // node：网页端模块（ESM，浏览器里 import）
  "assembly": "MyPlugin.dll",             // dotnet：程序集
  "entryType": "MyPlugin.Entry",          // dotnet：实现 TipCL.Plugins.ITipCLPlugin 的类型
  "install": { "npm": true, "registry": "https://registry.npmmirror.com" },  // 安装后在插件目录跑 npm install --omit=dev
  "permissions": ["launcher", "ai.tools", "ai.presets", "ai.sessions", "ai.llm", "credentials", "ui", "ui.overlay", "system"],
  "config": {
    "voice":  { "type": "string",  "default": "zh_female", "label": "音色", "description": "..." },
    "censor": { "type": "boolean", "default": true, "label": "打码" },
    "port":   { "type": "integer", "default": 7391, "min": 0, "max": 65535 },
    "mode":   { "type": "string",  "enum": ["a", "b"], "enumLabels": ["甲", "乙"] },
    "secret": { "type": "string",  "secret": true }
  },
  "contributes": {
    "credentials": [ { "name": "DOUBAO_TTS_API_KEY", "label": "豆包语音 API Key", "description": "新版控制台" } ],
    "agentPresets": [ {
      "id": "dsh-game", "name": "大肥鱼玩游戏", "description": "...", "order": 10,
      "systemPrompt": "你是 DeepSeek……",   // 或 "systemPromptFile": "prompts/fish.md"
      "replaceSystemPrompt": true,          // true：不用 TipCL 默认系统提示词；false：追加在后面
      "includeRuntimeContext": false,       // 是否注入实例/版本等运行时上下文和长期记忆
      "tools": { "include": ["game_*"], "builtin": false },   // builtin:true 时还带 TipCL 内置工具
      "maxRounds": 0,                       // 0 = 不限步数
      "view": "game"                        // 用这个预设的对话默认打开哪个 aiView 标签
    } ]
  },
  "dependencies": { "dsh-livestream": ">=0.1.0" },        // 可选依赖用 optionalDependencies
  "optionalDependencies": {}
}
```

### 权限
| 权限 | 管什么（宿主强制检查） |
|---|---|
| `launcher` | `launcher.call` 调用启动器全部 BackendApi、订阅启动器事件、`hooks.*` |
| `ai.tools` | 给 AI 注册工具 |
| `ai.presets` | 注册 agent 预设 |
| `ai.sessions` | 新建/读取/驱动 AI 对话 |
| `ai.llm` | 直接调用模型（`llm.*`，消耗用户额度） |
| `credentials` | 读写自己的凭据 |
| `ui` | 侧栏页面、AI 标签、首页卡片、设置页、提示框 |
| `ui.overlay` | 主窗口浮层 |
| `system` | 声明性：Node 插件天然拥有完整的文件、网络、进程权限，安装时必须显示红色警告 |

Node 插件做不到真正沙箱，`system` 对 `runtime: node` 自动视为已声明。安装/更新时弹出权限确认，新增权限需要重新确认。

## 3. 运行时

### 3.1 Node 运行时（主力）
- 所有 Node 插件跑在**同一个** Node 宿主进程里（和 DSH/cordis 一样），这样 `ctx.get('别的插件的服务')` 直接拿对象。宿主崩溃时 TipCL 自动重启（30 秒内超过 3 次就停，并在插件页显示错误）。
- Node 选择：系统 PATH 里的 `node` ≥ 22 → 否则 `runtime/node/node.exe` → 否则首次需要时下载 `node-v22.x-win-x64.zip`（先 `https://npmmirror.com/mirrors/node/`，失败再 `https://nodejs.org/dist/`），走 TipCL 的下载器，校验 SHASUMS256。
- 启动：`node runtime/plugin-host/host.mjs`，环境变量 `TIPCL_HOST_TOKEN`（随机）、`TIPCL_HOME`、`TIPCL_VERSION`、`TIPCL_LANG`。
- 通信：**stdin/stdout 换行分隔 JSON（NDJSON）**，双向 JSON-RPC 风格：
  - 请求 `{"id": 7, "method": "launcher.call", "params": {...}}`
  - 响应 `{"id": 7, "result": ...}` 或 `{"id": 7, "error": {"code": "permission_denied", "message": "..."}}`
  - 通知（无 id）`{"method": "event", "params": {...}}`
  - stdout 只走协议；插件的 `console.*` 被宿主重定向到 stderr，TipCL 按 `[plugin-id]` 前缀写进对应日志。
    stderr 每行格式：`[<plugin-id>] <文字>` 或 `[<plugin-id>] [warn|error|debug] <文字>`；宿主自己的行是 `[host] ...`。（插件用 stdio:'inherit' 起的子进程仍会直接写 fd 1，插件不要这么做。）
  - 宿主脚本是平铺的一层目录（`host.mjs`、`host-*.mjs`、`sdk.js`、`tipcl-plugin.d.ts`），没有子目录，内嵌资源按文件名释放即可。
- 宿主内置一个 Web 服务器（`127.0.0.1:<随机端口>`），所有 Node 插件共用这一个源：
  - 插件注册的路由（如 `/api/dsh-game/events`）
  - `/__tipcl/sdk.js` 网页端 SDK
  - `/__tipcl/view/<plugin>/<view>?k=<pageToken>&kind=<kind>&id=<specId>&props=<json>` 视图外壳页。可选参数：`chatId`（aiView，等价于 `props.chatId`）、`theme=light|dark`（覆盖当前主题）、`transparent=1`（`kind=overlay` 时自动透明）。没带 `kind`/`id` 时宿主按该插件注册过的 UI 里 `view` 相同的那一项推断（浮层的 `tipcl.overlay.update` 需要知道浮层 id）。宿主在 `ui.register` 的 spec 里会附上 `url`（已带 `k/kind/id`，aiView 只需再拼 `&chatId=`）。
  - `/__tipcl/plugin/<plugin>/<path>` 插件目录里的静态文件（只读，禁止 `..`）
  - `POST /__tipcl/call` 网页端调用宿主 API（必须带 `X-TipCL-Page` 头 = pageToken）
  - `GET /__tipcl/events?topics=a,b&k=<pageToken>` 宿主事件 SSE（每条 `data: {"topic","data"}`）
  - `GET /__tipcl/health` → `ok`
  - `GET /__tipcl/events-worker.js`：SDK 的 SharedWorker。Chromium 对同一源最多 6 条 HTTP/1.1 连接（同一 WebView2 环境里所有 WebView 共用），所以同一插件的所有视图（页面、AI 标签、浮层、卡片）经它**共用一条** `/__tipcl/events`；不支持 SharedWorker 时每页自己连。WPF 端请让所有插件 WebView 用同一个 `CoreWebView2Environment`（同一用户数据目录），SharedWorker 才能共享；插件自己的 SSE 路由也算在这 6 条里（dsh-game 的舞台事件流就是每页一条）
  - 拒绝 `Host` 头不是 `127.0.0.1:<port>` / `localhost:<port>` 的请求（421，防 DNS rebinding）；也拒绝带外站 `Origin` 或 `Sec-Fetch-Site: cross-site|same-site` 的请求（403），别的网站戳不到插件路由
  - 网页端经 `/__tipcl/call` 不能调：`host.*`、`log`、`tools.*`、`hooks.*`、`presets.*`、`events.subscribe`、`plugin.load/unload`、`llm.stream/abort`（这些要 Node 端执行体或只给宿主用；网页端事件走 `/__tipcl/events`）
- **pageToken** = 小写十六进制 `HMAC-SHA256(key = UTF8(TIPCL_HOST_TOKEN), msg = UTF8(插件 id))` 的前 32 位。TipCL 在 `plugin.load` 的 `pageToken` 里传给宿主（以它为准），宿主在结果里回显。

### 3.2 .NET 运行时（受信任插件）
- `Assembly.LoadFrom`，实例化 `entryType`，调用 `ITipCLPlugin.Apply(IPluginContext ctx)`。卸载/更新/禁用需要重启 TipCL（插件页显示“重启后生效”）。
- 接口定义在 TipCL.exe 的 `TipCL.Plugins` 命名空间（插件引用 TipCL.exe 编译）：

```csharp
public interface ITipCLPlugin { void Apply(IPluginContext ctx); }
public interface IPluginContext {
  string Id { get; }  PluginPaths Paths { get; }  IPluginLogger Logger { get; }
  void Effect(Func<Action> setup, string label = null);
  object Launcher(string method, IDictionary<string, object> kwargs = null);     // = launcher.call
  IDisposable OnEvent(string topic, Action<string, object> handler);
  IDisposable RegisterTool(PluginToolSpec spec, Func<PluginToolCall, CancellationToken, PluginToolResult> execute);
  IDisposable RegisterPreset(AgentPresetSpec spec);
  IDisposable RegisterHook(string hook, Func<object, object> handler);
  IPluginLlm Llm { get; }  IPluginCredentials Credentials { get; }  IPluginSessions Sessions { get; }
  IPluginConfig Config { get; }  IPluginUi Ui { get; }
  void Provide(string name, object service);  object Get(string name);
}
// IPluginUi 额外支持原生 WPF：RegisterPage(spec, Func<FrameworkElement>)、RegisterDashCard(spec, Func<FrameworkElement>)、RegisterAiView(spec, Func<string chatId, FrameworkElement>)
```
.NET 插件和 Node 插件共享同一套宿主服务实现（`PluginHostApi`），只是调用方式不同。

原生 WPF 界面的登记处：`TipCL.Plugins.PluginUiRegistry`（内核实现，见 `Core/Plugins/Sdk/PluginUiRegistry.cs`）。WPF 界面用**反射**按名字调用，不做编译期依赖，所以下面的签名改动要同步：
```csharp
// 主路径（内核现有实现）
NativeUiEntry PluginUiRegistry.Find(string plugin, string kind, string id);   // kind: page | dashCard | aiView
FrameworkElement NativeUiEntry.Create();                  // page / dashCard
FrameworkElement NativeUiEntry.CreateAiView(string chatId); // aiView
// 兼容路径（Find 不存在时才用）：static CreatePage(plugin, id) / CreateDashCard(plugin, id) / CreateAiView(plugin, id, chatId)
```
这些入口同样要出现在 `plugins_ui()` 里（行上 `native:true`、`url:""`），WPF 才知道侧栏 / 首页 / AI 页要放它们。控件实现 `IDisposable` 时，WPF 卸掉它时会调用 `Dispose`。首页卡片 `defaultW/defaultH`：大于 12 按 px，不超过 12 按“格”（1 格约 160×110px）。

## 4. 宿主 API（方法名即 NDJSON `method`，Node ctx 与网页端 SDK 都映射到这里）

调用身份：Node 宿主发来的请求在 `params.plugin` 里带插件 id（宿主替插件填，插件改不了）；网页端调用由 Node 宿主按 pageToken 填。TipCL 按该插件的 `grantedPermissions` 检查，不通过返回 `permission_denied`。

### Node 宿主 → TipCL（请求）
| method | params | result | 权限 |
|---|---|---|---|
| `host.hello` | `{hostVersion, webPort, pid}` | `{tipclVersion, lang, theme, home}` | — |
| `log` | `{plugin, level, text}` | — | — |
| `launcher.call` | `{plugin, method, args?:[], kwargs?:{}}` | BackendApi 返回值 | launcher |
| `events.subscribe` | `{plugin, topics:[...]}`（支持 `*` 通配，如 `game.*`）。**累加**：只加不减 | — | 按 topic |
| `events.unsubscribe` | `{plugin, topics:[...]}` 取消这些 topic | — | — |
| `tools.register` | `{plugin, tool:{name, description, parameters(JSON Schema), timeoutMs?, label?}}` | — | ai.tools |
| `tools.unregister` | `{plugin, name}` | — | ai.tools |
| `presets.register` | `{plugin, preset}`（同 manifest 结构） | — | ai.presets |
| `presets.unregister` | `{plugin, id}` | — | ai.presets |
| `ai.chats.create` | `{plugin, preset?, title?, view?, prompt?, open?:true}`（`preset`/`view` 可写插件自己的短 id，先按 `plugin:<id>/<短id>` 找） | `{chatId}` | ai.sessions |
| `ai.chats.list` | `{plugin}` | `[{chatId, title, preset, updatedAt, running}]` | ai.sessions |
| `ai.chats.events` | `{plugin, chatId, from?}`（返回 `seq > from` 的事件） | `{seq, events:[...]}` | ai.sessions |
| `ai.chats.send` | `{plugin, chatId, text, mode:"auto"\|"queue"\|"steer", images?}` | `{ok, mode, error?}` | ai.sessions |
| `ai.chats.cancel` | `{plugin, chatId}` | `{ok}` | ai.sessions |
| `ai.chats.open` | `{plugin, chatId, view?}` | — | ai.sessions + ui |
| `ai.chats.state` | `{plugin, chatId}` | `{running, seq}` | ai.sessions |
| `llm.providers` | `{plugin}` | `[{id, name}]` | ai.llm |
| `llm.models` | `{plugin, provider}` | `[{id, name}]` | ai.llm |
| `llm.stream` | `{plugin, requestId, provider, model, system?, messages:[{role, content}], maxTokens?, temperature?, reasoningEffort?, tools?, purpose?}`（`content` 是字符串，或含图片时是片段数组；宿主把纯文字片段数组合并成字符串） | `{text, finish:{kind}, usage?}`；过程中 TipCL 发通知 `llm.chunk` | ai.llm |
| `llm.abort` | `{plugin, requestId}` | — | ai.llm |
| `credentials.resolve` | `{plugin, name}` | `{value} \| null` | credentials |
| `credentials.set` | `{plugin, name, value}`（value 为 null 删除） | — | credentials |
| `credentials.status` | `{plugin}` | `{[name]: {configured}}` | credentials |
| `attachments.save` | `{plugin, images:[{data(base64), mediaType, name?}]}` | `[{id, mediaType, name}]` | ai.tools |
| `config.get` | `{plugin}` | `{key: value}` | — |
| `config.set` | `{plugin, key, value}` | — | — |
| `ui.register` | `{plugin, kind, spec}` kind ∈ `page`\|`aiView`\|`dashCard`\|`overlay`\|`settings`\|`action` | — | ui / ui.overlay |
| `ui.unregister` | `{plugin, kind, id}` | — | ui |
| `ui.toast` | `{plugin, text, kind?:"info"\|"success"\|"warn"\|"error"}` | — | ui |
| `ui.confirm` | `{plugin, title, text, ok?, cancel?}` | `true/false` | ui |
| `ui.prompt` | `{plugin, title, text, placeholder?, default?}` | `string \| null` | ui |
| `ui.openPage` | `{plugin, page}`（插件页 id，或内置页 id 如 `launch`、`ai`） | — | ui |
| `ui.overlayUpdate` | `{plugin, id, visible?, width?, height?, anchor?, clickThrough?}` | — | ui.overlay |
| `ui.openExternal` | `{plugin, url}`（只允许 http/https） | — | ui |
| `hooks.register` | `{plugin, hook, priority?}` | — | launcher（`ai.tool.*` 需 ai.tools） |
| `hooks.unregister` | `{plugin, hook}` | — | |
| `plugin.reload` | `{plugin}` | — | — |

`log` 由宿主作为普通请求发出（结果忽略）；`ctx.logger.*` 走它，`console.*` 走 stderr。

UI spec：
- `page`：`{id, label, icon?, order?, view, group?:"top"|"more"}` → 侧栏多一个入口，内容是 WebView 打开 `view`
- `aiView`：`{id, label, order?, view, presets?:[...]}` → AI 页对话顶部多一个标签；`presets` 为空表示所有对话都显示
- `dashCard`：`{id, label, view, defaultW?, defaultH?}` → 首页“添加卡片”里出现
- `overlay`：`{id, view, width, height, anchor?:"bottom-right"|..., margin?, clickThrough?:false, visible?:true}`
- `settings`：`{view}` → 替换自动生成的设置页（自动页仍可从“高级”打开）
- `action`：`{id, label, where:"instance"|"launch"|"tray"|"ai.input", icon?}` → 点击时 TipCL 发 `action.invoke`

### TipCL → Node 宿主（请求/通知）
| method | params | 说明 |
|---|---|---|
| `plugin.load` | `{id, dir, dataDir, main, client?, manifest, config, granted, dev, pageToken, cacheDir?, logFile?}` | 加载并 `apply`；返回 `{ok, error?, pageToken, webPort, warnings?}`（`warnings`：apply 期间被 TipCL 拒绝的注册，如缺权限）。同 id 再次 load = 先卸载再加载 |
| `plugin.unload` | `{id}` | 逆序执行 effect，卸载 |
| `plugin.configChanged` | `{id, key, value}` | 通知（单元格 `.get()` 立即读到新值） |
| `tool.execute` | `{plugin, name, callId, chatId, args, messageText}` | 返回 `{content:[{type:"text",text} \| {type:"image", attachment:{id} \| data, mediaType}], isError?}` |
| `tool.cancel` | `{callId}` | 通知：abort 对应 signal |
| `event` | `{topic, data, plugins?:[id], plugin?}` | 订阅的事件；`plugins`/`plugin` 指明投递给哪些插件（不写时宿主按权限和本地订阅过滤）。宿主会为每个插件订阅 `theme.changed`，有 `ai.sessions` 的再订阅 `ai.event`（同步会话镜像用），同一事件发一次即可，宿主按 `seq` 去重 |
| `llm.chunk` | `{requestId, chunk:{type:"text-delta",text} \| {type:"reasoning-delta",text} \| {type:"tool-call",...} \| {type:"finish", reason:{kind:"stop"\|"length"\|"error"\|"aborted"}, usage}}` | 通知 |
| `hook.invoke` | `{plugin, hook, data}` | 返回修改后的 data（或 `null` 不改）；超时 10 秒视为不改 |
| `action.invoke` | `{plugin, id, context}` | 通知 |
| `ui.event` | `{plugin, kind, id, event, data}` | 通知：页面打开/关闭、浮层被点击等 |
| `host.shutdown` | `{}` | 卸载全部插件后退出 |
| `host.ping` | `{}` | 返回 `{ok, hostVersion, webPort, plugins}`（健康检查） |

### 事件 topic（`events.subscribe` / 网页端 `tipcl.events.on`）
- 启动器：`ui_changed`、`task_added`、`task_count_changed`、`finished`、`game.started`、`game.exited`、`game.crash`、`game.log`（每行 `{instance, line}`）、`update_staged`、`plugins.changed`
- AI：`ai.event`（`{chatId, event}`，见 §5）、`ai.chat.created`、`ai.chat.deleted`
- 凭据：`credentials.updated`（`{name, configured}`，只发给本插件）
- 配置：`config.changed`（`{key, value}`，只发给本插件）
- 主题：`theme.changed`（`{theme, colors}`）

### 钩子（`hooks.register`）
| hook | data | 可修改 |
|---|---|---|
| `launch.before` | `{instance, version, javaPath, jvmArgs[], gameArgs[], env{}, account{name,type}}` | 返回修改后的对象（参数、环境变量） |
| `launch.after` | `{instance, version, pid}` | — |
| `game.exit` | `{instance, version, code, crashed}` | — |
| `ai.tool.before` | `{chatId, name, args}` | 返回 `{block:"原因"}` 拦截，或改 `args` |
| `ai.tool.after` | `{chatId, name, args, result}` | 改 `result` |
| `ai.turn.before` | `{chatId, text}` | 改 `text` / 追加 `context` |

## 5. AI 对话事件模型（满足 dsh-game 的“念工具调用那条消息里的话”、“盯着会话看 turn/end”、游戏标签页的思考终端）

每个对话有递增的 `seq`。TipCL 在内存里为每个对话保留最近 2000 条事件，并通过 `ai.event` 推送。事件：

```jsonc
{ "seq": 41, "type": "turn/start", "data": { "turnId": "..." }, "at": 1730000000000 }
{ "seq": 42, "type": "user/message", "data": { "text": "去玩狼人杀吧！", "source": "user" | "plugin" | "steer" } }
{ "seq": 43, "type": "assistant/delta", "data": { "text": "我先" } }                 // 流式片段（可选消费）
{ "seq": 44, "type": "assistant/reasoning", "data": { "text": "..." } }              // 思考片段（模型支持时）
{ "seq": 45, "type": "assistant/message", "data": { "message": { "content": [
      { "type": "text", "text": "好嘞，开局！" },
      { "type": "tool-call", "id": "call_1", "name": "game_start", "args": { "game": "werewolf" } } ] } } }
{ "seq": 46, "type": "tool/start",  "data": { "callId": "call_1", "name": "game_start", "label": "..." } }
{ "seq": 47, "type": "tool/result", "data": { "callId": "call_1", "name": "game_start", "content": [ { "type": "text", "text": "..." } ], "isError": false } }
{ "seq": 48, "type": "turn/end", "data": { "reason": "stop" | "cancelled" | "error" | "max_rounds", "error": "..." } }
```

**关键时序**：`assistant/message` 必须在执行该消息里的任何工具**之前**发出，`tool.execute` 的 params 里也带 `messageText`（同一条消息的文字），这样插件在工具里就能先念完这句话再推进游戏。

对话与预设：对话记录新增字段 `preset`（预设 id，含插件前缀 `plugin:<id>/<presetId>`）和 `view`。用预设的对话：
- 系统提示词按预设替换或追加；`includeRuntimeContext:false` 时不注入实例状态和长期记忆
- 工具 = 预设白名单匹配到的插件工具（`builtin:true` 时再加内置工具，走原有按需选择）
- `maxRounds:0` 不限步数（硬上限 10000 防失控）
- 插件工具不弹写操作确认（权限在安装时已授予），但 `ai.tool.before` 钩子照常

并发：TipCL 现在同一时间只跑一个 AI 回合。对同一对话 `send(mode:"auto")`：没在跑 → 开新回合；正在跑 → steer（下一步就能看到）。正在跑别的对话时 → 返回 `{ok:false, error:"busy"}`，网页端提示。

工具结果里的图片：Agent 把图片作为下一次请求里紧跟工具结果的一条 `user` 视觉消息（`[工具 game_continue 返回的画面]` + 图片）；模型不支持视觉时只保留文字并注明“(画面已省略：当前模型不支持看图)”。

## 6. LLM 服务

- 供应商 `tipcl`：用户当前的 AI 设置（公益网关或自定义端点），`listModels` 走现有 `Client.ListModels`。
- 额外供应商：插件页“模型供应商”里添加 `{id, name, baseUrl, apiKey}`（OpenAI 兼容；apiKey 存 DPAPI，引用 `provider:<id>`），存在 `plugins.json.providers`。
- `stream`：复用 `Client.ChatStream`，把 `provider/model/maxTokens/temperature` 覆盖进设置；`reasoningEffort: "off"` 时不请求思考（对支持的端点传 `reasoning_effort`/`thinking` 关闭参数，不支持的忽略）。多个 `llm.stream` 可以并发（与 AI 回合互不阻塞）。

## 7. 网页端 SDK（`/__tipcl/sdk.js`，ESM；视图外壳自动加载）

插件的 `client` 模块导出：
```js
export function apply(ui) {
  ui.view('library', (el, props, tipcl) => { /* 挂载，返回卸载函数 */ return () => {}; });
  ui.view('game',    (el, { chatId }, tipcl) => { ... });   // aiView 收到 chatId
  ui.css(`...`);                                            // 注入样式
}
```
`tipcl` 对象：
```ts
tipcl.plugin            // { id, version, dir }
tipcl.theme             // { mode:'light'|'dark', colors:{bg, fg, card, border, accent, muted, danger}, font }，CSS 变量 --tipcl-*，<html data-theme>
tipcl.call(method, params)                  // 任意宿主 API（受权限检查）
tipcl.events.on(topic, fn) => off
tipcl.ai.createChat({ preset, view, prompt, open }) => { chatId }
tipcl.ai.send(chatId, text, { mode }) / tipcl.ai.cancel(chatId) / tipcl.ai.open(chatId, { view })
tipcl.ai.events(chatId, from?) => { seq, events }; tipcl.ai.subscribe(chatId, fn) => off   // 先补历史再推实时
tipcl.ai.state(chatId) => { running, seq }
tipcl.llm.providers() / models(provider)
tipcl.credentials.status() / set(name, value)
tipcl.config.get() / set(key, value) / onChange(fn)
tipcl.nav.open(page) / tipcl.ui.toast(text, kind) / confirm(...) / prompt(...) / openExternal(url)
tipcl.overlay.update({ visible, width, height, clickThrough })   // 只在浮层里可用
tipcl.launcher.call(method, kwargs)
tipcl.asset(path) => URL                    // 插件目录静态文件
```
同源 `fetch('/api/<插件路由>')`、`EventSource`、`localStorage` 照常可用。

WPF 宿主与视图外壳的约定（WPF 已实现，Node 宿主的外壳页 / SDK 要配合）：
- **地址参数**：WPF 打开 `plugins_ui()` 给的 `url` 时，会在查询串里补 `theme=<JSON>`（结构同 `tipcl.theme`：`{mode, colors:{bg, fg, card, border, accent, muted, danger}, font}`），并把视图 props 合并进 `props=<JSON>`（已有 `props` 时按键合并）：aiView 带 `{chatId}`，浮层带 `{overlay: <id>}`，首页卡片带 `{card: <id>}`。外壳页应优先用 URL 里的 `theme` 初始化 CSS 变量 `--tipcl-*` 和 `<html data-theme>`，避免先白后黑的闪烁；`props` 原样传给 `ui.view` 的回调。
- **主题切换**：WPF 对每个存活的视图执行 `window.dispatchEvent(new CustomEvent('tipcl:theme', {detail: theme}))`，同时 `chrome.webview.postMessage({type:'theme.changed', theme})`。SDK 监听其一即可：更新 CSS 变量、`tipcl.theme`，并触发 `tipcl.events.on('theme.changed')` 的回调（不需要内核再发 `theme.changed` 事件给网页端，发了也无妨）。
- **导航**：视图只能停留在插件源（`127.0.0.1:<端口>`）上；跳到别的源、`target=_blank`、`window.open` 都会被拦下并交给系统浏览器。插件页面之间跳转用 `tipcl.nav.open`。
- **自动播放**：WebView2 以 `--autoplay-policy=no-user-gesture-required` 启动，`<audio>`/`<video>`/Web Audio 不需要用户先点一下。
- **浮层透明**：浮层视图的背景是透明的（`DefaultBackgroundColor` 透明 + 窗口 DWM 玻璃）。浮层页面自己的 `html, body` 也要 `background: transparent`。`clickThrough:true` 时整个浮层窗口不接收鼠标（Win32 层面的穿透，CSS `pointer-events` 无法只穿透一部分）。
- **DevTools**：只对开发者链接的插件（`source:"dev"`）或设了环境变量 `TIPCL_WPF_DEBUG=1` 时开启（右键 → 检查 / F12）。

## 8. Node 宿主 ctx（插件 `apply(ctx, config)` 收到的对象）

DSH 兼容为第一目标，dsh-game 的宿主端代码只需改 import（`@deepseek-ai/dsh-tools` 的 `defineTool` 换成 `tipcl:tools` 的同名函数，或宿主直接接受普通对象）。

```ts
ctx.id / ctx.paths { plugin, data, cache, logs } / ctx.manifest
ctx.effect(setup: () => (() => void) | void, label?)
ctx.reload()                                         // 请 TipCL 卸载后重新加载本插件（plugin.reload）
ctx.logger.{debug,info,warn,error}(...)
ctx.get(name) / ctx.provide(name, service) / ctx.inject(names, (ctx) => void)   // 依赖可用时回调，消失时撤销
ctx.tools.register(tool) => dispose                 // DSH 风格 {name, description, parameters:{k:{type,required,description}}, output:{render}, execute(args, exec)}，或 parameters 为 JSON Schema
ctx.presets.register(preset) => dispose
ctx.connection.fetch.register({ path, methods, fetch, requestBody? }) => dispose   // path 可以带 * 通配
ctx.attachments.saveImages([{ data: Uint8Array|Buffer, mediaType, name }]) => [{ id, mediaType, name }]
ctx.launcher.call(method, kwargs) / ctx.launcher.on(topic, fn)
ctx.events.on(topic, fn) => off
ctx.hooks.register(hook, async (data) => data | null) => dispose
ctx.config.get() / ctx.config.set(key, value) / ctx.config.onChange(fn)
ctx.ui.page(spec) / aiView(spec) / dashCard(spec) / overlay(spec) / settings(spec) / action(spec, handler) / toast / confirm / prompt / openPage / openExternal / overlayUpdate
ctx.get('llm')          // { listProviders(), listModels(provider), stream(req) => AsyncIterable<chunk> }
ctx.get('credentials')  // { resolve(name) => {value}|undefined, set(name, value), status() }
ctx.get('sessions')     // { get(chatId) => { id, seq, running, snapshotEvents(from) }, create(...), send(...), cancel(...) }  同步镜像
ctx.get('attachments') / ctx.get('tools') / ctx.get('connection')   // 同上面的对象，兼容 DSH 的 inject 写法
```
插件模块导出：`name?`、`inject?: string[]`（声明需要的服务，缺失时不加载并报错）、`apply(ctx, config)`；也支持 `export default { apply }`。一个插件可以有多个入口（`main` 可为数组，对应 dsh-game 的 `index` + `agent` 两个子插件）。

Node 宿主实现细节（`src/TipCL/Resources/plugin-host/`，类型见 `tipcl-plugin.d.ts`）：
- 模块别名：`tipcl:tools`、`tipcl`、`@deepseek-ai/dsh-tools` 都映射到宿主内置模块（`defineTool` 等，原样返回），`import` 和 `require` 都可以。
- `ctx.paths = { plugin, data, cache, logs, logFile }`；未知属性按 `ctx.get(name)` 取（DSH 的 `ctx.liveBody` 写法）。
- `DSH_HOME` = `plugin-data` 根目录（所以 `$DSH_HOME/<id>` 正好是 `ctx.paths.data`，所有插件一致）；`TIPCL_PLUGIN_DATA` = 本插件数据目录。两者在**导入模块之前**设好（dsh-game 在模块顶层读 `DSH_HOME`）。环境变量是进程级的：`TIPCL_PLUGIN_DATA` 只在本插件的模块求值和 `apply` 期间可靠，之后请用 `ctx.paths.data`。
- `config` 单元格：任何键都能 `.get()`（未声明的读到 `undefined`）；manifest `default` 先填，模块若导出可调用的 `Config`（schemastery）也用它补默认值。
- `tools.register`：执行时 `exec = {callId, chatId, agent:{id: chatId}, signal, messageText}`；返回字符串 → 文字；`{content, isError}` → 原样；有 `output.render` 则用它；图片片段可带 `attachment`（`saveImages` 的结果）或原始 `data`（Buffer/Uint8Array/base64）。抛错 → `isError:true`。执行前若会话镜像还没有这个对话，先回填一次。
- `sessions.get(id)` 同步返回 `{id, seq, running, snapshotEvents(from), send, cancel, open, ready()}`（`snapshotEvents` 返回 `seq > from` 的事件，事件同时带 `at` 和 DSH 的 `time`）；没 `ai.sessions` 权限返回 `undefined`。
- `llm.listProviders()` 是**同步**的（dsh-game 不 await），读宿主缓存（插件加载前预取，之后每 5 秒最多刷新一次）；`stream()` 的 `signal` 中止时发 `llm.abort` 并抛出 `signal.reason`；消费方提前 `break` 也会发 `llm.abort`。
- 所有注册（工具、路由、预设、UI、钩子、事件、provide）都自动记在插件的 effect 栈里，卸载时逆序撤销；返回的 dispose 幂等，可以再包一层 `ctx.effect`（dsh-game 的写法）。卸载时该插件所有进行中的路由流和网页 SSE 连接被关闭（`ReadableStream.cancel()` 会被调用），进行中的工具 `signal` 被 abort。
- ESM 不能真正卸载：重载时入口模块带 `?reload=n` 重新导入，但它 import 的子模块（含 esbuild 拆出的 chunk）仍是旧实例；CJS 入口会清掉插件目录下的 `require.cache`。

## 9. 插件市场（客户端）

- 设置项 `plugin_market_indexes`：索引地址列表（默认空，后端由项目方自建；可以填多个，按 id 合并，先到先得）。
- 索引格式：
```json
{
  "schema": 1,
  "name": "TipCL 官方插件市场",
  "plugins": [ {
    "id": "dsh-game", "name": "大肥鱼玩游戏", "version": "0.1.0", "author": "luckui",
    "description": "...", "icon": "https://.../icon.png", "homepage": "https://...",
    "tags": ["AI", "游戏"], "verified": true,
    "runtime": "node", "engines": { "tipcl": ">=3.3.0" },
    "permissions": ["launcher", "ai.tools", "ai.sessions", "ai.llm", "credentials", "ui", "ui.overlay"],
    "download": { "url": "https://.../dsh-game-0.1.0.zip", "sha256": "...", "size": 12345678 },
    "screenshots": ["https://..."], "changelog": "...",
    "downloads": 0, "rating": 0
  } ]
}
```
- 安装流程：下载 → 校验 sha256（不符直接拒绝）→ 读 zip 里的 `tipcl-plugin.json`（必须和索引的 id/version 一致）→ 检查 `engines.tipcl` 与依赖 → 弹权限确认（未 `verified` 的显示“未审核”）→ 解压到 `plugins/.staging/<id>` → `install.npm` 时跑 npm → 原子替换 `plugins/<id>` → 加载。失败回滚。
- 更新：启动时和手动检查；版本更高即提示；新增权限要重新确认。
- 卸载：先 unload，再删插件目录；询问是否同时删除 `plugin-data/<id>` 和凭据。
- 本地安装：选择 zip 或文件夹（复制）；开发者模式：链接文件夹（不复制、热重载）。
- 未审核插件：设置项 `plugin_allow_unverified`（默认 false），关着时市场里未审核插件的安装按钮要先打开开关。

## 10. BackendApi 新增 RPC（WPF 界面经 BridgeClient 调用）

| 方法 | 说明 |
|---|---|
| `plugins_list()` | 已安装插件：`[{id, name, version, author, description, icon(dataUrl/glyph), runtime, enabled, state:"running"\|"stopped"\|"error"\|"restart_required"\|"loading", error, source, permissions, granted, hasSettings, devPath, updateAvailable}]` |
| `plugins_market(refresh=false)` | 合并后的市场列表 + 每项 `installed`/`installedVersion`/`updatable` |
| `plugins_install(id)` | 后台任务（TaskContext 进度），返回 `{task_id}`；需要权限确认时发 `plugins.confirm` 事件 |
| `plugins_install_local(path, dev=false)` | 本地 zip/文件夹 |
| `plugins_confirm(request_id, ok)` | 回复权限确认 |
| `plugins_uninstall(id, delete_data=false)` | |
| `plugins_set_enabled(id, enabled)` | |
| `plugins_reload(id)` | |
| `plugins_get_config(id)` / `plugins_set_config(id, key, value)` | 自动设置页 |
| `plugins_credentials(id)` / `plugins_set_credential(id, name, value)` | |
| `plugins_ui()` | 当前所有插件 UI 贡献：`[{plugin, kind, id, label, icon, order, url, spec}]`，`url` 已带 pageToken |
| `plugins_ui_reply(request_id, result)` | 回复 `ui.confirm/prompt` |
| `plugins_providers()` / `plugins_set_provider(p)` / `plugins_remove_provider(id)` | 额外模型供应商 |
| `plugins_runtime_status()` | Node 路径/版本、宿主状态、端口 |
| `plugins_log(id, lines=200)` | 读日志尾部（返回字符串；`id="host"` 为 Node 宿主自身的日志，兼容旧写法 `_host`；插件 id 不能用 `host`） |
| `ai_new_chat(preset="", view="")` | 原方法加参数 |
| `plugins_action(plugin, id, context={})` | **（WPF 新增）** 用户点了插件动作按钮（`ui.action`）；内核给 Node 宿主发 `action.invoke {plugin, id, context}`。`context` 至少含 `where`，launch 页带 `instance`/`version`，AI 输入框带 `chatId` |
| `plugins_ui_event(plugin, kind, id, event, data={})` | **（WPF 新增）** 界面事件，内核转成 `ui.event` 通知给 Node 宿主。WPF 发：插件页面 `shown`/`hidden`（kind `page`）、AI 标签 `shown`/`hidden`（kind `aiView`，`data.chatId`）。dsh-game 靠它在游戏库打开时收起大肥鱼浮层 |
| `ai_presets()` | **（WPF 新增）** 当前可用的 agent 预设 `[{id, name, description, plugin, view}]`，`id` 形如 `plugin:<插件>/<预设>`。AI 页「新对话 ▾」菜单和对话列表上的预设标记用它 |

WPF 界面对上表返回值的约定（**内核按这个实现**；字段名 camelCase 或 snake_case 都认，下面写 camelCase）：
- `plugins_install` / `plugins_install_local` / 更新：返回 `{task_id}`（或直接返回 task_id 字符串），进度走现有 `progress`/`finished` 任务事件；同步完成的可以返回 `{id}`。
- `plugins_market(refresh)`：返回数组，或 `{plugins:[...], errors:[{url, message}]}`（某个索引读失败时 WPF 显示“N 个市场地址读取失败”）。每项字段同 §9 索引 + `installed`、`installedVersion`、`updatable`。
- `plugins_get_config(id)`：`{schema:{key: manifest config 字段声明}, values:{key: 当前值}}`。`secret:true` 的字段**不回传明文**：已设置时给非空占位（如 `"******"`），WPF 只在用户填了新值时才 `plugins_set_config`。
- `plugins_set_config(id, key, value)` / `plugins_set_credential(id, name, value=None)`：`value` 为 `null` 表示删除（WPF 用字典传参，null 会原样发出）。
- `plugins_credentials(id)`：`[{name, label, description, configured}]`（manifest `contributes.credentials` + 状态）；也接受 `{NAME:{configured}}`。
- `plugins_providers()`：`[{id, name, baseUrl, configured}]`（`tipcl` 可以列出来，WPF 当作内置、不可编辑）；`plugins_set_provider(p)`：`p = {id, name, baseUrl, apiKey?}`，不带 `apiKey` 表示不改 Key；`plugins_remove_provider(id)`。
- `plugins_runtime_status()`：扁平对象，WPF 逐项显示；建议键 `nodePath`、`nodeVersion`、`hostState`、`hostVersion`、`webPort`、`pid`、`restarts`、`error`。
- `plugins_ui()`：每项 `{plugin, kind, id, label, icon, order, url, spec}`；**.NET 插件的原生界面行上 `native:true`、`url:""`**，WPF 去 `PluginUiRegistry` 拿控件（见 §3.2 补充）；网页视图暂时没有地址（Node 宿主没起来）时 `url:""`、`native:false`，WPF 显示“还没准备好”，等下一次 `plugins.ui_changed`。`kind:"settings"` 的项 `id` 可为空。
- `ai_list_chats()`：每个对话多两个字段 `preset`（预设 id，普通对话为空串）、`view`（默认标签）。
- 设置项：`save_settings` 必须接受并保存 `plugin_market_indexes`（字符串数组）和 `plugin_allow_unverified`（bool），`get_settings` 原样返回。WPF 设置页「插件」卡片和插件市场直接用 `save_settings({data:{...}})` 写这两项。
- 首页卡片类型 `plugin:<插件>:<卡片 id>`：`UiLayout.IsKnownCard` 已放行 `plugin:` 前缀（WPF 这边已改），布局里保存这类卡片不会被过滤掉。

事件（EventBus → WPF）：`plugins.changed`、`plugins.ui_changed`、`plugins.confirm {request_id, plugin, permissions, verified, action}`、`plugins.ui_request {request_id, plugin, kind:"toast"|"confirm"|"prompt"|"openPage"|"openChat"|"openExternal"|"overlay", ...}`、`ai.event`、`plugins.error {plugin, message}`。

事件负载细节（WPF 已按此实现；其余字段平铺在顶层，也接受包在 `params`/`data` 里）：
- `plugins.confirm`：`{request_id, plugin, name?, version?, runtime?, permissions:[...], newPermissions?:[...], verified, action:"install"|"update"|"enable"}`；`permissions` 的元素可以是字符串或 `{id, label, danger}`（界面用 `id`，说明文字用自己的中文表）。WPF 弹权限确认（`runtime` 缺省按 node，自动加 `system` 并显示红色警告；`verified:false` 显示“未审核”），然后 `plugins_confirm(request_id, ok)`。
- `plugins.ui_request` 各 kind 的字段：
  - `toast`：`{text, toastKind?:"info"|"success"|"warn"|"error"}`（也认 `level`）—— 顶层 `kind` 已被请求类型占用
  - `confirm`：`{title, text, ok?, cancel?}` → 回复 `true/false`
  - `prompt`：`{title, text, placeholder?, default?}` → 回复字符串或 `null`
  - `openPage`：`{page}`（同插件的页面 id、完整的 `plugin:<插件>:<页面>`，或内置页 id 如 `launch`、`ai`、`settings`、`plugins`）
  - `openChat`：`{chatId, view?}` → WPF 切到 AI 页、选中该对话（列表里没有会先刷新）、选中 `view` 对应的 aiView 标签。`tipcl.ai.createChat({open:true})` / `ai.chats.open` 都走这个
  - `openExternal`：`{url}`（只开 http/https）
  - `overlay`：`{id, visible?, width?, height?, anchor?, margin?, clickThrough?}`（即 `ui.overlayUpdate`）
  - 只要带了 `request_id`，WPF 都会 `plugins_ui_reply(request_id, result)`：confirm → bool，prompt → string|null，其它 → `true`（未知 kind → `null`）。

## 11. 不做 / 以后做
- 嵌入式脚本运行时（Jint）：Node 运行时已覆盖 JS 插件，先不做；接口留在 `runtime` 字段。
- 市场后端（账号、评分、审核后台）：项目方自建，客户端只认 §9 的索引格式。
- Win7/8.1：WebView2 不支持，插件页面改为在系统浏览器打开同一个地址（功能不变，只是不嵌在启动器里），浮层和首页卡片不可用。

## 12. WPF 界面实现说明（界面层，供各方对照）

- **WebView2 打包**：`Microsoft.Web.WebView2` 托管 DLL 由 Costura 打进 exe；原生 `WebView2Loader.dll`（x64/x86/arm64）作为内嵌资源 `TipCL.WebView2Loader.<arch>.dll`，第一次用 WebView 前释放到 `%TIPCL_HOME%/runtime/webview2/loader/<arch>/` 并 `CoreWebView2Environment.SetLoaderDllFolderPath`（FodyWeavers 里 `ExcludeAssemblies="WebView2Loader"`）。共享一个 `CoreWebView2Environment`，用户数据目录 `%TIPCL_HOME%/runtime/webview2/profile`。
- **运行时检测**：`GetAvailableBrowserVersionString()`；没有运行时（或 Win7/8.1）时每个视图显示兜底面板：「在浏览器中打开」（同一个带 token 的地址）、Win10+ 上「安装 WebView2 运行时」（Evergreen 引导安装器）、「重新检测」。插件页「开发者 → 运行环境」也显示状态。
- **侧栏**：内置入口「插件」在「更多」分区（新用户默认就有；老用户按 `SectionMembersFromConfig` 的规则自动补进「更多」）。插件 `page`：`group:"more"`（默认）挂在「更多」分区末尾；`group:"top"`（或「更多」被隐藏时）放在主列表末尾（分组排法下带一个「插件」小标题）。按 `order`、`label` 排序。图标：1–2 个字符当字形，`data:` / `http(s)` 当图片，其余用默认拼图图标。页面对象缓存，切走再回来 WebView 不重建。
- **AI 页**：对话上方的标签条只在有匹配的 aiView 时出现（`spec.presets` 为空 = 所有对话；预设可写 `game`、`dsh-game/game` 或完整的 `plugin:dsh-game/game`）。选中插件标签时插件视图盖住消息列表和输入框。每个（视图, 对话）一个 WebView，保留最近 4 个。对话的 `view` 或预设的 `view` 决定切到该对话时默认选哪个标签；用户手动选过的按对话记住。
- **首页卡片**：「编辑布局 → 添加卡片」里列出插件卡片，初始大小用 `spec.defaultW/defaultH`（px）。编辑布局时插件卡片里的 WebView 会暂时隐藏（否则会盖住拖拽手柄）。
- **airspace**：WebView2 是独立 HWND，WPF 内容画不到它上面。对话层（`Dlg`）打开时所有不在对话里的 WebView 会暂时隐藏；Toast 在 WebView 区域上会被挡住（已知限制）。
- **动作按钮**：`where:"launch"` 放在启动页工具栏（context `{where, instance, version}`），`where:"instance"` 放在「版本管理」页标题栏（`{where, folder, instance}`），`where:"ai.input"` 放在 AI 输入框上方（`{where, chatId}`）；`tray` 目前没有放置位置（TipCL 没有托盘图标）。
