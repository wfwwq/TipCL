# M4-H R21m:布局收敛 —— 移植放到仓库根,Windows 版搬进 `windos/`(2026-11-14)

用户的"来吧"回应的是上一条里那句"**要不要把移植挪到仓库根(而不是 `linux/` 子目录)**"。
这一轮把它做了,并且顺手把上一轮为了绕开"移植在子目录"而多出来的那份根 workflow **消掉了**。

---

## 1. 先查外部引用,再动布局(这一步救了自动更新)

动布局之前先问:**仓库里哪些路径是被外部按固定 URL 引用的?** 查出来一条硬的:

| 引用方 | 位置 | 内容 |
|---|---|---|
| Windows 版 | `windos/src/TipCL/Core/Launcher/Config.cs:12` | `DEFAULT_UPDATE_URL = "https://raw.githubusercontent.com/wfwwq/TipCL/main/update.json"` |
| Windows 版 | `windos/src/TipCL/Core/Launcher/Updater.cs:12` | 同上 |
| Linux 移植 | `src/TipCL.Core/Launcher/Config.cs:13` 与 `Updater.cs:12` | **同一个 URL**(移植当初就是照参考版抄的) |
| README | `windos/README.md:159` | 明确写着"启动器默认读取本仓库的 update.json 检查更新" |

⇒ **`update.json` 必须留在仓库根**:它不属于任何一个子项目,而是"这个仓库的发布通道";挪进子目录会让
**已经装在用户机器上的客户端**更新失效(它们读的是写死的 URL)。本轮核对:`update.json` 留在根,
blob 与上游**逐字节相同**(`808feaf4434adaf7719ec4bd45b4df7f1123f263`)。

同理 `LICENSE` 留根(仓库级),`.github/ISSUE_TEMPLATE/**` 留根(GitHub 只在根读它)。

另一条:上游 README 的 CI 徽章写死了 `actions/workflows/ci.yml` —— 而根上这个文件名现在要归移植,
所以徽章必须一起改(见下)。

---

## 2. 目标布局与实现

```
仓库根 = Linux/Avalonia 移植(当前主线)
  ├─ src/ tests/ tools/ docs/ progress/ PROGRESS.md DECISIONS.md GOAL.md TipCL.Linux.sln 交接必读.md
  ├─ .github/workflows/ci.yml              ← 移植的流水线(路径不变,文档里的引用仍然有效)
  ├─ LICENSE / update.json / README.md     ← 仓库级 / 发布通道 / 新的总说明
  ├─ .github/ISSUE_TEMPLATE/**             ← 仓库级
  └─ windos/                               ← Windows 桌面版整棵树(WPF / .NET Framework 4.8)
       ├─ src/ tests/ tools/ docs/ progress/ samples/ TipCL.sln PROGRESS.md DECISIONS.md GOAL.md 交接必读.md
       └─ README.md                        ← 原根 README 跟着 Windows 工程搬进来
```

实现(纯 `git mv` + 一次 `checkout`,**历史靠 rename 检测延续**,没有重写历史):

```bash
git switch -c layout-port-at-root origin/main
git mv src tests tools docs progress samples TipCL.sln PROGRESS.md DECISIONS.md GOAL.md 交接必读.md .gitignore windos/
git mv .github/workflows/ci.yml      .github/workflows/ci-windows.yml     # 改路径 + 加 paths 过滤
git mv .github/workflows/release.yml .github/workflows/release-windows.yml
git mv README.md windos/README.md
git checkout linux-port -- .          # 移植整棵树落到根(同一路径、内容不变)
git rm -r linux                       # 去掉旧子目录
git rm .github/workflows/ci-linux.yml # 消掉上一轮为子目录布局多加的那份
```

**同时改掉的引用**:
* `ci-windows.yml`:`dotnet build windos/src/TipCL/TipCL.csproj`、`dotnet test windos/tests/TipCL.Tests`;
  触发加 `paths: ["windos/**", ".github/workflows/ci-windows.yml"]`(只在动 Windows 时跑)。
* `release-windows.yml`:`dotnet build windos/src/...`、`cp windos/src/TipCL/bin/Release/net48/TipCL.exe`。
* 移植的 `ci.yml`:加 `paths-ignore: ["windos/**", …]`(只在动移植时跑)。
* `windos/README.md`:徽章 `ci.yml → ci-windows.yml`;`[MIT](LICENSE) → ../LICENSE`、`update.json → ../update.json`
  (它其余的相对引用 `docs/icon.png` / `docs/plugins/DESIGN.md` / `samples/` **跟着文件一起搬进 `windos/` 之后就地有效**)。
* 根上**重写**一份简短 README:说明"根=移植、`windos/`=Windows 版",并写明 `update.json` 为什么必须留在根。

---

## 3. 验证

