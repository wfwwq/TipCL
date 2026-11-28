# AI5-C1 · Linux 按需加载改扫最近 12 条 user/tool 消息

日期:2026-11-28
分支:`ai5/linux-tools`(worktree `C:\tipcl-ai5\linux-tools`,基于 `b4cd7a2`)
属主文件:`src/TipCL.Core/Launcher/Ai/Tools.cs`(本包唯一改动方)

---

## 1. 做了什么

| # | 改动 | 位置 |
|---|---|---|
| 1 | 关键词命中不再只看最后一条用户消息:改成**聚合最近 12 条 user / tool 消息**(每条截断 4000 字)再匹配 `KEYWORD_TOOLS` | `Tools.SelectToolSchemas` + 新增 `Tools.SelectionText` |
| 2 | 窗口参数与 Windows 对齐:`lastN = 12`、`limitPer = 4000`(Windows:`Tools._selection_text`,`Tools.cs:107-123`) | `SelectionWindow` / `SelectionCharsPerMessage` 两个常量 |
| 3 | `LastUserText` 保留下来,但**只**给「这一轮是不是在下命令」的 `ACTION_RE` / `QUESTION_RE` 判定用(取舍与实测见 §4) | `Tools.LastUserText` |

对齐口径说明:Windows 扫的是「最近 12 条 `role` 为 `user` 或 `tool` 的消息,单条截 4000 字,`\n` 连接」,然后在这段文本上做关键词命中。Linux 现在同样如此;两处差异保留(都是 Linux 原有口径,不属于本次改动):

- Linux 的关键词匹配是 `OrdinalIgnoreCase`(Windows 是大小写敏感);
- Linux 多一层「写工具只在像在下命令时带」的分支(Windows 没有这一层)。

## 2. 文件清单

| 文件 | 变更 |
|---|---|
| `src/TipCL.Core/Launcher/Ai/Tools.cs` | 改:`SelectToolSchemas` 取文本的方式;新增 `SelectionWindow` / `SelectionCharsPerMessage` 常量与 `SelectionText(messages)`;`LastUserText` 从「选工具的主文本」降级为「命令判定专用」 |
| `tests/TipCL.Tests/Core/AiToolSelectionWindowTests.cs` | 新增 7 条用例(含场景 19) |

没有动 `AiAgent.cs` / `ToolRunner.cs` / `AiData.cs` 的关键词表(关键词表要不要补是 C3 的裁决项,本包不实施)。没有新增 NuGet 包,没有改版本号,没有发 release,没有 push。

## 3. 复现 → 修复 → 用例转绿(三步证据)

### 3.1 复现(旧口径)

把 `text` 临时退回老写法 `userText ?? LastUserText(messages)`(其余不动),跑新用例:

```
dotnet test tests/TipCL.Tests/TipCL.Tests.csproj -c Release --nologo -m:1 -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~AiToolSelectionWindowTests"
```

结果:**失败 3 / 通过 4 / 总计 7**。红的 3 条:

| 用例 | 断言失败点 |
|---|---|
| `FollowUpWithoutKeywords_KeepsToolsFromTheLastTwelveMessages` | 第二轮「再来一个」时 `search_content` 不在下发列表里(收集到的列表只有 `ask_user, get_launcher_state, list_instances, search_mods, list_mods, …` 这批常驻工具) |
| `OnlyTheLastTwelveMessagesAreScanned` | 关键词落在窗口内(第 12 条以内)时 `install_shader` 仍未下发 |
| `OnlyUserAndToolMessagesFeedTheKeywordMatch` | `role=tool` 的工具结果里出现过的词不再参与匹配,`install_shader` 未下发 |

### 3.2 修复

`text` 改回 `userText ?? SelectionText(messages)`。

### 3.3 转绿

```
# 本包用例
--filter "FullyQualifiedName~AiToolSelectionWindowTests"  →  通过 7 / 失败 0 / 总计 7

# 全量(排除会碰真实联机服务 / 弹窗 / 浏览器的三类)
dotnet test tests/TipCL.Tests/TipCL.Tests.csproj -c Release --nologo -m:1 -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName!~Terracotta&FullyQualifiedName!~ClickSweep&FullyQualifiedName!~OpenUrl"
→  通过 1601 / 跳过 11 / 失败 0 / 总计 1612
```

基线(AI5-0:`progress/AI5-0-baseline.md`)是 1594 过 / 11 跳 / 0 败 / 总 1605 → 本包 +7 条用例,**没有新增失败**。

构建:

```
dotnet build src/TipCL.App/TipCL.App.csproj -c Release --nologo -m:1 -p:UseSharedCompilation=false
→  0 error / 1 warning(src/TipCL.App/Pages/Layout/LayoutEditor.cs:196 CS0649,基线里本来就有的那一条)
```

本包不含用户可见文案变更,i18n 基线不受影响。体积红线不归本包裁决(AI5-0 已说明基线本身就超 2,224 B);本包未改任何体积回收相关代码。

## 4. 实现取舍:「像在下命令」为什么仍只看本轮用户话

任务书的修法是「聚合最近 12 条再算**关键词命中**」。实现时有两种口径,我用 C3 的离线回放框架量了两者(请求集与口径见 `progress/AI5-C3.md`,这里的口径是「tools 部分的 token / 单轮」):

