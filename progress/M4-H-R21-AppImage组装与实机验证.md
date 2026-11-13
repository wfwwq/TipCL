# M4-H R21:AppImage 组装与实机验证

状态:流程已在真机打通(2026-11-13,干跑用的是当时的产物);最终产物出来后再跑一遍并归档

## 为什么现在才做

`tools/package.sh`(M3-C2 写的)要求**在 Linux 上跑**,而它的第一步是 `dotnet publish` —— 盒子(Ubuntu 24.04.5,user,免密 sudo)**没有 dotnet**,所以这条路在盒子上根本走不通;本地能 publish,但本地不是 Linux,生不出 Linux AppImage。结果:`dist/` 里一直只有 `TipCL-linux-x64/` 与 zip,**没有 `.AppImage`**(脚本里那段 `linuxdeploy` 分支从来没被走到过)。

## 打通后的流程(可复现)

**前置条件(两条都是本轮踩出来的):**

1. 发布仍在本地/CI 做:`tools/package.ps1 -SkipWin` → `dist/TipCL-linux-x64/TipCL.App`(自包含单文件),再把产物拷到盒子;
2. 盒子上:**`file` 包必须存在**(`sudo apt-get install -y file`)。`appimagetool` 依赖它做 MIME 识别,缺了会以 `ERROR: file command is missing but required` 直接失败(linuxdeploy 自己会先跑完 desktop/icon 部署,只有最后一步挂);
3. 运行侧:盒子有 `/dev/fuse`,但在这个环境里直接执行 AppImage 仍不可靠 —— 已验证可用的方式是 `APPIMAGE_EXTRACT_AND_RUN=1 <appimage> <args>`(解包到临时目录再执行,不需要 FUSE)。

**组装步骤(纯 bash,在盒子上做):**

```bash
# 1. AppDir 布局(与 tools/package.sh 里那段注释一致)
mkdir -p TipCL.AppDir/usr/bin TipCL.AppDir/usr/share/applications TipCL.AppDir/usr/share/icons/hicolor/256x256/apps
cp TipCL.AppDir.bin  TipCL.AppDir/usr/bin/TipCL            # ← 本地发布出来的单文件
cp tools/AppImage/tipcl.desktop TipCL.AppDir/tipcl.desktop
cp tools/AppImage/tipcl.png     TipCL.AppDir/tipcl.png
cp tools/AppImage/tipcl.desktop TipCL.AppDir/usr/share/applications/tipcl.desktop
cp tools/AppImage/tipcl.png     TipCL.AppDir/usr/share/icons/hicolor/256x256/apps/tipcl.png
printf '%s\n' '#!/usr/bin/env bash' 'HERE="$(cd "$(dirname "$(readlink -f "${0}")")" && pwd)"' 'exec "$HERE/usr/bin/TipCL" "$@"' > TipCL.AppDir/AppRun
chmod +x TipCL.AppDir/AppRun TipCL.AppDir/usr/bin/TipCL

# 2. linuxdeploy(先用 APPIMAGE_EXTRACT_AND_RUN=1 跑它自己,免 FUSE)
curl -fSL -o linuxdeploy.AppImage https://github.com/linuxdeploy/linuxdeploy/releases/download/continuous/linuxdeploy-x86_64.AppImage
chmod +x linuxdeploy.AppImage
APPIMAGE_EXTRACT_AND_RUN=1 ./linuxdeploy.AppImage --appdir TipCL.AppDir \
    --desktop-file=TipCL.AppDir/tipcl.desktop --icon-file=TipCL.AppDir/tipcl.png --output appimage
# 产物:TipCL-x86_64.AppImage(名字取自 desktop 的 Name 字段)
```

## 干跑结果(2026-11-13,产物 = 当时的 `tipcl14`,44,966,962 B)

- `linuxdeploy` 全流程成功:`-- Deploying desktop files -- / -- Copying files into AppDir -- / Running output plugin: appimage / **Success**`;
- **`TipCL-x86_64.AppImage` = 38,959,608 B**(38.9 MB;比单文件产物小,因为 AppImage 自己压缩 squashfs);
- 真机执行:`TIPCL_HOME=~/tipcl/smoke19 APPIMAGE_EXTRACT_AND_RUN=1 ./TipCL-x86_64.AppImage --smoke` → **exit 0**,生成 `smoke.png` 60,101 B,应用日志 `=== TipCL 0.1.0-m3 启动 pid=87161 os=Unix 6.8.0.142 ===` + `home=/home/user/tipcl/smoke19 cwd=/home/user/tipcl/appimg-run`,**0 WARN**;
- 证据:`_cmp/box-appimage-out.txt`(AppDir 组装)、`_cmp/box-appimage2-out.txt`(装 `file` 后出包 + 冒烟)。

