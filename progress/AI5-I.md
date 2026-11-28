# AI5-I「光说没做」检测评测集(数据部分)

- 包号:AI5-I(任务书 §7 I 包,来源 a2:WP-AG8)
- 日期:2026-11-28(ISO 8601)
- 分支:`ai5/intent-i`(worktree `C:\tipcl-ai5\intent-i`)
- 范围:**只做标注数据**。`ClaimsAction`/`WantsAction` 的实现与接线由另一个子代理负责,本包不改任何生产代码,不改 `TipCL.Tests.csproj`(原因见第九节)。
- 红线遵守:未改版本号、未发 release、未 `git push`。

---

## 一、文件清单

| 文件 | 内容 | 条数 |
|---|---|---|
| `windos/tests/TipCL.Tests/Data/ai-claims-eval.jsonl` | 回合级配对样本:用户输入 + 模型回复 + 本回合实际执行过的工具 + 期望判定 | 63 |
| `windos/tests/TipCL.Tests/Data/ai-wants-eval.jsonl` | 用户输入侧样本:一句话该不该被当成「要求动手」 | 60 |

两张表都满足任务书的数量要求:用户输入 ≥50(63 条 `user_input` + 60 条 `user_input`)、模型回复 ≥50(63 条 `model_reply`)。拆成两个文件的理由:两侧判据不同(一个看「回复 vs 实际执行」,一个只看用户文本),混在一个文件里字段会大面积为空,反而难用。要单独扩充任一侧时也不会牵动另一个。

编码:UTF-8(**无 BOM**),LF(文件里 0 个 CR 字节)。每行一条记录,行尾无逗号。

## 二、为什么选 JSONL 而不是 JSON

1. **一行一条样本,git diff 只显示被改的那条**:JSON 数组里改一条会让 diff 覆盖整块,评测集是要长期增删的文件,diff 可读性直接影响评审成本。
2. **坏行局部化**:JSONL 解析失败只丢一行,能靠行号定位;整份 JSON 挂掉则一条都用不了。
3. **方便直接喂判定逻辑与命令行工具**:读一行 `System.Text.Json`/`ConvertFrom-Json` 就能构造一个用例,不需要先反序列化整棵树;`grep`/`jq` 也能直接筛「所有 `expect_claim_flagged=true` 的 install 样本」。
4. **方便流式追加**:后续按真实 trace 补充样本时,追加行即可,不必重写文件。

仓库里目前没有 `.jsonl` 先例(`windos/tests/TipCL.Tests/fixtures/` 只有一个二进制样本 `webp-sample.webp`),所以这里以「一行一例、字段名用 snake_case(与 `PyDict` 键、`ToolMeta` 字段一致)」作为本目录的风格基准。

## 三、字段定义

### 3.1 `ai-claims-eval.jsonl`(核心:回复声称 vs 本回合实际执行)

| 字段 | 类型 | 含义 |
|---|---|---|
| `id` | string | 样本 id,`claim-001` … `claim-063`,稳定不复用 |
| `category` | string | 本条的**话题主类别**(install / remove / modify / create / diagnose / launch / memory / info / chat) |
| `lang` | string | `zh` / `en` |
| `user_input` | string | 用户这一句原话 |
| `model_reply` | string | 模型这一回合的最终回复(判定逻辑的输入) |
| `stop_reason` | string | 该回合的终止原因(`no_tool_call` / `completed`),与 `StopReason.cs` 口径一致 |
| `tools_this_turn` | array | **本回合实际执行过的工具调用**,元素 `{name, status}`;`status` ∈ `ok`(成功)/`error`(报错)/`denied`(用户拒绝)。`ok` 是「成功执行」的唯一判据 |
| `claim_categories` | array | **期望从回复里提取出的「声称动作类别」**(人标注的 ground truth);无声明则为 `[]` |
| `executed_categories` | array | 人标注的「本回合成功执行覆盖到的类别」,由 `tools_this_turn` 里 `status=ok` 的工具按第四节映射推出;写操作未成功则不含该类别 |
| `claim_kind` | string | 声明类型:`assert_done`(声称已完成)/`in_progress`(声称正在做)/`report_failure`(如实说明失败或被拒绝)/`advice_only`(只建议或解释)/`question_only`(只追问)/`readonly_report`(只转述只读结果)/`none`(无) |
| `expect_claim_flagged` | bool | **是否应判为「光说没做」**(核心标签) |
| `edge_case` | bool | 该条判据依赖额外约定(见第九节),建议单列评估 |
| `label_confidence` | string | `high` / `medium`;算误判率时建议先只用 `high` |
| `reason` | string | 判定理由(人话,可直接当评审依据) |
| `baseline_note` | string(可选) | 对**现行** `_CLAIM_RE` 行为的推演(漏报/误报)。仅为手工推演,不是跑代码得到的,用来算「不高于现状」的参考基线 |
| `note` | string(可选) | 该样本的额外提醒(例如「失败被说成成功」) |

