# M4-H R21i:收尾 —— 在 Linux 上真跑起来之后(2026-11-13/14)

本轮没有新功能主线,做的是**把「只在 Windows 上验证过」的部分真正拿到 Linux 上跑一遍**,并把
跑出来的问题修掉。三件有价值的事:**CI 工作流做了真解析校验**、**Linux 上第一次真跑测试套件(发现 3 个真
问题 + 1 个环境问题)**、**补齐参考版的启动前账号闸门**。

---

## 1. CI 工作流(`.github/workflows/ci.yml`,R21h 新增)第一次被"真解析"

R21h 里这条流水线只有结构自检(手写脚本数缩进)。本轮装了 PyYAML 6.0.3 做**真解析 + 交叉核对**
(`_cmp/validate-ci.py`,开发工具不进仓),结论 0 问题:

| 检查项 | 结果 |
|---|---|
| `yaml.safe_load` 真解析 | OK;顶层 `name` / `on` / `env` / `jobs` 都在(PyYAML 按 YAML 1.1 把裸 `on` 读成布尔 `True`,取值时按此处理) |
| 触发器 | `push`(所有分支)+ `pull_request` + `workflow_dispatch` |
| 结构 | 3 个 job(`test` 4 步 / `package-linux` 9 步 / `package-win` 5 步 = 18 步);每步 `run` 与 `uses` 二选一;`uses` 只有三个且都钉版本;`needs` 无悬空、无环 |
| 引用路径 | workflow 里出现的仓库路径逐个存在(`src/TipCL.App`、`tests/TipCL.Tests/TipCL.Tests.csproj`、`tools/package.sh`) |
| 与打包脚本一致 | `--appimage-only` / `--deb` 与 `tools/package.sh` 的开关完全对应;产物名一致(`dist/TipCL-linux-x64.AppImage`、`dist/*.deb`、两个 zip);`.gitignore` 确实忽略 `dist/`;无旧产物名残留 |

**仍然没变**:本仓没有 remote,这条流水线**一次都没在真 runner 上跑过**。上面是"解析 + 静态交叉核对",
不是"跑通了"。

### 1.1 CI 里 `dotnet test` 为什么不带 `DotNetHostPath`(实测,不是推断)

本地一直要带 `-- RunConfiguration.DotNetHostPath=<dotnet.exe>`,CI 里没带。本轮把这条问清楚:

```
本机(不带该参数) → 失败 3 / 通过 0 / 总计 3,exit 1
异常: TipCL.Tests.TestIsolation.Init() → System.IO.Directory.CreateDirectory
```

与 PROGRESS「M4-G 环境注意」记的完全一致:本机仓库目录带可继承的**低完整性标签**,`testhost.exe`
以低完整性启动、写不了 `%TEMP%`。**这是本机环境问题,不是测试工程缺陷**,干净 runner 上没有这层标签 ——
所以 CI 里就该用普通 `dotnet test`,给它传 `DotNetHostPath` 反而是错的。

### 1.2 CI 那步 Release 构建的判据成立

`dotnet build src/TipCL.App/TipCL.App.csproj -c Release --nologo`(与 CI 同一命令):**0 警告 0 错误**。

---

## 2. Linux 上第一次真跑测试套件(本轮最重要的一段)

**做法**:盒子上 `apt-get install dotnet-sdk-8.0`(装上 8.0.134),源码用 `git archive` 打成 zip 推过去
(只含 tracked 文件,1.83 MB),然后跑与 CI **完全相同**的命令。

结果:跑起来了,而且立刻发现问题。以下是逐条。

### 2.1 `ShortcutTests.Cli_Launch_RunsGameWithShortcutArgs`:一条从未执行过的用例

这条用例的正文**只在 Linux 上执行**(开头 `if (OperatingSystem.IsWindows()) return;`),所以在 Windows 上
一直"绿"着,其实一行都没跑过 —— 第一次真跑就是红的(`退出码 3`)。诊断过程中夹具缺了三件东西,
**产品侧逐条与参考版相同**(不是缺陷):

| 缺什么 | 症状 | 参考版对应位置(同一条规则) |
|---|---|---|
| 账号 | `LaunchError: 启动游戏前请先创建一个离线用户(「账号」页 → 添加离线账号)` | `Core/BackendApi.cs:2881-2883` `_require_launch_account` 抛同一句 |
| 客户端 jar | `客户端 jar 缺失: .../versions/sc-1.0/sc-1.0.jar\n请重新安装版本 sc-1.0。`(预检先给一条 warn「客户端 jar 未直接找到」) | `Core/Launcher/McLauncher.cs:525` 抛、`Preflight.cs:113` 警告 |
| `arguments.game` | 断言 `Assert.Contains("--version", args)` 失败:启动器**只做占位符替换**、不会凭空补 `--version` | 同上:`McLauncher.BuildLaunchCommand` 只替换 `${...}` |