| 项 | 结果 |
|---|---|
| 三份 workflow 真解析 | 全过;触发条件符合预期(移植:`paths-ignore windos/**`;Windows:`paths windos/**`;release:`on: release: published`) |
| README 相对链接 | 新根 README 的 10 个相对引用、`windos/README.md` 的 5 个,**逐个 `Test-Path` 都解析得到** |
| `windos/` 内 csproj 引用 | `TipCL.Tests.csproj` 的 `..\..\src\TipCL\TipCL.csproj` 相对自己的位置仍然落在 `windos/src/TipCL` ✓;没有跳出 `windos/` 的引用 |
| 文件数 | **1050 = 425(移植)+ 625(上游)**,一个不多一个不少 |
| 移植内容零改动 | `git diff --cached linux-port -- .` 只有**新增**(上游搬进 windos/ 的那些),没有改动/删除移植文件 |
| `update.json` | blob 与上游逐字节相同 ✓ |
| **两条流水线在新布局下真跑** | 推送 `2f7683e` 后:移植的 `ci` **run #237 四个 job 全绿**(`test (ubuntu/windows)`、`package (linux-x64 → AppImage + .deb)`、`package (win-x64 → zip)`),产物 `tipcl-linux-x64` 152,804,629 B / `tipcl-win-x64` 81,170,026 B;Windows 的 `CI` **run #1** 的 `构建` 与 `单元测试` 两步全 success ⇒ **Windows 工程在 `windos/` 里照样构建/测试通过** |

**过程中的两个小插曲(如实记)**:
1 `git rm -r linux` 只删**被跟踪**的文件;我那些未跟踪的开发目录(`_cmp/` `dist/` `scratch/` + 84 张冒烟图)是物理留在 `linux/` 里的,
回头发现后搬回新根(碰巧新根就是移植根,`linux/.gitignore` 也跟着成了根 `.gitignore`,搬迁后仓根**干净**)。
2 推送遇到**网络抖动**(3 次 `Empty reply from server` / `Could not connect to github.com:443`),`git ls-remote` 恢复后重推成功;
期间本地提交已完成,没有丢东西。

**回退**:`git revert 2f7683e`(它是一次普通提交,revert 会把搬运反过来);`linux-port` 分支与所有 tag 不受影响。

---

## 4. 账与遗留

| 项 | 值 |
|---|---|
| main | `4706c69` → `2f7683e`(FF,无 force) |
| 重复的 workflow | **已消掉**(根上只有三份:移植 `ci.yml` + Windows `ci-windows.yml`/`release-windows.yml`) |
| `linux-port` 分支 | 保留(内容 = 现在 main 的根,历史仍是独立一条;要不要删由你定) |
| 仍未做 | 1 win job 首次失败(`700cd21`)的原因未定位;2 两条平台级提示(`actions/*@v4` 的 Node 20 弃用、`ubuntu-latest` 2026-10 迁移)未处理;3 `linux-port` 分支的取舍 |

## 5. 附:推送抖动的那一段(以及绕法)

`2f7683e` 那次推送还算顺(重试 1 次),但紧随其后的文档提交连续失败 **十几次**:
`Empty reply from server` / `Recv failure: Connection was reset` / `Failed to connect to github.com:443 after 21s`。
当时的诊断有意思:

```
github.com:443        reachable = 时通时不通(5 次探测:True/False/False/False/True)
api.github.com:443    reachable = True
raw.githubusercontent.com:443 reachable = True
git ls-remote        也失败(说明不是"推送体积大"的问题)
```

排除掉的:HTTP/2(改成 `-c http.version=HTTP/1.1` 无效)、代理配置(`http.proxy`/`HTTPS_PROXY` 都是空)、凭据(同一套凭据十几分钟前刚推成功过)。

**绕法(有效)**:解析到 `github.com` 的 A 记录后,**把域名钉到那个 IP** 再推:

```bash
# 探一下钉 IP 时 git 服务是否真的响应(公开仓可匿名读)
curl -sS -o /dev/null -w '%{http_code}' --resolve github.com:443:20.205.243.166 \
  'https://github.com/wfwwq/TipCL.git/info/refs?service=git-upload-pack'      # -> 200

# 钉住 IP 推送
git -c http.curloptResolve=github.com:443:20.205.243.166 push origin main
#  2f7683e..469caac  main -> main      ← 一次成功
```

**结论与记法**:这类"TCP 时通时不通、服务空回复"的抖动,优先怀疑**域名解析/路由层面的干扰**;
`http.curloptResolve`(Git 2.30+ 的 `http.curloptResolve`)能把域名钉到已验证可用的 IP,是比"反复重试"更有效的一招。
另外:推送失败**不影响本地**——提交一直在本地,只是等一个网络窗口;所以遇到这种情况先确认"本地提交完整",再慢慢找路。