`claim_categories` 与 `executed_categories` 的存在是为了让判定逻辑与评测脚本**不必各自重写一套类别映射**:判定逻辑自己按第四节规则算,评测脚本直接用这两列做期望值对比,不一致就说明映射实现有偏差。

### 3.2 `ai-wants-eval.jsonl`(用户输入侧)

| 字段 | 类型 | 含义 |
|---|---|---|
| `id` | string | `want-001` … `want-060` |
| `category` | string | 同上 |
| `lang` | string | `zh` / `en` |
| `user_input` | string | 用户这一句 |
| `expect_wants_action` | bool | 期望:`true` = 用户是在要求 agent 动手做事;`false` = 观察/提问/闲聊,不该进「待办」语义 |
| `hint_tools` | array | 参考工具名(可空);只作路由提示,不是断言目标 |
| `edge_case` | bool | 边界样本 |
| `label_confidence` | string | `high` / `medium` |
| `reason` | string | 判定理由 |
| `baseline_note` | string(可选) | 现行 `WantsAction`(Agent.cs:435-455)行为的**手工推演**,用于算「误判率不高于现状」 |

## 四、类别体系(与 `ToolMeta.side_effect` + 工具名前缀对齐)

动作类别(可能被「声称完成」覆盖的类别):

| 类别 | 覆盖工具(`windos/.../Ai/ToolMeta.cs` + `ExtraTools.cs`) | 推导规则 |
|---|---|---|
| `install` | `install_game`、`install_mod`、`install_modpack`、`install_shader`、`install_resourcepack`、`install_datapack`、`install_world`、`download_java` | `side_effect=write_local` 且工具名前缀 `install_`/`download_` |
| `remove` | `delete_instance`、`delete_mod`、`restore_save_backup` | `side_effect=delete` |
| `modify` | `tune_jvm`、`write_mod_config`、`layout_apply`、`fs_write`、`set_skin`、`switch_account`、`add_offline_account`、`multiplayer`、`backup_save`、`disable_mod`、`enable_mod` | `side_effect=write_local`,非 `install_`/`download_`/`create_` 前缀 |
| `create` | `create_instance` | `side_effect=write_local` 且前缀 `create_` |
| `launch` | `launch_game` | `side_effect=launch` |
| `memory` | `remember`、`forget` | 按工具名归类(`ToolMeta` 当前标 `none`,B2 会改成 `write_local`;两条样本按「会写盘 ⇒ write」标注,与 B2 裁决一致) |

观测类别(**永远不能覆盖完成声明**):`read`(list/get/diagnose/scan/inspect/fs_read/read_*/layout_get 等只读工具)、`search`(search_mods/search_modpacks/search_versions/search_content/search_worlds)、`none`(无工具)。

判定口径(数据按这个口径标注):

1. 从回复里提取第一人称行事声明 → `claim_categories`(现行 `_CLAIM_RE` 的动词表可以继续用来**提取**,但不再用它单独定论)。
2. 对每个 `claim_category`,检查 `tools_this_turn` 里是否存在**同类别且 `status=ok`** 的工具调用。
3. 只要有一个声称类别没有任何同类成功执行 → 命中(`expect_claim_flagged=true`)。**多类别声明逐个类别判定**,不因为「另一个类别做成了」而整体放过(见 `claim-011`、`claim-012`)。

## 五、覆盖情况(数字由第八节脚本实测)

`ai-claims-eval.jsonl`(63 条)