修夹具:唯一名字的离线账号 + `--account <名字>` 显式指定(避免与并行跑的 `AccountPageTests` 抢
"当前账号"这个共享状态)、占位 jar、以及**真实形状**的 `arguments.game`
(`--username ${auth_player_name} --version ${version_name} --gameDir ... --accessToken ...`)。
另外把 `Assert.Equal(CliExit.Ok, code)` 换成带 CLI 输出的 `Assert.True(..., $"CLI 退出码 {code};CLI 输出:\n{output}")`
—— 这条用例失败时如果不带现场,根本没法查。

**修完的实测输出(第一次真的跑通整条链)**:

```
[cli-launch] 实例: sc-419e135b | 版本: sc-1.0
[cli-launch] 实例 Java 设置: 自动选择
[cli-launch] 账号: cli-5a2c1993 (离线)
[cli-launch] Java -version: openjdk version "17.0.9" 2023-10-17
[cli-launch] 使用 Java 17: /tmp/tipcl-shortcut-.../fake-java/java
[cli-launch] 实际启动: /tmp/tipcl-shortcut-.../fake-java/java
[cli-launch] 游戏已退出,退出码 0
Passed!  - Failed: 0, Passed: 1, Total: 1
```

即:`.desktop 命令行 → CLI 解析 → 账号/Java 解析 → 拼出游戏命令行 → 真的把进程拉起来` 这条链在 Linux 上
是通的(游戏进程由假 java 顶替,它把收到的参数写进日志 —— 这也是 D-35 定的判据口径)。
顺带说明:`内存: None MB` 这行**不是** Linux 的差异,参考版 `BackendApi.cs:3047` 打印的也是同样的
`Py.Str(memory_mb)`(CLI 不传内存就是 None;真正的 `-Xmx` 由 `BuildLaunchCommand` 默认为 4096)。

### 2.2 行内码底色用例:一条过严的断言(平台无关的脆弱点)

`MdViewInlineCodeTests.InlineCode_BackgroundIsActuallyPainted` 里对照段落断言"整帧里恰好 0 个 `#F2F2F2` 像素":

```
Expected: 0 / Actual: 1     (Ubuntu;Windows 上确实是 0)
```

字形栅格化/抗锯齿逐平台不同,文字边缘可以凑出恰好等于底色的像素。判据改成"对照 ≤ 5 且带行内码 > 100
且 > 10 × 对照",区分度没丢,也不再因为一个边缘像素判死。

### 2.3 `Motion` 的收尾竞态:未被观察的任务异常会**打崩进程**(真缺陷,已修)

测试跑到一半宿主直接崩:

```
The active test run was aborted. Reason: Test host process crashed
[ERROR] unhandled(Task)
[ERROR] System.AggregateException: ... (Unable to locate 'Avalonia.Animation.IGlobalClock'.)
 ---> System.InvalidOperationException: Unable to locate 'Avalonia.Animation.IGlobalClock'.
   at Avalonia.Animation.Animatable.OnPropertyChangedCore(...)
   at Avalonia.Media.TranslateTransform.set_Y(Double value)
   at TipCL.App.Common.Motion.<>c__DisplayClass4_1.<FadeIn>b__1() in Common/Motion.cs:line 43
```

链条:`Motion.FadeIn` 用 `DispatcherTimer.RunOnce` 在 220ms 后写 `translate.Y = 0`;如果这时 Avalonia 的
平台/服务定位器已经拆掉(窗口关掉、会话收尾、测试结束),这次写值就抛异常 → **未被观察的任务异常** →
终结器线程重抛 → **进程终止**。真机运行期不会走到这里(那时平台还活着),但"关窗瞬间正好有待跑的淡入
定时器"在真机上同样可能发生,而它的后果是崩溃而不是一条日志。

修法:`Motion` 里所有延后回调统一走一个新的私有 `Later(delay, body)`,内部 `try/catch` + 一条
`AppLog.Warn("动画收尾回调在平台拆掉之后触发,已忽略: ...")`(与 D-62 对未观察异常的分流口径一致:
能识别、可解释、只记一条,不升级成崩溃)。三处回调(`FadeIn` 带位移 / 不带位移 / `FadeOut` 的 `done`)全部改掉。

