# R32 独立验收 · R32-CIFIX(ubuntu CI 唯一红的修复)

> **落盘说明(lead)**:本报告由独立验收人(只读子代理,未参与修复)完成全部取证;其无写权限,故由 lead 转录落盘,保留其全部判词、证据摘录、纠正与"未做到"。
> 验收人自述:**未运行任何测试/构建,未修改任何文件,未执行 git 写操作,未 push/fetch**,只用 `git` / `grep` / `sed` / `python -I` / `curl` 读。

## verdict

**PASS(带注记)** —— 核心主张全部成立:根因判断正确、修复实现与声称一致、新增用例正确且落在批 2、
CI run 276 的 5 个 job 全 success 且 main HEAD = `9f2ca01`、改动范围只有 2 个文件、本地树与远端树逐字节等价。
注记集中在三处**方法论/表述**问题:①`9f2ca01` 在本地根本不存在("都可 `git rev-parse`" 是错的);
②本机复现数字无法独立验证(且当时留存的批 2 日志是失败中止记录);③被验方把期望串的 **10 个 `-`** 记成了 5 个。

## 逐条核对

### 主张 1(根因)— PASS

```
$ git show 7aff853:src/TipCL.Core/Layout/PageLayoutIO.cs
    char[] invalid = Path.GetInvalidFileNameChars();
    ...
    sb.Append((Array.IndexOf(invalid, c) >= 0) ? '-' : c);

$ git show b98de43 -- src/TipCL.Core/Layout/PageLayoutIO.cs
-       char[] invalid = Path.GetInvalidFileNameChars();
+       sb.Append(IsInvalidFileNameChar(c) ? '-' : c);
+   private static bool IsInvalidFileNameChar(char c)
+   {
+       if (c < ' ') { return true; }          // 0x00–0x1F
+       switch (c) { case '"': case '<': case '>': case '|': case ':':
+                    case '*': case '?': case '\\': case '/': return true; ... }
```

Windows 上旧集合 = 0x00–0x1F(32 个)+ `" < > | : * ? \ /`(9 个)= 41 个;新实现覆盖同样 41 个
⇒ **新旧集合在 Windows 上相等**(不只是子集)。`':'` / `'/'` / `'\\'` / 控制符均在;Unix 上旧集合只有 `\0` 和 `/`,新集合向 Windows 对齐。

### 主张 2(守卫用例)— PASS(但"5 个 '-'"是错的)

```csharp
public void ExportSuggestedFileName_IsPlatformIndependent()
...
    PageLayoutIO.ExportToFileDialog("a:b*c?d\"e<f>g|h\\i/j\u0001k");
    Assert.Equal("a-b-c-d-e-f-g-h-i-j-k.json", suggested);
```

字面量解码后正好是 `a : b * c ? d " e < f > g | h \ i / j U+0001 k`;期望串里实际有 **10 个** `-`
(11 个字母 + 10 个分隔符),每个 `-` 精确对应一个非法字符。
旧实现在 Unix 上只会把 `/` 换成 `-`,实际值 `a:b*c?d"e<f>g|h\i-j<U+0001>k.json` ⇒ **Unix 必红**;Windows 上旧集合已覆盖全部输入 ⇒ 旧实现也绿。用例设计成立。

### 主张 3(本机复现数字)— PASS-带注记(只能核"自洽")

- CI 修复前批 1 `Passed 346 / Total 346`;批 2 `Failed 1 / Passed 402 / Skipped 1 / Total 404`。
- 修复后:原失败转通过(402+1)+ 1 条新用例 ⇒ 批 2 = 404 通过 + 1 跳过 = 405;批 1 不变 346。**自洽**。
- **批归属**:该文件没有任何 `namespace` 声明(`Layout/` 下 8 个测试文件全为全局命名空间)⇒
  FullyQualifiedName 为 `PageLayoutIOV2Tests.ExportSuggestedFileName_IsPlatformIndependent`,不含
  `TipCL.Tests.App` ⇒ 被 `!~TipCL.Tests.App` 选中、不被 `~TipCL.Tests.App` 选中。**确实落在批 2**。
- 注记:不能跑测试 ⇒ 「404 / 346 / 0 警告」未被独立验证;
  `.ci-check/batch2-win.log` 实际是一次**失败中止**记录(`hostfxr.dll could not be found`,测试运行已中止);
  CI 的 Release 构建**没有** `-warnaserror`,所以「CI 绿」推不出「0 警告」。

### 主张 4(run 276 / 5 job 全 success)— PASS

```
44118534477 | ci | head 9f2ca012ceb7820e68abc24b79a149a5caa1680e | completed success | run 276
refs/heads/main -> commit 9f2ca012ceb7820e68abc24b79a149a5caa1680e
jobs(5): package(linux) success / test(windows) success / package(win) success /
         test(ubuntu) success / verify(linux) success
```

ubuntu test job 的 6 个步骤(含批 1、批 2、Release build)全部 success。旁证:run 275 的 ubuntu 失败步骤
确为「dotnet test (batch 2/2 — 其余全部)」,注解 `Process completed with exit code 1.`。