## 还差什么(最终收尾时做)

1. 用**最终产物**重跑一遍(本轮之后 R15~R20 还会改代码),把 `dist/TipCL-linux-x64.AppImage` 归档出来;
2. ~~补一次**不带 `APPIMAGE_EXTRACT_AND_RUN`** 的裸跑~~ —— **已补测**:直接执行 `./TipCL-x86_64.AppImage --smoke`(带 FUSE)**exit 0**、出 `smoke.png` 568,431 B(launch 页)、应用日志正常 ⇒ **这台盒子的 FUSE 能挂,裸跑可用**;`APPIMAGE_EXTRACT_AND_RUN=1` 只是无 FUSE 环境下的后备。另测 `gio launch <appimage>` 失败(`Unable to load: Key file contains line "ELF…"`)—— 属预期:AppImage 是「按可执行文件直接跑」的形态,不是 `.desktop` 项,别用 `gio launch` 描述它的双击路径;
3. 确认 `tools/package.sh` 的注释与本文档一致(两点前置条件:`file` 包、FUSE/extract-and-run),必要时把 `file` 缺失与 FUSE 这两条写进脚本的提示文案;
4. 双击可用性:AppImage 需要 `chmod +x` + 桌面文件里 `Exec` 指向 AppImage 才行,这部分与已归档的 `.desktop` 快捷方式链(D-23)不是同一件事,文档里要写清各自覆盖的路径。

## R21b 收尾:最终产物 + 真机端到端(2026-11-13;主 agent 收口,移植代理修 P0,主 agent 复核)

上面「还差什么」四条,本轮逐条落地:

| # | 上面写的待办 | 本轮结果 |
|---|---|---|
| 1 | 用**最终产物**重跑并归档 | 本地 `tools/package.ps1 -SkipWin` 发布 → 盒子上组装 `TipCL-x86_64.AppImage` = **38,967,800 B**,sha256 **`1176a917607d8b16fca60c600a09c71a5c2a7d74957ea04a430e27b38ca1ee8e`**;rsh 分片拉回本地 `dist/TipCL-linux-x64.AppImage`(哈希一致;6 MB 分片会 `h2 protocol error`,2 MB 可用) |
| 2 | 裸跑(不带 `APPIMAGE_EXTRACT_AND_RUN`) | 已补测:直接执行 `./TipCL-x86_64.AppImage --smoke` → **exit 0**、`smoke.png` **574,076 B**、应用日志 **0 WARN / 0 ERROR** ⇒ 这台盒子的 FUSE 能挂,裸跑可用 |
| 3 | `tools/package.sh` 注释与本文档一致、两条前置条件写进脚本提示 | 已改:头部写明 **`file` 包**与 **FUSE / `APPIMAGE_EXTRACT_AND_RUN=1`** 两条前置;新增 **`--appimage-only`**(跳过 `dotnet publish`,直接用已有的 `dist/TipCL-linux-x64/TipCL.App` 组装 —— 正是本轮「本机发布、盒子组装」的分工);linuxdeploy 检测改成「先直接跑,失败再 extract-and-run」,并补了相对路径转绝对、缺 `file` 时告警。**改完后在盒子上真跑通了**(见下),而这一跑当场揪出这条分支**从来没对过**:`--desktop-file` / `--icon-file` 只写了 `tipcl.desktop` / `tipcl.png`,可脚本是 `cd dist` 之后调 linuxdeploy 的,那两个文件在 AppDir 里 —— linuxdeploy 报 `ERROR: No such file or directory: tipcl.png` 直接失败。改成 AppDir 内路径后才出包成功 |
| 4 | 双击可用性:写清各覆盖哪条路径 | 见下「三种形态」 |

