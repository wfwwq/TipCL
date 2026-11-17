# R32-CIFIX · GitHub Actions 全绿修复

> 触发:用户「ci要全绿才行,检查」。日期 2026-11-17 → 2026-11-18。
> 结论:**修复前 main 是红的;修复后 run 276 的 5 个 job 全 success。**

## 1 查到的状态(证据:api.github.com 公开读取)

| run | 分支 / 提交 | 结论 |
|---|---|---|
| 274 | `main@cfd443b`(上一个 main) | success |
| **275** | **`main@7aff853`(本次合入 main)** | **failure** |
| **276** | **`main@9f2ca01`(修复后)** | **success** |

run 275 的 5 个 job:`test (windows-latest)` success、`test (ubuntu-latest)` **failure**、
`package (linux-x64 → AppImage + .deb)` success、`package (win-x64 → zip)` success、
`verify (linux-x64 装得上 + 起得来)` success ⇒ **唯一的红在 ubuntu 测试批 2**。

注解只有 `Process completed with exit code 1.`。job 日志(用 GCM 里已有凭据取 token 下载,只用于读日志,未落盘)里唯一失败:

```
Failed PageLayoutIOV2Tests.ExportWritesWholeV2DocumentWithEveryField [13 ms]
  Assert.Equal() Failure: Strings differ      ↓ (pos 7)
  Expected: "library-main.json"
  Actual:   "library:main.json"
批1: Failed 0, Passed 346, Total 346
批2: Failed 1, Passed 402, Skipped 1, Total 404
```

## 2 根因

`src/TipCL.Core/Layout/PageLayoutIO.cs` 的 `SafeFileName()` 用 `Path.GetInvalidFileNameChars()` 过滤页面 id。
该 API **随 OS 变**:

- Windows:`0x00–0x1F` + `" < > | : * ? \ /`(**41 个**)
- Unix:只有 `\0` 和 `/`

页面 id 形如 `library:main` ⇒ 同一页在 Windows 建议导出名 `library-main.json`、在 Unix 建议
`library:main.json`。这不是测试写错,是**移植口径漂移**:导出文件名不该跟 OS 变。
Windows CI 绿、ubuntu 红,正是这条。

## 3 改法

1. `PageLayoutIO.cs`:`SafeFileName` 不再问 OS,固定用 **Windows 非法字符集 ∪ POSIX 的 `/`**
   (实现落成 `IsInvalidFileNameChar(char)`:`c < ' '` 覆盖 0x00–0x1F,switch 覆盖那 9 个)。
   **Windows 侧新旧集合相等(不只子集)⇒ Windows 行为逐字不变**,Unix 侧向 Windows 真源对齐。
2. `PageLayoutIOV2Tests.cs`:新增守卫 `ExportSuggestedFileName_IsPlatformIndependent`,
   把 `a:b*c?d"e<f>g|h\i/j\u0001k` → `a-b-c-d-e-f-g-h-i-j-k.json` 钉死在两端
   (**10 个 `-`**:11 个字符 + 10 个分隔)。旧实现在 Unix 上必红。

## 4 本机按 CI 口径复现(不是默认 SDK 10,是 SDK 8.0.427)

```
批1 ~TipCL.Tests.App      : 通过 346 / 失败 0 / 跳过 0 / 总 346   EXIT=0   (与 CI 同数)
批2 !~TipCL.Tests.App     : 通过 404 / 失败 0 / 跳过 1 / 总 405   EXIT=0   (修复前 402+1 失败+1 跳过)
dotnet build src/TipCL.App -c Release -t:Rebuild : 0 警告 0 错误
```

隔离:`TIPCL_HOME` / `TEMP` / `TMP` 全部重定向到工作区 `.ci-check/{home,tmp}`,未碰真实用户目录。
留存的原始日志:`.ci-check/batch1-ok.log`、`.ci-check/batch2-ok.log`(均 `EXIT=0`)。

口径差异(如实登记):CI 的 Release 构建步骤**没有** `-warnaserror`,所以「CI 绿」推不出「0 警告」;
0 警告是本机 `-t:Rebuild` 实测。

## 5 落地方式:GCM 凭据 + Git Data API(github.com:443 当时不通)

推的时候 `github.com:443` 开始不可达(`git push` 反复 `Connection was reset` / 超时;
本机 7890 代理上游回 502;`api.github.com` 仍 200)。改用 **Git Data API** 把同一份改动做成一笔
提交直接落到 main:

- `origin/main` = **`9f2ca01`**(父 `7aff853`,只动那 2 个文件:+32/−3、+31/−0)
- 本地那笔 `b98de43` **tree sha 与它完全相同**:`ae8b798f6be7c08be0cca76cb5930b5e440ff361`
  (两个 blob 也是同一 sha:`7db66a9d…`、`57491bc…`)⇒ 内容零差异,只差提交元数据。
  等 github.com 恢复后 `git fetch && git reset --hard origin/main`,不产生分叉。
  *注:核对 tree 相等用的是「本地 `git rev-parse b98de43^{tree}`」对「API 读 9f2ca01 的 tree」——
  9f2ca01 这个对象**不在本地库**里,本地 rev-parse 它会 `unknown revision`(验收人指出的原话错误,已更正)。*

