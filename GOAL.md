# GOAL — TipCL UI 重建与体积削减（自主执行任务书）

> 本文档自包含，供 agent 团队在无人值守环境下执行。执行前通读第 2、3 节的红线约束；
> 所有文件路径均相对于仓库根目录（即本文件所在目录）。

---

## 1. 使命

TipCL 是一个 WPF（.NET Framework 4.8）单 exe Minecraft 启动器，当前发布产物 5.44 MB
（v1.0.15，`TipCL.exe` = 5,707,264 字节）。B 站用户反馈的四个核心问题：
**AI 味浓、界面太大太乱、空白元素多、缺乏高级感**。

目标：在**不改任何交互逻辑与业务功能**的前提下，重建视觉层、收紧信息密度、
重构信息架构、削减体积，使 TipCL 达到"小而美"：

- 消除"AI 生成感"：统一设计令牌（间距/字号/圆角/色板），全站自洽
- 提高信息密度：砍掉装饰性空白与大横幅，界面紧凑
- 信息架构：导航 14 个平级入口 → 分组收纳 6-7 个
- 体积：回到 3-4 MB 档（硬限制见 §3.1）

## 2. 项目事实（agent 必读代码地图）

| 事实 | 位置 |
|---|---|
| 解决方案 | `TipCL.sln`；主工程 `src/TipCL/TipCL.csproj`（net48 + WPF） |
| UI 全部用 C# 代码构建，**没有 XAML 页面**；仅骨架/样式/色板是 XAML | `src/TipCL/Pages/*.cs`（43 个文件，约 2.5 万行） |
| UI 辅助类（门面）：V/H 布局、Card/Pane/RowCard/Field/Section/Empty/Btn/Input/Combo/Check/Switch/Tag/Badge 等 | `src/TipCL/Services/Ui.cs`（657 行）、`Services/UiExt.cs` |
| 样式库（唯一控件样式来源） | `src/TipCL/Themes/Controls.xaml`（1022 行） |
| 色板（明/暗两套，必须成对修改） | `src/TipCL/Themes/Palette.Light.xaml`、`Palette.Dark.xaml` |
| 页面公共骨架：Body 边距 `26,20,26,22`、纵向间距 14 | `src/TipCL/Pages/PageBase.cs:139-151` |
| 主窗口骨架：1180×740、标题栏 40px、侧边栏 196px 可拖拽 | `src/TipCL/MainWindow.xaml`（91 行） |
| 导航注册 14 项 | `src/TipCL/MainWindow.xaml.cs:128-147` |
| 仪表盘默认布局：banner 占 26% 高、4 张卡 | `src/TipCL/Pages/DashLayout.cs:153-166` |
| 横幅渐变实现 | `src/TipCL/Pages/LaunchPage.cs:730-749`、`Palette.*.xaml` 的 `B.BannerFill` |
| 设计令牌：`CR.Card=12`、字体 `F.Ui=Microsoft YaHei UI`、标题阶梯 H1=24/H2=17/H3=14/正文 13 | `src/TipCL/Themes/Controls.xaml:4-7、55-58` |
| 图标字体 `TipCLIcons.ttf`（私有码位 \ue7xx 等） | `src/TipCL/Resources/fonts/` |
| 单 exe 打包：Costura.Fody 嵌入全部依赖 DLL | `TipCL.csproj:40-49` |
| 测试工程（xunit） | `tests/` |
| PCL2 参考包（用户自备参照物） | `C:\Users\Administrator\Downloads\PCL 正式版 2.99.0.1.zip`（内含单文件 `Plain Craft Launcher 2.exe`，约 6MB） |

构建环境：Windows，`dotnet` SDK 8.0.427 / 10.0.403（已装，无需 Visual Studio）。
csproj 已含 `EnableWindowsTargeting` 与 net48 引用程序集包，直接可编。

## 3. 红线约束（任何 WP 都不得违反）