**真机端到端(隔离 HOME,最终 AppImage)**:同意门弹出 → 点「同意」关窗并写 `feedback_consent: true` → 主窗口正常(chips `Java 自动` / `4 GB`、新闻行「Minecraft 26.4 Snapshot 8」)→ 齿轮开「启动配置」→ 展开「更多设置」→ 在浮层里把分辨率改成 1600 × 900 → `default_resolution` 逐次变化 → 复原 854 × 480;全程 **0 unhandled**、进程存活。**同一轮真机先撞出两条只在真机暴露的 P0 并修掉**:对话框点按钮不关窗(PARITY §9.7 / D-51)、浮层里除文字外的控件全画不出来(PARITY §9.8 / D-52);两条都是「移植时把平台代劳的一步弄丢了」,真机验收的价值就在这里 —— 无头结构级检查当初是绿的。

**`tools/package.sh` 这次改动怎么验的(如实)**:

- `bash -n tools/package.sh` 通过(Git Bash 与盒子上各一次);
- 新增的 `--appimage-only` 分支在本地隔离目录里用**假 linuxdeploy**(`FAKE_FAIL_DIRECT=1` 时先失败,模拟无 `/dev/fuse`)跑通四条路径:直接成功 / 直接失败→extract-and-run 成功 / 缺单文件→明确报错 exit 1 / 坏参数→报错 exit 1(驱动脚本 `_cmp/pkgtest/run.sh`,产物落在 `_cmp/pkgtest/dist`);
- **盒子上真实组装一次**(盒子上有 `file`、有 `/dev/fuse`、有 `linuxdeploy.AppImage`,只是没有 dotnet —— 正好是 `--appimage-only` 的用武之地):`LINUXDEPLOY=<盒上 linuxdeploy> bash tools/package.sh --appimage-only` → **exit 0**、`linuxdeploy` 末尾 `Success`、产物 `dist/TipCL-linux-x64.AppImage` = **38,967,800 B**(与归档件**同尺寸**,sha256 不同属预期:AppImage 的 squashfs 会写文件 mtime,`fba9fd0f…` vs 归档的 `1176a917…`);再直接裸跑 FUSE 冒烟:`--smoke` **exit 0**、`smoke.png` 574,566 B、应用日志 **0 WARN / 0 ERROR**;
- 也就是说:**「本机发布、盒子组装」这条分工现在是一条可复现的脚本命令**,不再是一段手工敲的说明。踩到的那个坑(`--icon-file` 相对路径)已记进 DECISIONS D-55。

**三种形态与「双击」的关系(第 4 条,三者不等价,分开写)**:

1. `dist/TipCL-linux-x64/TipCL.App`(self-contained 单文件):`chmod +x` 后命令行直接跑;
2. `dist/TipCL-linux-x64.AppImage`(AppImage):**`chmod +x` 后双击与命令行都行**;没有 `/dev/fuse` 的环境要前缀 `APPIMAGE_EXTRACT_AND_RUN=1`。同一条产物**不能**用 `gio launch <appimage>` 打开(`Unable to load: Key file contains line "ELF..."`)—— 它不是 `.desktop` 文件;
3. `.desktop` 快捷方式链(D-23):往应用菜单里加一条 `Exec=` 指向 1 或 2 —— 与第 2 条不是同一件事。

**浮层关闭行为(顺带补测,三条都有证据)**:`×` 关、点外面 light-dismiss 关、**按 `Esc` 不关**(4 次独立按键 + 正向对照证明键送得到:往浮层输入框打字有字形变化;源码侧 `MakePopup` 只设了 `IsLightDismissEnabled`)。取证纪律见 `_cmp/append-r21b-docs` 落进 PROGRESS 的那两条:必须先证「浮层开着」再下结论;`xdotool getwindowfocus --shell` 本机不支持(会打出「指针下的窗口」冒充焦点);**PNG 的 md5 不能当画面比对**(`import` 写时间戳)。

**另一条与本文档相关、也属本轮收尾的新坑(工具侧,详见 DECISIONS D-53)**:`tools/package.ps1` 是**带 BOM 的 UTF-8**,而 BOM 正是它在 Windows PowerShell 5.1 下能按 UTF-8 解码的前提;收尾时用编辑工具改了它,工具**静默丢掉 BOM**,同一份脚本立刻从 0 个语法错误变成 18 个。最后改成**追加式**写入,并回头逐个核对了本轮所有改动文件(3 个源码 + 1 个测试 + 1 个脚本 + 3 个文档)的 BOM 状态,确认一个都没掉。
