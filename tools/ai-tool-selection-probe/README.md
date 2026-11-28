# ai-tool-selection-probe(AI5-C3 回放探针)

量一件事:**这一轮「该有的工具」有没有被 `Tools.SelectToolSchemas` 下发。**
没下发 → 模型要么改用别的办法,要么调一个本轮没声明的工具 → 触发 C2 的
`tool_schema_miss` 全量重发(白烧一轮 + 一次完整上下文)。

它是 AI5-C3 数据报告(`progress/AI5-C3.md`)的**可复核工具**:报告里每个数字都能在这里跑出来。

## 怎么跑

```powershell
# 注意:本机 DOTNET_ROOT 的用户级变量在 DSH 会话里可能还是旧值,先覆盖再跑
$env:DOTNET_ROOT='C:\Program Files\dotnet'

cd tools/ai-tool-selection-probe
dotnet run -c Release -m:1 -p:UseSharedCompilation=false            # 只打印数据
dotnet run -c Release -m:1 -p:UseSharedCompilation=false -- --check # 顺便当回归闸门(退出码 1 = 不达标)
```

要一条命令看关键结论:

```powershell
dotnet run -c Release -- --check | Select-String -Pattern "###|漏选请求|最常漏选|=== 基线|PASS|FAIL|方案"
```

## 输出四段

1. **逐条**:每轮 `ideal` 数、漏选(现状)、漏选(只看最后一条用户消息的老口径)、下发工具数、tools token;
2. **汇总**(每集):漏选请求数 / 每百回合、最常漏选工具、下发规模分布(最小 / 中位 / 最大 / ≥50 的条数)、tools token/轮;
3. **方案对比**:方案 1a(漏选过的工具并入 `CORE_TOOLS`)、1a'(只并入 Top3)、1b(补关键词)、
   方案 2(非 CORE 一行目录 + `find_tools`/`load_tools` 按需加载),以及**多发一轮的往返成本**和
   「裸写写工具」那一支(`ACTION_RE` 命中就把全部写工具带上);
4. **基线对照 + `--check` 闸门**:和记录在案的数字比,漏选不许变多、tools token 不许超基线 ×1.10。

## 四个请求集(标注规则见 `RequestSets.cs` 头注释)

| 集 | 条数 | 用途 | 1b 前 | 1b 后 |
|---|---|---|---|---|
| 典型请求集 `Typical` | 40(有标注 39) | 主要意图覆盖(装模组 / 排错 / 卡顿 / 存档 / 升级 / 造包 / 服务器 / 联机 / 文件…) | 7/39 = **17.9/百回合** | 0/39 = **0** |
| 口语变体集 `Variants` | 12 | 同一批意图换口语说法,量「漏选率对说法有多敏感」 | 5/12 = **41.7/百回合** | 0/12 = **0** |
| 关键词补丁集 `KeywordPatch` | 10 | AI5-C3 方案 1b **针对**的说法(含 SH-1 场景 18a 的「好卡」) | 8/10 | 0/10 |
| 对抗性留出集 `HoldOut` | 10 | **故意**用表外说法,且不参与补词 → 量关键词路线的长尾 | 6/10 | 5/10 = **50/百回合** |

- 合并(典型 + 口语变体):1b 前 **23.5/百回合**(12/51)→ 1b 后 **0**。
- `KeywordPatch` 是「因」不是「果」:补词就是照它写的,所以补完必然 0 漏选,**不能拿它证明真实覆盖率**;
  真实覆盖率的下界要看 `HoldOut`(它证明关键词路线的天花板:换个说法就还会漏)。
- `--check` 只对前三个集设闸门;`HoldOut` 是对抗集,不设闸门。

## 口径(与报告一致)

- **一轮** = 一次用户请求(一次 `SelectToolSchemas`);
- **`ideal`** = 「这一轮没有这个工具,任务就做不完」的工具集合,按 `AiData.SYSTEM_PROMPT` 写明的流程人工标注;
- **漏选** = `ideal` 里没被下发的工具;**漏选率(每百回合)** = 有漏选的请求数 ÷ 有标注的请求数 × 100;
- **token** = 项目自己的估算口径 `Tokens.EstimateText`(CJK 1 / 其余 0.25),对每个 schema 的 JSON 序列化求和,
  报的是 **tools 部分**,不含系统提示与历史。同一口径下:系统提示 2955、`CORE_TOOLS` 18 个 2243、全量 78 个 13770。
  **这是估算,不是计费真值**,只用于方案之间的相对比较;
- **往返成本**:每多发一轮请求都要重发「系统提示 + 全部历史 + tools」,本探针按「系统提示 + 消息」≈ 2982 token/轮计。
  provider 若有 prompt caching,这一项会便宜很多(报告 §4.3 有敏感度分析)。

## 怎么复核「1b 前」的数字

`Before` 常量抄自 commit `d78bd54`(C1 已落地、1b 未落地)的实测。要自己跑一遍基线:

```powershell
git worktree add C:\tmp\ai5c3-base d78bd54
Copy-Item -Recurse tools\ai-tool-selection-probe C:\tmp\ai5c3-base\tools\
cd C:\tmp\ai5c3-base\tools\ai-tool-selection-probe
$env:DOTNET_ROOT='C:\Program Files\dotnet'; dotnet run -c Release
```

（探针自己不读 git,所以基线只会作为常量出现;`--check` 比的是 `After` 常量 = 当前提交的实测。）

## 边界

- **不进产品构建**:`TipCL.Linux.sln` 只登记 `src/TipCL.App` / `src/TipCL.Core` / `tests/TipCL.Tests`,
  CI 也只 build/test 那三个 csproj,打包脚本(`tools/package.sh`、`tools/deb.sh`)只拷发布产物。
  本目录不会进 AppImage / deb / compact。
- **不影响 i18n 基线**:`tools/i18n-audit.py` 只扫 `src/TipCL.App` + `src/TipCL.Core`,本目录里的中文
  字符串不参与统计,所以不需要 `i18n:ignore` 标记。
- 只读:除了 `bin/`、`obj/`(被 `.gitignore` 忽略),它不写任何文件。