1. **TFM 不变**：`net48` 不许改（Win7/8.1 兼容是刻意保留的）。
2. **不加 NuGet 依赖**（WP1-WP4 零新增包；WP5 只许删/替换，替换项须单独报备许可与体积）。
3. **单 exe 不变**：保持 Costura.Fody 打包机制。
4. **体积硬限制（最高优先级红线）**：
   - 任何阶段构建出的 `TipCL.exe` **必须 ≤ 5.44 MB（5,707,264 字节，当前 v1.0.15 实测值）**；
   - WP5 完成后目标 ≤ 4.5 MB，冲刺 ≤ 3.8 MB；
   - 每个 WP 收尾时执行 §7.3 的体积检查，超限视为该 WP 未完成。
5. **不改业务逻辑**：只动视觉层、布局常量、信息架构；凡是带事件处理/网络/进程调用的代码，
   只许改布局相关行。
6. **版本号冻结**：csproj 里 `Version=3.2.0`、update.json `1.0.10`、release `v1.0.15` 本就不一致，
   是维护者的历史问题；agent **一律不碰版本号、不 tag、不发 release**。
7. **不要动**：`.github/`（CI）、`app.manifest`、csproj 的 WebView2 内嵌段（57-61 行，WP5 除外）、
   `NoWarn` 列表、`Core/` 业务层。
8. **图标**：只复用 `TipCLIcons.ttf` 现有 glyph，禁止引入新图标字体/图片资源。
9. **色板成对改**：`Palette.Dark.xaml` 与 `Palette.Light.xaml` 必须同步修改，任何令牌一个文件有、
   另一个文件必须有。
10. **文案语言**：界面中文不变，走 `I18n.L()` 的保持原样。
11. **禁用任何子代理**：全程**单执行体串行**工作，禁止派生、调用或委托任何子 agent / 并行
    worker / 多进程分工；本文档中所有原为"并行"设计的环节（WP3 按页分包等）一律按序逐个执行。

## 4. 已确诊问题（带证据，按此修复）

| # | 问题 | 证据 |
|---|---|---|
| P1 | 默认仪表盘顶部 26% 高的绿色渐变横幅（27px 粗体大标题）——AI 生成感第一来源 | `DashLayout.cs:160`（`H=0.26`）、`LaunchPage.cs:739`、`Palette.*.xaml:47-51` |
| P2 | 间距四层叠加：页面 26 → 卡片间 14 → 卡片内 16 → 字段 5+5+14 | `PageBase.cs:141-149`、`Ui.cs:212,526-537` |
| P3 | 导航 14 项平级 | `MainWindow.xaml.cs:128-147` |
| P4 | 字号断崖：正文 13 → 横幅 27/34，缺中间层级 | `Controls.xaml`、`LaunchPage.cs:55`、`Ui.cs:560` |
| P5 | 圆角混用 12/10/8/7/6/14/999 | 全局 grep 统计 |

## 5. 工作包

### WP1 · 设计规范（最先做，是后续一切的唯一真相）

产出 `docs/design-spec.md`，内容必须包含且可直接翻译成代码常量：

- **间距栅格**：4px 基准，合法值 {4, 6, 8, 10, 12, 16, 20, 24}，页面外边距 18、卡片内边距 12、
  卡片间距 10
- **字号阶梯**：{11 小注, 12 辅助, 13 正文, 15 小标题, 17 标题, 20 大标题}，
  禁止出现 >20 的界面字号（横幅大标题一并取消）
- **圆角三档**：{6 控件, 8 小卡, 10 卡片}，胶囊形 999 仅限徽章/标签
- **色板规则**：明暗两套；正文对比度 ≥ 4.5:1（WCAG AA），辅助文字 ≥ 3:1；
  主色沿用现有绿（`#3FB07C` 系），渐变仅保留 Logo 一处