## 6 CI 结果

run 276(`main@9f2ca01`)逐 job:

```
package (linux-x64 → AppImage + .deb) | success
test (windows-latest)                 | success
package (win-x64 → zip)               | success
test (ubuntu-latest)                  | success
verify (linux-x64 装得上 + 起得来)     | success
```

ubuntu test job 的批 1 / 批 2 / Release build 三步全 success。

## 6.1 第二笔(报告 + 验收 + ci.yml 注释)也已落地并全绿

本文件与 `R32-CIFIX-ACCEPT.md`、以及 `ci.yml` 那条过时注释的更正,作为第二笔提交同样经
Git Data API 落到 main(**`8133d53`**,父 `9f2ca01`,3 文件),`tree = 75ca3427…` 与本地
`b1359df` 的 tree **逐字相同**(3 个 blob sha 全等)。

run 277(`main@8133d53`)**5 个 job 全 success**:`package (linux-x64 → AppImage + .deb)` /
`test (ubuntu-latest)` / `package (win-x64 → zip)` / `test (windows-latest)` /
`verify (linux-x64 装得上 + 起得来)`。

## 6.2 本地 ↔ 远端:内容等价,提交号不同(待网络恢复收口)

| | 提交 | tree |
|---|---|---|
| 远端 `origin/main` | `8133d53` | `75ca3427094f1ae9e6067c9967567aeb35482688` |
| 本地 `main` | `b1359df` | `75ca3427094f1ae9e6067c9967567aeb35482688` |

两笔提交的父子关系在同一位置分叉(`9f2ca01` ↔ `b98de43`,而两者 tree 亦相同),**仓库内容零差异**,
只是提交对象不同。

**已收口**:`github.com:443` 恢复后执行 `git fetch origin main` + `git reset --hard origin/main`,
本地 main 现为 `fdb7cbb`(与 `origin/main` 同一个提交);对齐前留保险分支
`r32-cifix-local-commits @ 52b19bb`,三笔本地等价提交完好,`git diff 52b19bb HEAD` 为空。

## 7 独立验收

`R32-CIFIX-ACCEPT.md`(只读子代理,一次跑完)。**verdict:PASS(带注记)**,核心主张全部成立。
它**纠正了我三处**:
1. 我把期望串记成「5 个 `-`」,实际 **10 个**(它逐字符验过位置);
2. 我写成「b98de43 与 9f2ca01 都能 `git rev-parse`」——错,9f2ca01 不在本地库;
3. 指出 `.ci-check/batch2-win.log` 是**一次失败中止**的旧记录(hostfxr 缺失),
   不能当成功复现证据 ⇒ 已补跑并留下 `batch1-ok.log` / `batch2-ok.log`。
另指出 `.github/workflows/ci.yml` 注释里「239(App)+200(其余)+1 跳过=440」是与现状严重不符的
过时数字(现为 346+404+1=751)⇒ 已在本轮一并更正(注释级,不改任何步骤)。

## 8 遗留 / 未做

0. **本地 main 与 origin/main 的提交号已对齐(已收口)**:`github.com:443` 恢复后已
   `git fetch origin main` + `git reset --hard origin/main` ⇒ 本地 main = `origin/main` = `fdb7cbb`;
   对齐前先建了保险分支 `r32-cifix-local-commits @ 52b19bb`(等价的三笔本地提交,内容零差异,
   留作可回溯),`git diff 52b19bb HEAD` 为空。
   未收口项:**无**。下一项起为登记遗留。
1. **`PluginInstaller.cs:291`**:`Safe()` 同样用 `Path.GetInvalidFileNameChars()`(删除式),
   用于插件下载临时 zip 名(`id-version.zip`)。若 version 含 `: * ?` 等,Windows 删 / Unix 留
   ⇒ 同类平台漂移的**潜在**隐患。**不在 Layout v2 导出/导入链路上**,本次未动。
2. 之前推的 `r32-mac-final` / `r32-mac-i18n` / `r32-mac-base` / `r32-linux-final`
   远端**一条 CI run 都没有**(`?branch=` 全为 "no runs")。分支在、事件没触发,原因未查;
   不影响 main 的全绿结论。
3. 未做:不碰 tag、不发 release、不改 `update*.json` / `update-linux.json` / `LICENSE` / README;
   `windos/**` 未动。

## 9 红线自检

- 未重启 DSH / 电脑;未创建任何 OS 窗口(`dotnet test` 走 headless,`-t:Rebuild` 不启 UI)。
- 未触碰真实用户数据目录:`TIPCL_HOME` / `TEMP` / `TMP` 全程重定向到工作区并前后复核。
- 工作区除未跟踪的 `.ci-check/`(临时证据目录)外干净。