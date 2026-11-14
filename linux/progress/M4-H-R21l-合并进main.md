# M4-H R21l:把移植合并进 `main`(作为 `linux/` 子目录)+ 根 workflow(2026-11-14)

用户指令:"加进main"。

---

## 1. 为什么不是"合并两条历史",而是"作为子目录加进去"

`main`(= Windows 版 TipCL 本体,625 个文件)的根目录里,与移植**重名**的路径一大把:
`PROGRESS.md` / `DECISIONS.md` / `GOAL.md` / `README.md` / `docs/` / `progress/` / `src/` / `tests/` / `tools/` /
`.gitignore` / `交接必读.md`。直接 `git merge --allow-unrelated-histories` 会在**每一个重名路径**上冲突,
而且"取哪一边"对两边都是破坏。

**做法**(既满足"加进 main",又不动上游任何文件):

```bash
git switch -c main origin/main                     # 上游的树
git merge --allow-unrelated-histories -s ours --no-commit linux-port
git read-tree --prefix=linux/ -u linux-port        # 把移植整棵树放到 linux/ 前缀下
git commit                                         # 合并提交:两个父(上游 + 移植的 49 个提交)
git push origin HEAD:main                          # fast-forward,无 force
```

**结果核对**(逐项实测,不是"命令成功就算"):

| 项 | 值 |
|---|---|
| 合并提交 | `ac62394`,父 = `803dd385`(上游 main)+ `44e9917`(移植历史)⇒ 两边历史都能从 main 追溯 |
| 根下新增 | 只有 `linux/`(425 个文件,96,522 行) |
| 上游文件 | `README.md` / `LICENSE` / `PROGRESS.md` / `DECISIONS.md` / `src` / `tools` / `.github/workflows/ci.yml` 的 **blob 与 `origin/main` 逐一相同** |
| 上游之外零改动 | `git diff --stat origin/main HEAD -- . ':(exclude)linux'` → **空** |
| 上游 CI 是否被打扰 | main 上 `CI` run **#234**(merge 提交)与 **#235**(随后那次)**都 success** |
| 推送方式 | `803dd38..ac62394` —— **fast-forward,没有用 force** |

---

## 2. 合并之后,移植的 CI 在 main 上是**失效**的 —— 所以补了根 workflow

GitHub **只读仓库根的 `.github/workflows/`**。移植原有的 workflow 在 `linux/.github/workflows/ci.yml`,
在 `linux-port` 分支上有效(那个分支里移植就在根),但合并进 main 之后它位于 `linux/` 子目录 ⇒ **不会被注册**;
而上游根上的 `CI` 只构建/测试 Windows 侧(`src/TipCL/...`),不覆盖移植。

**新增**:`.github/workflows/ci-linux.yml`(仓库根,纯新增文件,不动上游任何文件):

* `defaults.run.working-directory: linux` —— 所有 run 步骤的 cwd 定到移植目录;
* 产物路径加前缀:`linux/dist/...`(上传/校验用),冒烟用 `$GITHUB_WORKSPACE/linux/dist/TipCL-linux-x64.AppImage`;
* `paths` 过滤 `linux/**` 与本文件本身 ⇒ 只在动到移植时跑,不重复消耗;
* 其余与嵌套那份一致:测试**分两批**、Release 构建、`publish → linuxdeploy → AppImage + .deb → zip → hashes →
  xvfb 冒烟 → 上传产物`;win-x64 的冒烟**作提示不作门槛**(失败发 `::warning` 注解)。

**验证**(真跑):`c00a475` 推送后 —— 上游 `CI` **#235 success**;新的 `linux-port CI` **run #1 四个 job 全绿**
(`test (ubuntu-latest)`、`test (windows-latest)`、`package (linux-x64 → AppImage + .deb)`、`package (win-x64 → zip)`),
产物 `tipcl-linux-x64` **152,804,615 B**、`tipcl-win-x64` **81,170,109 B**。

**这份重复是刻意的、必须同步**:`linux/.github/workflows/ci.yml`(linux-port 分支用)与
`.github/workflows/ci-linux.yml`(main 用)是同一套流水线在两个布局下的版本。要消掉它,只能把移植挪到仓库根
(那是另一种布局决策,没做)。

---

## 3. 本地工作目录的布局变了(必须知道)

合并后,原工作目录 `D:\项目\tipcl\linux` 变成了**合并仓库的根**:

```
D:\项目\tipcl\linux\            ← 合并后的仓根:上游 Windows 工程在根
  ├─ src\  tests\  docs\  tools\  PROGRESS.md  README.md ...   (上游)
  ├─ .github\workflows\{ci.yml, release.yml}                    (上游)
  └─ linux\                                                   ← **移植在这里**
      ├─ src\  tests\  tools\  docs\  progress\  PROGRESS.md  DECISIONS.md  ...
      └─ .github\workflows\ci.yml                               (linux-port 分支用)
```
从**干净 clone** 看到的布局是 `TipCL/{...Windows..., linux/{...移植...}}` —— 那个 `linux/linux` 的怪样子只是
"本地目录恰好也叫 linux"造成的错觉,不是仓库的问题。

**随之做的搬迁**:我留在旧根上的开发产物(`_cmp/`、`dist/`、`scratch/`、84 张 `smoke*.png`)在新布局下**不再被忽略**
(上游根 `.gitignore` 只有 `bin/ obj/ .vs/ *.user *.suo *.pdb publish/ _wph_run/ shot-*.png`),
留着就是一颗雷 —— 将来谁 `git add -A` 会把它们提交进上游仓。已全部挪进 `linux/`(`linux/.gitignore` 有对应规则),
搬完 `git status` 在仓根**干净**;顺手也让我的开发脚本恢复了正确的工作根。

**怎么回退**(需要时):`git revert -m 1 ac62394`(只撤掉 `linux/` 的加入),或直接删掉根上的 `ci-linux.yml`;
`linux-port` 分支与所有 tag 都不受影响。

---

## 4. 账

| 项 | 结果 |
|---|---|
| main | `803dd385` → `ac62394`(合并)→ `c00a475`(根 workflow),均 fast-forward |
| linux-port | `44e9917`(不变,保留为移植的独立分支) |
| 上游 | 零改动、CI 两次 success |
| 移植在 main 上的 CI | `linux-port CI` run #1 四 job 全绿,产物已上传 |
| 本地 | 仓根干净;开发产物与忽略规则就位 |

**仍未做 / 未定位**:1 win job 首次失败(`700cd21` 那次)的确切原因 —— 日志下载要管理员权限,读不到,已用"不阻断 + 留注解"覆盖;
2 两条平台级提示(`actions/*@v4` 的 Node 20 弃用、`ubuntu-latest` 2026-10 迁移)未处理;
3 两份 workflow 的同步靠人肉(要消掉得把移植挪到仓库根);
4 要不要把移植并进根目录(而不是 `linux/` 子目录)是布局决策,留给你。
