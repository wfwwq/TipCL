# .NET 原生插件样例(kind = block)

最小可运行的 **.NET 原生插件**:注册一个布局区块,布局文档里用
`plugin:dotnet-block-sample:hello` 引用它。契约见 `docs/plugins/DESIGN.md` §3.2 / §4.F,
类型定义在 `src/TipCL/Core/Plugins/Sdk/PluginSdk.cs`(命名空间 `TipCL.Plugins`)。

与 `hello-plugin/`(Node 插件)的差别:Node 插件走宿主进程 + JSON-RPC;.NET 插件被启动器
`Assembly.LoadFrom` 直接载入本进程,所以:

- 必须与启动器同一 TFM(`net48`),编译期直接引用 `TipCL.exe`;
- manifest 用 `"runtime": "dotnet"` + `"assembly"` + `"entryType"`;
- 界面直接给 WPF 控件(`Func<FrameworkElement>`),不经 WebView。

## 构建 + 跑端到端(一条命令)

```powershell
pwsh -NoProfile -File samples\dotnet-block-plugin\run-e2e.ps1
```

脚本做三件事(每步都会打印原始输出):

1. `dotnet build src\TipCL\TipCL.csproj -c Release`(插件要引用它的 exe);
2. `dotnet build samples\dotnet-block-plugin\DotNetBlockSample.csproj -c Release`;
3. 跑 `tests\TipCL.NativeBlockE2E`(net48 控制台驱动):把插件装进一个**临时 TIPCL_HOME**,
   走真实启动路径(`BridgeHost.StartAsync` → `PluginManager` 发现/加载 → `Assembly.LoadFrom`
   → `Apply` → `RegisterBlock` → `plugins_ui` RPC → 布局内容工厂),然后断言:

| 步骤 | 断言 |
|---|---|
| 1 | 布局侧内容工厂对**不存在的**插件块返回「插件区块不可用」占位(说明这条口子是真的) |
| 2 | 插件被真实加载:`PluginUiRegistry.Find("dotnet-block-sample","block","hello")` 命中 |
| 3 | `plugins_ui` RPC 里出现这个块(`kind=block`) |
| 4 | `WidgetRegistry.Build("plugin:dotnet-block-sample:hello","launch")` 返回插件自己的控件(含样例文字) |
| 5 | 工厂**可重复调用**且每次都拿到新控件(宿主会重复问) |
| 6 | 插件被停用后:注册表条目消失、RPC 里没有它、同一块 id 回到「不可用」占位(块保留、不删数据) |

## 手工安装(在真机上玩)

把这三样放进 `%TIPCL_HOME%\plugins\dotnet-block-sample\`:

```
tipcl-plugin.json
DotNetBlockSample.dll      <- samples\dotnet-block-plugin\bin\Release\DotNetBlockSample.dll
```

然后在插件页启用它(首次启用会问一次 `ui` 权限),到「设置 → 布局 → 编辑」里加一个插件区块,
块 id 写 `plugin:dotnet-block-sample:hello`。

## 为什么 manifest 里**不**声明 `contributes.blocks`

`.NET` 插件的界面是**运行时**用 `ctx.Ui.RegisterBlock(...)` 注册的(`DESIGN.md` §3.2),
manifest 只需声明 `runtime/assembly/entryType` + 需要的权限。**额外**在 manifest 里声明同一个块会踩一个
当前树上的坑(RPC 层缺陷,已在本轮 E2E 里拍到,详见 `tests/TipCL.NativeBlockE2E` 的报告与
`_tmp\tasks\win-weak\e2e-run3.txt`):

- `PluginManager.UiRows()` 把「manifest 声明」与「原生注册表」**两批都塞进同一次 `plugins_ui`**
  (`PluginManager.cs:1346` 的 `"native": false` 行 + `:1366` 的 `"native": true` 行),同一个
  `(plugin, kind, id)` 于是有两行;
- 前端 `PluginUi.Find` 是 `FirstOrDefault`,拿到的是 `native:false` 且 `url:""` 的那一行,
  于是走「插件页面还没准备好」分支 → **.NET 插件的块渲染不出自己的控件**;
- 契约(`docs/plugins/DESIGN.md:443`)说的是「.NET 插件的原生界面行上 `native:true`、`url:""`」,
  所以这属于 RPC 合并层该去重(native 行优先)的地方,不是插件写法问题。

因此本样例只做运行时注册 —— 这条路径在 E2E 里是**全绿**的;如果你确实要在 manifest 里声明,
先把上面那处去重修掉(修完把本样例的 `contributes.blocks` 加回来即可复现两行场景)。