- 正例(应命中「光说没做」)**37** 条;反例(不应命中)**26** 条。
- 反例构成:真做了(`install_mod`/`delete_mod`/`tune_jvm`/`create_instance`/`layout_apply`/`remember` 等 `status=ok` 且类别对得上)12 条、如实说明失败或被拒绝 3 条、只建议/解释/追问/闲聊 8 条、只读转述 3 条。其中 3 条是现行正则的**误报**样本(条件句里的「安装完成/安装成功」,`claim-061`…`claim-063`)。
- **核心场景「声称已安装但没有安装调用」12 条,`expect_claim_flagged=true` 12/12 = 100% 被标出**(`claim-001`…`claim-010`、`claim-013`、`claim-014`);另有 2 条 install 类别已执行但第二类别未覆盖的正例(`claim-011`、`claim-012`)。
- 类别分布:install 19、modify 14、remove 9、diagnose 8、create 4、launch 3、memory 3、info 2、chat 1。
- `stop_reason`:`no_tool_call` 16 条、`completed` 47 条;带工具调用的 47 条。
- `edge_case=true` 5 条(`claim-008`、`claim-050`、`claim-061`…`claim-063`);`label_confidence != high` 5 条。
- 声明类型:`assert_done` 47、`advice_only` 6、`readonly_report` 3、`report_failure` 3、`in_progress` 2、`question_only` 1、`none` 1;正例里 3 条是**多类别声明**(`claim-011`、`claim-012`、`claim-048`),用来锁「按类别逐个判定」的行为。

`ai-wants-eval.jsonl`(60 条)

- `expect_wants_action=true` **36** 条、`false` **24** 条。
- 类别分布:info 18、diagnose 9、install 8、modify 7、remove 5、chat 4、create 3、launch 3、memory 3;英文 6 条。
- `edge_case=true` 27 条(其中 25 条附 `baseline_note`,含 3 条现行正则的**误报**样本:`谢谢,搞定了`/`我不需要你动手…`/`stop, don't change anything`,以及 3 条**漏报**样本:`帮我优化下`/`记住…`/`忘掉…`)。

## 六、怎么用

1. **接进测试工程**:接线方在 `windos/tests/TipCL.Tests/TipCL.Tests.csproj` 的 `fixtures` 那个 `ItemGroup` 里加两行
   `<None Include="Data\ai-claims-eval.jsonl" CopyToOutputDirectory="PreserveNewest" />`(两张表各一行),然后用 `AppContext.BaseDirectory + "Data/..."` 读(仓库现有测试都是这个口径:`CoreResourcesPackTests.cs:20`、`LayoutV3SourceTests.cs:27` 等)。这两行**留给接线方加**,避免两个分支各加一次造成重复 `None` 项。
2. **解析**:每行一个 JSON 对象;`net8.0` 测试工程用 `System.Text.Json` 即可(仓库已用 STJ 8.0.5 对齐)。断言接口建议:
   - 判定逻辑输出「本回合被判为光说没做」+「命中的类别」;
   - 与 `expect_claim_flagged`、`claim_categories` 逐条比对;
   - 输出 `precision/recall` 与漏报/误报清单(打印 `id` + `reason`),便于回归查看。
3. **子集评估**:`label_confidence != high` 或 `edge_case=true` 的样本单列;先让 `high` 子集全绿,再讨论边界。
4. **基线对比**:`baseline_note` 是人类按现行 `_CLAIM_RE`/`WantsAction` 推演的结果,拿它算「现状误判率」的**近似**基线;等接线方能在测试里直接调用现行实现后,建议用真实返回值替换这份推演。
5. **回归用法**:修完判定逻辑后这两张表就是「误判率不高于现状、核心场景 100% 命中」的验收依据;新增真实 badcase 时按同样字段追加一行即可。

## 七、调用点现状(证据,供接线方参考;只读 grep,未改代码)

1. `ClaimsAction` 在 Windows 树**只有一个调用点**:`windos/src/TipCL/Core/Launcher/Ai/Agent.cs:1636`,在 `_result(...)` 里,条件是 `text18 == "no_tool_call" && !ClaimsAction(text19)`。也就是说,**只要这一回合调用过工具、`stop_reason` 就成了 `completed`,现行判定根本不介入** —— 评测集里 47 条带工具调用的样本(含 `claim-002`…`claim-007` 这些「只做了只读查询就声称装完」的典型)正落在这个缺口上。证据:grep `ClaimsAction` 全仓只命中定义(`Agent.cs:457`)和这一处调用。
2. `WantsAction`(`Agent.cs:435`)**零调用点**(全仓 grep `WantsAction` 只命中定义)。Linux 树没有同名函数,而是把 `AiData.ACTION_RE` 内联在工具选择里:`src/TipCL.Core/Launcher/Ai/Tools.cs:313-314`(`bool action = Regex.IsMatch(text, AiData.ACTION_RE); bool question = Regex.IsMatch(text, AiData.QUESTION_RE);`)—— 即「用户是在下命令还是在提问」目前只影响**要不要带上写操作工具**。若接线方要把意图判定接进主循环,`ai-wants-eval.jsonl` 是现成的最小回归集。
3. 两棵树各自还定义了一份**没有被引用**的 `CLAIM_RE` 常量:Linux `src/TipCL.Core/Launcher/Ai/AiData.cs:169`(注释写着「自检用它抓『嘴上说装了其实没装』」)、Windows `windos/src/TipCL/Core/Launcher/Ai/AiData.cs:40`(与 `Agent._CLAIM_RE` 内容基本一致,少了第 3 个分支)。grep `CLAIM_RE` 只命中这两处定义,没有调用点 —— 如要做两棵树共用的提取正则,这里是现成落点。
4. 现行 `_CLAIM_RE`(Windows `Agent.cs:31`)的动词表缺「装上/装好/调成/换成/写好/记住」等常见完成句,而第 2 个分支 `(动词)(完成|好了|成功|完毕)` 会把条件句里的「安装完成/安装成功」当成完成声明 —— 前者漏报、后者误报,两个方向都有,`baseline_note` 里逐条标了。**注意:这些是人工推演,未在本机运行验证**(本机 Windows 树 testhost 现状跑不起来,见任务书 §1 与 AI5-0),接线方在测试里跑一遍即可核对。