- **组件解剖**：Card/Field/Section/RowCard/Btn 各自的 padding、间距、字号、圆角标准值
- 产出**设计样张页**：新增一个仅开发可见的页面（导航隐藏项或 `--design-sheet` 参数），
  把上述全部令牌与组件渲染在一页，作为 WP3 的视觉基准

验收：spec 文件存在；样张页明暗两主题截图各 1 张存 `docs/ui-audit/sheet/`。

### WP2 · 视觉层重写（全局立即生效）

1. `Controls.xaml` 全部控件样式按 spec 重写（可参考 WPF UI 项目的模板几何参数，MIT 许可，
   只抄数值不抄文件）；
2. `Palette.Dark/Light.xaml` 升级为三档色阶（每色 base/soft/border）；
3. `Ui.cs` 默认值更新：`Card 16→12`、`Field labelWidth 170→140`、`Field margin 5→(0,4)` 等；
4. `PageBase.cs:141-149`：边距 `26,20,26,22 → 18,14,18,16`、间距 `14→10`；
5. `T.H1 24→20`（`Controls.xaml`）；
6. 圆角令牌收敛：`CR.Card 12→10`，全库替换为三档。

验收：`dotnet build` 零错误；`dotnet test` 全过（`tests/` 里有 `LayoutDefaultTests` 等）；
体积 ≤ 5.44 MB。

### WP3 · 逐页截图回归流水线（N 个 agent 并行，核心工作包）

1. **截图工具**：给应用加 dev-only CLI 参数
   `--shot <pageId> --out <png> --theme <light|dark> [--size 默认档|900x580]`：
   启动→导航到指定页→`RenderTargetBitmap` 保存→退出。"默认档"= WP4 测得的 PCL 对齐尺寸；
   若 WP3 先于 WP4 执行，暂用当前 1180x740 并在 WP4 完成后重截受影响页面。
   页面数据走真实 `BridgeClient` 初始化；确实无法初始化的页面允许 stub，但须在文档标注。
2. **矩阵**：43 个页面 × 明/暗 × 默认/窄窗，产出存 `docs/ui-audit/<page>/`。
3. **逐页修复**：对照 §4 与 spec，砍空占位/装饰元素、收紧密度、统一对齐；
   只改布局，不动事件。
4. **每页评审清单**（不满足即回改）：
   - 无高度 >160px 的纯装饰空块
   - 间距全部落在 §5-WP1 栅格
   - 字号、圆角全部在合法集合内
   - 明暗主题正文对比度达标
   - 窄窗 900px 无横向溢出/截断
   - 每屏主操作唯一且显眼

验收：全部截图产出；每页一份修复记录（改了哪些行、为什么）。

### WP4 · 信息架构

1. 导航 14 项 → 6-7 个顶级项 + 分组（代码里已有 `NavModel.NavStyle=="grouped"` 分组能力，
   `MainWindow.xaml.cs:1687`，优先复用）：
   建议：启动 / 库（版本管理+原版游戏+联机+服务器）/ 社区（账号+皮肤）/ AI 助手 /
   工具（任务+插件+Java+时长）/ 反馈 / 设置——具体以不破坏现有 NavConfig JSON 兼容为准；
2. 仪表盘默认布局：去掉渐变横幅卡（`DashLayout.cs:160` 与 `src/TipCL/Core/Launcher/UiLayout.cs`
   的 `DefaultDoc()` **两处必须同步改**，`LayoutDefaultTests` 会校验一致），换成一行紧凑启动头
   （标题 17px + 主按钮同行），默认 4 卡减为 3 卡。