### 2.4 测试宿主内存:环境性 OOM(如实记为"未定位",CI 用分批规避)

修完 2.3 之后仍然会崩,而且崩的位置每次不同(148 / 288 / 381 条)。`dmesg` 给出了真正的答案:

```
oom-kill:constraint=CONSTRAINT_NONE ... global_oom ... task=dotnet,pid=164126,uid=1000
Out of memory: Killed process 164126 (dotnet) total-vm:275212804kB, anon-rss:4936736kB
```

**是内核 OOM 杀掉了测试宿主**,不是代码崩溃。逐进程采样给出了形状:测试宿主 RSS 以约 200 MB/s
线性增长(2s:250MB → 12s:936MB → 27s:3600MB → 43s:5070MB 被杀);分批跑则很轻:

| 批次 | 用例数 | 峰值 RSS | 耗时 |
|---|---|---|---|
| `FullyQualifiedName~TipCL.Tests.App` | 239 通过 | **0.43 GB** | 10 s |
| `FullyQualifiedName!~TipCL.Tests.App` | 200 通过 + 1 跳过 | **0.43 GB** | 9 s |
| 整批(同一宿主) | 到 289 条被杀 | 5.07 GB | 44 s |

**口径**:增长**根因没有定位到具体代码**(在测试宿主内部,性质上像 Avalonia 无头会话每派发的开销),
这里不写成"已解决",只写成"已规避 + 已量化"。规避方式:
* CI 的 `test` 步骤改成**两批**(`.github/workflows/ci.yml`,注释里写了这条实测理由);
* 盒子上的验收也按两批跑。

注:Windows 上同一套整批跑没事(有页面文件、不会 OOM),这正是"只在 Windows 上验证"会漏掉这类问题的原因。

---

## 3. 悬停重挂 `Res.Bind` 的订阅语义(PARITY §9.5 里那条"未验证")

PARITY 当时把"悬停每次重挂 `Res.Bind`,旧订阅会不会漏"列成未验证。本轮给出**框架源码级**答案 + 可执行断言:

* Avalonia 11.3.2:`AvaloniaObject.Bind(AvaloniaProperty, IBinding, anchor)`(`src/Avalonia.Base/AvaloniaObject.cs:659`)
  → `ValueStore.AddBinding(property, expression)`(`src/Avalonia.Base/PropertyStore/ValueStore.cs:48`);
  该分支在 `priority == BindingPriority.LocalValue` 时**先** `DisposeExistingLocalValueBinding(property)`
  (同文件 `1238-1245`)**再**装新绑定。`!property = binding` 这条索引器写法与 `Res.Bind` 走的都是 LocalValue
  ⇒ **重挂必然释放旧订阅**。
* 三条用例(`tests/TipCL.Tests/App/RebindSubscriptionTests.cs`):
  1. `Rebind_ReleasesThePreviousSubscription`:计数 observable 走**同一个** ValueStore 入口,第二次绑定后
     第一条的活跃订阅必须归零(用例内部自带反证:绑定后先断言计数为 1);
  2. `Rebind_ManyTimes_LeavesExactlyOneLiveSubscription`:悬停那样来回 100 轮,活跃订阅恒为 1(泄漏会是 199);
  3. `LinkHoverRebind_KeepsFollowingTheTheme`:真链接控件按悬停写法重挂 200 次后切深浅色,字色必须等于
     **当前**主题对应键的颜色。⚠ 如实写:第 3 条**不能**单独证明订阅被释放(泄漏时最后订阅的那条仍会最后
     推值,颜色看着也对),它证明的是用户看得见的那件事;释放由第 1 条证明。

---

## 4. 参考版的「启动前账号闸门」在 Linux 侧缺失 —— 本轮补齐

这是"在 Linux 上真跑测试"牵出来的**产品级缺口**(不是测试问题)。

**参考版做法**(`Services/AccountGate.cs` + `Pages/LaunchPage.cs:873/1287-1295`):
启动页的「用户」是**真实账号下拉**(`_acc = Ui.Combo()`,填 `AccountGate.ListAsync()`,排除哨兵名"离线模式");
点「启动」先 `EnsureLaunchAccountAsync()` → `EnsureAsync`:选中的账号还在就用它 → 否则用列表第一个 →
一个账号都没有就弹「创建离线用户」(名字输入 + 默认皮肤下拉默认/Steve/Alex,「创建并继续」,名字校验
1–16 字符不含空白),建好之后把下拉补齐再继续;用户取消才真的不启动。