### 主张 5(只改 2 个文件、未碰红线)— PASS-带注记

```
$ git diff --name-status 7aff853 b98de43
M src/TipCL.Core/Layout/PageLayoutIO.cs
M tests/TipCL.Tests/Layout/PageLayoutIOV2Tests.cs
```

GitHub API 的 9f2ca01 文件列表同样只有这 2 个文件(+32/−3、+31/−0)。对 `windos/`、`update*.json`、
`LICENSE`、`.github/`、版本号的过滤均为 none。
注记:`git show --stat 9f2ca01` **本地跑不通** —— `fatal: ambiguous argument '9f2ca01': unknown revision`。

### 主张 6(工作区干净)— PASS

`git status --short` 只有 `?? .ci-check/`;`--ignored` 下另有各项目 `bin/` `obj/`,属被忽略项。

## 独立发现

### (a) 全仓其他 `Path.GetInvalidFileNameChars()`

1. `src/TipCL.Core/Plugins/PluginInstaller.cs:291` — `Safe()` 用它**删除**字符,调用点 `:67`
   拼 `id + "-" + Safe(version) + ".zip"`。跨平台共享 Core 代码,若 version 含 `: * ?` 等,
   Windows 删 / Linux 留 ⇒ 同类漂移的**潜在**隐患;但只影响插件下载临时 zip 名,
   **不经过 PageLayoutIO,与 Layout v2 导出/导入无关**。
2. `windos/src/TipCL/DevShot.cs:368`、3. `windos/src/TipCL/Smoke.cs:675` — Windows 专属(`net48`),
   `ci.yml` 对 `windos/**` paths-ignore,无跨平台场景,本次未动。
另 grep `GetInvalidPathChars` 零命中;`DirectorySeparatorChar` / `PathSeparator` 的命中都是合法用法。

### (b) 树哈希确实相等

```
$ git rev-parse b98de43^{tree}
ae8b798f6be7c08be0cca76cb5930b5e440ff361
GitHub API commits/9f2ca01 -> tree.sha = ae8b798f6be7c08be0cca76cb5930b5e440ff361, parent = 7aff853
```

⇒ 两棵树完全相等,"本地不必强推"的内容依据成立。**但**这个相等是「本地 rev-parse」对「API 读 9f2ca01 的 tree」
比出来的,不是本地双 rev-parse(9f2ca01 不在本地库)。

### 其他

- `.github/workflows/ci.yml` 注释仍写「两批合起来覆盖全量: 239(App)+200(其余)+1 跳过 = 440」,
  与实际 346 + 404(总 751)严重不符 —— 过时注释,不影响功能但会误导后人。
- run 276 的 job 日志匿名下载 403(`Must have admin rights to Repository.`)⇒ 修复后 CI 的用例级计数
  无法从公开 API 独立读取,只能确认步骤 success。

## 未做到

- 未运行任何测试/构建,未改文件,未执行 git 写操作,未 push/fetch。
- 无法独立验证「批 1 346 / 批 2 404 通过 + 1 跳过 / Release 0 警告」,只能验证其与 CI 数字自洽。
- 无法读取 CI job 日志原文(403)。
- Windows/Unix `GetInvalidFileNameChars()` 的精确集合是按 .NET 已知实现推理,未在本机实测打印。

---

## lead 的处置(逐条)

| 验收人指出 | 处置 |
|---|---|
| 期望串 10 个 `-`,被验方写 5 个 | **接受,已改**;`R32-CIFIX.md` §3 写 10 个 |
| 「b98de43 与 9f2ca01 都能 rev-parse」是错的 | **接受,已改**;§5 加了更正括注 |
| 批 2 留存日志是失败中止记录 | **接受**;已补跑,现存 `.ci-check/batch1-ok.log`(346/346,`EXIT=0`)、`batch2-ok.log`(404 通过+1 跳过/405,`EXIT=0`) |
| `ci.yml` 注释 239+200+1=440 过时 | **接受,已改**为「2026-11-18 复算:346(App)+404(其余)+1 跳过 = 751」,历史口径保留并标注 |
| CI 无 `-warnaserror`,绿推不出 0 警告 | **接受**;报告里 0 警告明确标为本机 `-t:Rebuild` 实测 |
| `PluginInstaller.cs:291` 同类漂移 | **登记为遗留**,本次不动(不在 Layout v2 链路上) |
| 9f2ca01 不在本地库 | 如实登记;待 `github.com:443` 恢复后 `fetch` + `reset --hard origin/main` 对齐 |

---

## 补记(验收之后的第二笔,lead 自记)

验收回执之后又落了一笔:**`8133d53`**(父 `9f2ca01`),内容是 `R32-CIFIX.md` + 本文件 + `ci.yml` 那条
过时注释的更正。该笔经同一 Git Data API 落地,`tree = 75ca3427…` 与本地 `b1359df` 相同。
run 277(`8133d53`)**5 个 job 全 success**。

本补记**未经独立验收**(它是验收后的增量,且只含文档与一行注释);下一次验收如需要,应按
`b98de43..b1359df` 的差量单独核。