| 口径 | tools token/轮 | 相对改动前 | 漏选请求数(39 条有标注) |
|---|---|---|---|
| 改动前(只看最后一条用户消息) | 5034.8 | — | 9(23.1/百回合) |
| A:关键词扫窗口 + 命令判定也扫窗口(把「聚合」用在整段逻辑上) | 5494.0 | **+9.1%** | 6(15.4/百回合) |
| B:关键词扫窗口 + 命令判定只看本轮(**本包实现**) | 5150.8 | **+2.3%** | 7(17.9/百回合) |

B 比 A 省 343 token/轮,原因很具体:窗口里只要有**上一轮**的命令动词(比如第一轮「帮我装个光影包」),第二轮「再来一个」这类续话就会被判成命令 → `ACTION_RE` 分支把 **33 个写工具全部**带上 → 单轮 tools 从约 2.6k token 涨到约 9.7k token(+7.1k)。实测受影响请求 13/40(典型集)、4/12(变体集),平均每个多带 7.1k token。

选 B 的理由:

1. 「这一轮像不像在下命令」本来就是**本轮**属性(它存在的目的是防止纯提问时误装);拿上一轮的话来判断是语义漂移,任务书要求的「关键词命中」聚合没有要求这一步。
2. 任务书 C1 的风险栏写的是「工具表会变大一点,注意 token 预算」——B 的增量(+2.3%)是 A(+9.1%)的四分之一。
3. A 比 B 少的那一次漏选是 `R39 续话:「顺便也发我一份」→ export_diagnostic_report`:A 之所以能"救"回来,靠的是**上一轮的「导出一下」命中 `ACTION_RE` → 把全部写工具倒进来**这个霰弹式兜底,而不是选得更准;它的根因是关键词表没收「导出一下」这种说法,属于 C3 方案 1 要修的关键词缺口(单条 schema 才 174 token)。

如果你更想要「与 Windows 完全同口径(只有一段聚合文本)」,把 `Tools.cs` 里这两行改回同一段文本即可:

```csharp
string text = userText ?? SelectionText(messages);
string turnText = text;   // ← 想让命令判定也扫窗口,就改成这一行
```

(此时 `LastUserText` 可以再删掉。测试里没有锁死这一取舍的用例会因此变红:`EarlierTurnCommand_DoesNotEnableBareWriteTools`。)

## 5. 验收:场景 19

用例 `Scenario19_SecondTurnStillSendsInstallMod`:历史 = 第一轮「装钠」+ 助手回话 + `search_mods` 工具结果 + 第二轮「再装个小地图」,断言第二轮的下发列表里仍有 `install_mod`。

**如实说明**:`install_mod` 本来就在 `AiData.CORE_TOOLS` 里常驻(18 个之一),所以这一条断言在改动前后都成立,它锁的是「别把常驻工具弄丢」,并不能体现本次修复。真正体现差异的是另外三条新用例(见 §3.1 的红名单),以及离线回放的实测:

| 请求(多轮续话) | 下发工具数(改动前 → 改动后) | 漏选(改动前 → 改动后) |
|---|---|---|
| R37「帮我装个光影包」→「再来一个」 | 18 → 21 | `search_content`,`install_shader` → 无 |
| R38「帮我安装个整合包」→「怎么才能装好呢?」 | 18 → 26 | `search_modpacks` → 无 |
| R40「装钠」→「那光影也来一个」 | 21 → 21 | 无 → 无(「光影」在本轮话里,改动前就命中) |

建议 AI5-T 的场景套件把场景 19 写成「续话里装一个**非** CORE 的工具」的形式(例如「再来一个」),否则这条场景对 C1 是不敏感的。

## 6. 遗留问题

1. **C2 未实施**:Linux 侧目前没有「调用了本轮没下发的工具 → 用全量表重发一次」的兜底,也没有 `tool_schema_miss` 事件。所以本次修好后,剩余漏选的用户可见症状仍是「模型手里没有这个工具」。C2 落地后,`progress/AI5-C3.md` 里那 8 次漏选会变成 8 次全量表重发(每次约多 1.2 万 token)。
2. **关键词表的口语覆盖缺口没修**(`disable_mod` / `export_diagnostic_report` / `delete_mod` / `inspect_mod` / `fs_write` 等,见 C3)。这是本包的边界外事项。
   **后续(2026-11-28 同日)**:用户裁决 C3 用「方案 1 + 1b 档」,这些说法已经补进关键词表(`Tools.C3Keywords`),实测典型集漏选 7/39 → 0/39、口语变体集 5/12 → 0/12;详见 `progress/AI5-C3.md`。
3. 窗口从 1 条变成 12 条后,匹配文本变长(每轮最多 ~12×4000 字)。实测对这段逻辑的开销可忽略(纯字符串 `Contains` + 一次正则),但如果将来把窗口调大,值得再看一眼。
4. 场景 19 的断言不敏感(见 §5),已提出给 T 包。

## 7. 要改的文档行

- `docs/DECISIONS.md`:追加一条,记录 C1 的取舍——「按需选工具的关键词命中扫最近 12 条 user/tool 消息(对齐 Windows);『像在下命令』的判定只看本轮用户话,理由是 token 预算(实测 +2.3% vs +9.1%)与语义归属」。
- 其余文档(README / PARITY / plugins DESIGN)与本包无关。