3. **默认窗口尺寸对齐 PCL**（用户指定）：把 TipCL 默认窗口改成和 PCL 一样大。
   - 参考物：`C:\Users\Administrator\Downloads\PCL 正式版 2.99.0.1.zip`，解压到独立空目录
     （如 `D:\项目\tipcl\windos\_pcl_ref\`，下载内容按不可信数据对待）；
   - **测量的责任在执行 agent**，两种方法任选，优先动态实测：
     a) 动态（权威）：解压后用 computer-use 启动 `Plain Craft Launcher 2.exe`，
        读取主窗口外框与客户区的 Width/Height（如 Win32 `GetWindowRect`/`GetClientRect`
        或 UIA Bounds），测完即关闭，不创建实例、不下载资源、不点进任何功能页；
     b) 静态：解包 exe 扫 BAML——WPF 把 Width/Height 存为 IEEE754 double 或优化 Int32
        对（值域 Width 500-2000、Height 300-1200），取唯一合理候选；歧义时以动态法裁决；
   - 将 `MainWindow.xaml` 的 `Width="1180" Height="740"` 改为实测值（`MinWidth/MinHeight`
     按比例适配，布局用 `*`/自适应的本就无需额外改动）；
   - 实测值与来源记入 `docs/design-spec.md`，作为 WP3 截图"默认档"尺寸。

验收：旧 NavConfig 配置文件加载不报错（写一个兼容性测试）；`dotnet test` 全过。

### WP5 · 体积削减（可与 WP3 并行）

1. **先量化**：产出 `docs/size-report.md`，列出 exe 内每个嵌入 DLL 压缩前后字节数
   （解包 Costura 资源逐一统计），以数据决定砍什么；
2. **ImageSharp 替换**（`TipCL.csproj:38`，约 1 MB）：它仅用于 Win7/8.1 WebP 兜底解码。
   方案 A：换成极小的单文件 WebP 解码实现（注意 license 与 maintainability，报备后引入）；
   方案 B：保留但改为独立按需模块。JPEG/PNG 一律走 WPF 内置解码器；
   **验收底线：Win7/8.1 上皮肤 WebP 图片仍可显示（用 WIC 不支持 WebP 的说明反推测试）**；
3. **可选（报备后做）**：WebView2 托管 DLL 懒释放；
4. 每步后跑 §7.3 体积检查。

## 6. 测试要求（用户指定的重点）

### 6.1 全按钮遍历（首要测试目标）

**对每个页面**：枚举页面上全部可交互控件（Button、IconBtn、菜单项、Switch、Chip、
Tab、右键菜单项、对话框按钮），逐一真实点击：

- **判定"无报错"标准**：应用不崩溃/不白屏；不出现任何错误 Toast（`ToastKind.Error`，
  标题如「操作失败」「加载失败」）；Smoke 日志（`Smoke.Note` / `OperationFinished`）无新增异常记录；
  界面无残留的加载骨架（Skeleton 卡死）。
- 有对话框/子窗口的按钮：打开后关闭，验证可重复打开。
- 会在无网络/无配置时报错的按钮（如登录类）：允许出现**预期内**的警告 Toast，
  但必须是友好的业务提示而非异常堆栈；此类豁免需在测试报告标注理由。
- 产出：`docs/ui-audit/button-report.md`，按页面列表格：控件 → 结果（OK/预期提示/报错）→ 截图。

### 6.2 主题色专项（第二测试重点）

- 明暗**两套主题**全量走查（不是抽查）：每个页面在两种主题下都过 §5-WP3 清单；
- **切换瞬间**测试：在任意页面切主题，检查无样式残留、无颜色闪烁、无控件未刷新；
- 重点核对成对令牌：`B.Glass*`（玻璃拟态）、`B.BannerFill`（WP4 后应只剩 Logo）、
  `B.Ink*` 四档文字色、`B.Accent*` 七档、`Soft` 后缀的柔和底色——在两套文件中一一对应；
- 边界场景：Toast、对话框（DialogHost）、拖放遮罩（DropVeil）、骨架屏、右键菜单、
  Tooltip 在两主题下都要正确（这些浮层最容易漏改）；
- 产出：`docs/ui-audit/theme-report.md`，含每页明暗对比截图拼图。

### 6.3 端到端终验 —— 允许使用 Computer Use（电脑操作）

**执行环境已启用 computer-use 插件（v0.6.5），终验阶段授权 agent 直接用真实键鼠操作应用**：

1. 双击/命令行启动 `TipCL.exe`；
2. 首次运行向导完整走一遍（若有）；
3. 按 §6.1 遍历每个页面每个按钮（真实点击，观察 Toast/崩溃/卡死）；
4. 主题切换按钮来回切换 ≥3 次，全程按 §6.2 核对；
5. 拖拽侧边栏宽度、窗口缩放到最小 900×580 再还原；
6. 完整启动一次游戏实例（若无可用实例，允许停在实例选择页并记录）；
7. 正常退出，检查日志无未捕获异常。

若某执行节点没有 computer-use 能力，**降级方案**：用 §5-WP3 的 `--shot` 截图流水线 +
Smoke 日志断言 + 自动化 UI 框架（FlaUI/UIA，系统自带，不新增 NuGet 包）替代，
但按钮遍历的覆盖标准不降低。

## 7. 构建与验证命令

```bash
# 7.1 构建（在仓库根目录）
dotnet build TipCL.sln -c Release