**Linux 侧补前**:「用户」是一个**自由文本框**(`UiClass.Input("Player")`),没有闸门。后果:手输的名字必须
与「账号」页里已有账号**逐字相同**才能启动;一个账号都没有时点「启动」只会拿到
`LaunchError: 启动游戏前请先创建一个离线用户` —— 而界面本身没有一条能直接往下走的路。

**本轮补齐**(逐条对齐参考版):

| 件 | 内容 |
|---|---|
| 新文件 | `src/TipCL.App/Common/AccountGate.cs`:`IsSentinel` / `ValidName` / `ListAsync`(排除哨兵、当前账号最前)/ `EnsureAsync` / `CreateOfflineAsync`;对话框做成**可注入**的一步(`Prompt`,测试不真弹窗) |
| 启动页 | `_user` 由 `UiClass.Input("Player")` 换成 `UiClass.Combo()`,在 `FillCardDataAsync` 里用 `AccountGate.ListAsync()` 填、保持上次选中的账号;`LaunchAsync` 里先 `await AccountGate.EnsureAsync(...)`,`null` 就直接不启动;选中账号变化时写 `last_user` |
| 浮层探针 | `ConfigProbeControls` 里那一项由「用户 TextBox」改成「用户 ComboBox」(标签给人看,不影响断言逻辑) |
| 用例 | `tests/TipCL.Tests/App/AccountGateTests.cs` 6 条:名字口径、名单排除哨兵且当前账号最前、已有账号时**不弹窗**、注入对话框建号(真落库、type=offline)、取消返回 null 且不建号、选中的账号已失效时回退到真账号 |

**验证**:`dotnet build -c Release` 0 警告 0 错误;6 条用例全绿;`SMOKE_PROBE=popup-theme` 的 B 组
(真实「启动配置」浮层里的数据控件)**10/10 都拿到隐式主题与尺寸**(其中「用户 ComboBox」从 0×0 变成 276×32,
说明它确实进了宿主链);C 组(刻意保留的反证:不挂树的 popup)照旧 0/5 —— 探针整体 `POPUP-THEME OK`。

**如实说明一条判据的失效**:PARITY §9.8 记过"8/8 张启动页冒烟帧逐位相同(`070d6ad6682d6a86...`,564,149 B)"。
本轮连续两次同样条件下的 `SMOKE_PAGE=launch` 冒烟帧**哈希不同**(573,359 B / 573,327 B)—— 启动页那条线上新闻
行的内容/排版会变,所以"与归档基线逐位相同"在这个环境下**已经不成立**。本轮改的控件只在**浮层**里
(默认关闭),主帧逻辑上不受影响;这条改动由**结构性探针 + 单测**证明,不再靠像素比对。

---

## 5. 数字

| 项 | 值 |
|---|---|
| 测试基线 | **446**(439 → 445 通过 + 1 跳过);本轮新增 3(RebindSubscriptionTests)+ 6(AccountGateTests) |
| 本地(Windows) | 分批跑:App 组 + 其余组,0 失败 |
| 盒子(Ubuntu 24.04,dotnet 8.0.134) | 分批跑:239 + (200 通过 + 1 跳过)= **440**,0 失败;峰值各 0.43 GB |
| 构建 | Debug / Release 均 **0 警告 0 错误** |
| 浮层探针 | B 组 10/10,整体 `POPUP-THEME OK` |

---

## 6. 本轮仍未做的(如实)

1. **CI 从未在真 runner 上跑过**(本仓无 remote);YAML 只做了真解析 + 静态交叉核对。
2. **`.deb` 从安装路径拉起 GUI** 还没跑(脚本已备:`_cmp/box/deb-gui.sh`,用私有 Xvfb `:8` 隔离,
   装 → 跑 `tipcl` → 截图 → 卸);此前只验证到 CLI(`tipcl --ai-selftest` 54/54、`dpkg -i/-r` 干净)。
3. **测试宿主内存增长的根因未定位**(只量化 + 分批规避)。
4. **启动页浮层的真机点击**没做:本轮改动在浮层里,盒子上的真机判据是浮层探针(结构性),没有人工点一遍
   "一个账号都没有 → 点启动 → 弹创建对话框 → 建号 → 继续"。这条留给下一次实机验收。
5. 两个真机 chip 验证(存档管理其余 chip)由子代理在盒子上跑,结果另记。