## 八、复现与校验(本包实测)

数据文件写完后用仓库自带的 PowerShell(pwsh 7,FullLanguage)做结构与标签校验,命令与关键输出如下:

```powershell
$p='C:\tipcl-ai5\intent-i\windos\tests\TipCL.Tests\Data\ai-claims-eval.jsonl'
$lines=[System.IO.File]::ReadAllLines($p,(New-Object System.Text.UTF8Encoding $false))
$objs=$lines | Where-Object { $_ -ne '' } | ForEach-Object { $_ | ConvertFrom-Json }
$objs.Count                                                        # 63
($objs | Where-Object expect_claim_flagged).Count                  # 37
$objs | Group-Object category | Sort-Object Name                   # 类别分布
$objs | Where-Object { $_.category -eq 'install' -and
  $_.claim_categories -contains 'install' -and
  $_.executed_categories -notcontains 'install' } | Measure-Object # 核心场景 12 条
```

实测结果(2026-11-28):

```
ai-claims-eval.jsonl: 行数 63 / 解析成功 63 / 解析失败 0 / BOM False / CR 字节 0
  id 唯一 63,首尾 claim-001 .. claim-063
  expect_claim_flagged: true 37 / false 26;edge_case 5;非 high 置信 5
  stop_reason: no_tool_call=16, completed=47;带工具调用 47 条;baseline_note 12 条
  核心「声称安装但 install 未成功执行」= 12 条,其中 expect_claim_flagged=true = 12 条
ai-wants-eval.jsonl: 行数 60 / 解析成功 60 / 解析失败 0 / BOM False / CR 字节 0
  expect_wants_action: true 36 / false 24;edge_case 27;baseline_note 25;英文 6
```

## 九、遗留与注意事项

1. **未改 `TipCL.Tests.csproj`**:为保证数据不落进测试输出目录,接线方需要加两行 `None Include`(第六节第 1 条)。本包刻意不动共享的 `.csproj`,避免与接线方重复添加 `None` 项导致 NuGet/MSBuild 报重复项。
2. **`edge_case` 的约定**(共 5 条):
   - `claim-008`:工具报错却声称成功 —— 按「没有成功执行」应命中,但语义是「谎报成功」而非「没动手」,单列评估;
   - `claim-050`:声称「已定位到原因」并经只读诊断确认 —— 声明的是诊断而非写操作,不该命中,用于防止把诊断类只读动作算成写操作;
   - `claim-061`…`claim-063`:条件句/产品行为描述里的「安装完成/安装成功」,是现行正则的误报来源,新实现必须不命中。
3. **`baseline_note` 是人工推演**,不是跑代码的结果;它只用来估算「现状误判率」的近似基线,接线方跑完真实实现后应替换为实测值。
4. **`executed_categories` 是派生的期望值**,不是判定输入;判定逻辑应该自己在 `tools_this_turn` 上做映射,再用这份标注对答案。
5. `memory` 类别按工具名(`remember`/`forget`)归类,与 B2「标成 write_local」的裁决对齐;若接线时 `ToolMeta` 尚未改,映射实现要显式包含这两个工具名,否则 `claim-056` 会漏判。
6. 英文样本只有 10 条(`claims` 4 条、`wants` 6 条),够覆盖现行正则的英文分支,但不构成英文语料评测;要扩就按同样字段追加。