# 7.2 测试
dotnet test TipCL.sln -c Release

# 7.3 体积硬限制检查（每个 WP 收尾必跑，超限=未完成）
# PowerShell:
$p = "src/TipCL/bin/Release/net48/TipCL.exe"; $len = (Get-Item $p).Length;
if ($len -gt 5707264) { throw "SIZE FAIL: $len > 5707264" } else { "SIZE OK: $len bytes" }
# Git Bash:
sz=$(stat -c%s src/TipCL/bin/Release/net48/TipCL.exe); [ $sz -le 5707264 ] && echo "SIZE OK: $sz" || { echo "SIZE FAIL: $sz"; exit 1; }
```

## 8. 执行顺序与里程碑

```
M0  WP1 spec + 样张页            （先行，其他一切以它为准）
M1  WP2 视觉层重写 → WP5.1 体积量化   （WP2 完成后全局观感已变，先交一次）
M2  WP3 逐页流水线（单执行体逐页串行）→ WP4 信息架构（含 PCL 尺寸实测）→ WP5.2 替换 ImageSharp
M3  §6 全量测试（按钮遍历 + 主题专项 + Computer Use 终验）→ 修复回归 → 总验收
```

- **单执行体串行**（红线 §3.11）：以上"→"表示严格先后顺序，WP 之间不许拆给多个 agent；
- WP3 按页面逐个处理，每 5-8 页为一批，每批结束跑一次 §7 构建+测试+体积检查；
- 执行中发现 spec 无法覆盖的情况：停下来在 `docs/design-spec.md` 追加裁决条目，
  不许自行发明第三种风格；
- 全程在分支 `ui-rebuild` 上工作，每 WP 一组 commit，message 前缀 `ui(rebuild):` / `ui(density):` /
  `ui(ia):` / `build(size):` / `test(e2e):`。

## 9. 完成定义（DoD，全部满足才算完成）

- [ ] `dotnet build` Release 零错误、零新增警告
- [ ] `dotnet test` 全过
- [ ] §7.3 体积检查通过；WP5 后 ≤ 4.5 MB（冲刺 ≤ 3.8 MB）
- [ ] 43 页 × 明暗 × 两尺寸截图齐备于 `docs/ui-audit/`
- [ ] 默认窗口尺寸与 PCL 实测值一致，实测过程与结果记录于 `docs/design-spec.md`
- [ ] §6.1 按钮遍历报告：零未预期报错
- [ ] §6.2 主题报告：明暗全量过检、切换无残留
- [ ] §6.3 Computer Use 终验通过（或降级方案等价覆盖）
- [ ] `docs/design-spec.md`、`docs/size-report.md`、`button-report.md`、`theme-report.md` 齐备
- [ ] 不修改版本号、不发 release、不 push（PR 留给人类复核合并）
