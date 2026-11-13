# M4-H R21j:Linux 上的出包链路 + 存档管理 chip 收尾(2026-11-13/14)

接 R21i(在 Linux 上真跑测试)。这一轮把**出包链路**也在 Linux 上真跑了一遍,并把
PARITY §9.4 里最后一条「存档管理其余 chip 没逐点」补完。两处收获都不是"跑一遍确认没事",而是**跑一遍就发现问题**。

---

## 1. `tools/package.sh` 的改名通配:第二次出包会静默留下旧包(真缺陷,已修)

**现象**:在盒子上连着出两次包,第二次之后 `dist/TipCL-linux-x64.AppImage` 的 **mtime/sha256 一动不动**,
而单体二进制 `dist/TipCL-linux-x64/TipCL.App` 已经换了(45,002,711 → 45,002,391 B)。也就是说:
**D-60 那条「改完 `src/` 就重打产物」的规矩,被脚本自己破坏了 —— 产物看着是新的,其实是上一版。**

**根因**(单变量证据见下):

```bash
# 原脚本
mv -f dist/TipCL-*.AppImage dist/TipCL-linux-x64.AppImage 2>/dev/null || true
```

linuxdeploy 的产物名取自 desktop 的 `Name` 字段,实际是 `TipCL-x86_64.AppImage`。第一次出包时 `dist/` 里
只有它一个,通配匹配 1 个 → 改名成功。**第二次出包时 canonical 文件已经存在**,`TipCL-*.AppImage` 同时匹配
`TipCL-x86_64.AppImage`(新)与 `TipCL-linux-x64.AppImage`(旧) → `mv` 多源到**非目录目标**直接失败 →
失败被 `2>/dev/null || true` 吞掉 → 新包留在原地、canonical 名字还是旧包,`ls` 甚至不会少文件。

**单变量证据(盒子实测)**:

| 步骤 | `dist/TipCL-linux-x64.AppImage` | 旁证 |
|---|---|---|
| 旧脚本的第二次出包(16:57) | mtime **16:48**、sha256 `bfaa2649…`(没变) | `dist/TipCL-x86_64.AppImage` **16:57**、38,984,184 B —— 新包没被改名,留在原名下 |
| 修好后第 1 次(17:00:44) | mtime 17:00:44、sha256 `703a0c58…` | 残留的 `TipCL-x86_64.AppImage` 消失 |
| 修好后第 2 次(17:00:45,**原来会失败的那次**) | mtime 17:00:45、sha256 `beb216fc…` | 无残留 |

**修法**:只认「不是 canonical 自己」的那一个文件改名;一个都没找到就**明确报一条警告**(而不是静默通过):

```bash
renamed=""
for f in dist/TipCL-*.AppImage; do
    [ -e "$f" ] || continue
    case "$f" in dist/TipCL-linux-x64.AppImage) continue ;; esac
    mv -f "$f" dist/TipCL-linux-x64.AppImage
    renamed="$f"
    break
done
[ -n "$renamed" ] || echo "警告: 没找到 linuxdeploy 刚产出的 AppImage …,canonical 名字可能还是上一版" >&2
```

**校验新包真的带新代码**:把 canonical AppImage 解开取 `usr/bin/TipCL` 的 sha256,与 `dist/TipCL.AppDir/usr/bin/TipCL`
对齐(`a82ffdee…`)—— 旧包解开是 `e3335b25…`,两者不同,正好说明"解包对比"这一步不能省
(linuxdeploy 会 patch 载荷,所以 AppDir 里的那份与 publish 出来的那份哈希本来就不同,不能直接比)。

> 顺带记一条与 D-60 同源的坑:**AppImage 不是逐字节可复现的**(同样的输入连出两次得到
> `703a0c58…` 与 `beb216fc…`),所以"大小一样"或"看着是新的"都不能当验货,**只有 sha256 能**。

---

## 2. `AiPermissionPanel.ReloadRulesAsync`:一处 async 但没有 await(CS1998)

在 Linux 上真跑 `dotnet publish` 时冒出一条警告(本机 SDK 不报):

```
src/TipCL.App/Pages/Ai/AiPermissionPanel.cs(121,24): warning CS1998: This async method lacks 'await' operators and will run synchronously.
```

方法体只做同步的界面重建(读的是已在内存里的 `Config.CONFIG`)—— 参考版这里是 `async` 是因为要走后端取设置。
**项目口径是"警告就是信号"**,所以按实际行为改成同步方法 `ReloadRules()`(`RefreshAsync` 保持可 `await` 的签名,
内部返回 `Task.CompletedTask`),而不是加一句 `await Task.CompletedTask` 去哄编译器。
改完在盒子上重跑 `dotnet publish`:**0 warning lines / 0 error lines**。

---

## 3. 在 Linux 上真跑了一遍 CI 的 package-linux 链路(第一次)

盒子装上 .NET SDK 8.0.134 之后,**出包不再需要"Windows 发布 → 推 45 MB 到盒子 → Linux 组装"**,
可以直接在盒子上按 CI 的命令走完:

```
dotnet publish src/TipCL.App -c Release -r linux-x64 -o dist/TipCL-linux-x64 \
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:UseSharedCompilation=false   → exit 0,0 警告 0 错误
LINUXDEPLOY=/home/user/tipcl/linuxdeploy.AppImage ./tools/package.sh --appimage-only --deb → exit 0
```

这条正是 CI 的 `package-linux` 任务在做的事(此前只在 Windows 侧手工做过其中一半)。产出与校验:

| 产物 | 大小 | sha256 |
|---|---|---|
| `dist/TipCL-linux-x64/TipCL.App`(自包含单文件) | 45,002,391 B | `9f5f8462ff512515f8f441babea4907284b21c7995381509c08b30187c1c78d6` |
| `dist/TipCL-linux-x64.AppImage` | 38,984,184 B | `beb216fcfa2b774319a82a0d98e0e0089d48bfdeccce3032162a854dffac0e81` |
| `dist/tipcl_0.1.0-m3_amd64.deb` | 37,540,562 B | `601873a786f4f3eb6a02fc5e0b2d8b69ef129c70ea7016b23f394e4295afa16a` |

**新产物的冒烟**(裸 FUSE,隔离 `TIPCL_HOME`):`--smoke` **exit 0**、`smoke.png` 575,416 B、
应用日志 **0 WARN / 0 ERROR**。

> 顺带一条环境事实:盒子上**没有 `zip`**(CI 的第三个产物是 zip,runner 上自带)。不影响本仓交付物,记一笔。

---

## 4. `.deb` 装完从**安装路径**真的拉起了 GUI(此前只验到 CLI)

此前 `.deb` 只验到 `sudo dpkg -i` → `tipcl --ai-selftest` 54/54 → `dpkg -r` 干净。这次补上界面:

| 步骤 | 结果 |
|---|---|
| `sudo dpkg -i dist/tipcl_0.1.0-m3_amd64.deb` | exit 0;`dpkg -l` 显示 `ii tipcl 0.1.0-m3 amd64` |
| `/usr/bin/tipcl` 内容 | `exec /opt/TipCL/TipCL.App "$@"`(包装脚本) |
| 私有 Xvfb `:8`(1280x800x24)+ xfwm4,`DISPLAY=:8 TIPCL_HOME=<隔离> tipcl` | 进程 `/opt/TipCL/TipCL.App` 存活;`xdotool` 枚举到窗口 **`TipCL` 900x550 @ (192,173)**;root 截图 587,682 B(`/home/user/tipcl/r21j/deb-gui.png`);应用日志正常启动,无 WARN/ERROR |
| `sudo dpkg -r tipcl` | `dpkg record gone`;`/opt` 下文件移除;无残留进程 |

用私有显示 `:8` 而不是抢 `:0`,是为了不和别的会话(以及两个子代理)互相干扰 —— 这一步本身也是 R21i 那条并发教训的落实。

---

## 5. 存档管理其余 chip:7 项逐点验证(PARITY §9.4 最后一条,子代理执行 + 主 agent 独立复核)

**环境**:盒子 `main`、`DISPLAY=:0`、`TIPCL_HOME=/home/user/tipcl/chips-home`、夹具存档 `WorldDlg`
(level.dat `MARKER-v1` / marker.txt `ORIGINAL`),最终 AppImage。

| # | chip | 结论 | 证据(可独立复核的部分) |
|---|---|---|---|
| 1 | **备份** | 有证据 | 改前 `backups/` 空、UI `备份(0)/还没有备份`;点「备份」后 `backups/WorldDlg-20261113-143946.zip` **271 B**,`unzip -l` = `WorldDlg/level.dat` 10 B + `WorldDlg/marker.txt` 9 B;日志 3 行(`任务开始 task-… 备份存档 WorldDlg` / `存档备份完成` / `任务完成`);UI 变 `备份(1)` 并出现「还原 / 删除」行。**如实记**:列表刷新是异步的,点完 +4 s 的截图里还显示 `备份(0)`,再等才变 |
| 2 | **还原** | 有证据 | 先篡改:`level.dat` md5 `19030d24…` → `96e5634…`、`marker.txt` `ab0b8843…` → `77fc9bf5…`;点备份行「还原」(确认框 353x145「用「WorldDlg-…zip」覆盖现有存档?」)→ 两个 md5 **回到** `19030d24aa324805da31daf16f2754ae` / `ab0b884358ef3d28a8d0b7266a3c0345`(与 zip 内条目一致,主 agent 复核 ✓),存档 mtime 14:13 → 14:51。**坑**:`xdotool key Return` 在 `Dlg.AskCore` 上不触发默认按钮(还会再开一个确认框),得按像素点「取消(656,459)/ 还原(717,459)」 |
| 3 | **备份行删除** | 有证据 | 改前 `备份(1)` + zip 在盘;点备份行「删除」(无二次确认)后 `backups/` **为空**(主 agent 复核 ✓,目录 mtime 14:54),UI `备份(0)/还没有备份`,对话框高 424 → 407 |
| 4 | **删除存档(正路径)** | 有证据 | 用一次性存档 `WorldTwo`(保留 WorldDlg):确认框 394x145 danger「「WorldTwo」会被永久删除,其中的建筑与进度都无法恢复。」→ 点「永久删除(746,459)」→ `saves/` 只剩 `WorldDlg`(主 agent 复核 ✓),WorldDlg 的 md5 前后不变,对话框高 407 → 340 |
| 5 | **导出** | 有证据 | 保存对话框 `0x03600007` «导出存档» 1000x700@(141,75):`WM_CLASS="TipCL","TipCL"`、`_NET_WM_WINDOW_TYPE_DIALOG`、`_NET_WM_PID` = 应用自身 pid ⇒ **进程内画的 Avalonia 对话框**;Name 预填 `WorldDlg.zip`,点 Save 后磁盘真出 zip:`chips/s12-reopen.zip` **271 B** md5 `1d69e978199cf8b333cf2ada05c62042`,`unzip -l` 两条目、内容与 `saves/WorldDlg/` 现场一致(主 agent 复核 ✓)。**坐标教训**:这个选择器**视觉坐标 = 输入坐标**(`xwininfo` 的绝对 Y 与 `wmctrl` 的 Y 都对不上客户端原点),必须从 root 截图量 |
| 6 | **打开** | 有证据 | 点存档行「打开」→ X 里多出 Thunar 窗口 `WorldDlg - Thunar`,地址栏 `/home/user/tipcl/chips-home/.minecraft/saves/WorldDlg/`,列出 `level.dat` + `marker.txt`,状态栏 `2 files: 19 bytes`。调用链 `Crash.OpenPath → xdg-open` 由一个**透传 shim**(记录 argv 后 `exec /usr/bin/xdg-open "$@"`)留痕:`argv=[…/saves/WorldDlg]` —— 注意 shim **只记录、不替代**,真正的窗口是 Thunar 自己开的 |
| 7 | **媒体区(截图 / 崩溃报告 / 日志)** | 有证据 | 三档列表与磁盘逐一对上:截图 `2 个文件`(`2026-11-13_12.00.00.png` 167 B、`2026-11-13_15.30.00.png` 167 B;`file(1)` 认得出是真 PNG 96x64);崩溃报告 `2 个文件`(66 B / 44 B,只列 `.txt`);日志 `2 个文件`(28 B / 41 B)。**打开**也真的拉起外部程序:截图 → Chrome 标签(标题变 `…png (96×64)`);日志 → Mousepad(`~/tipcl/chips-home/.minecraft/logs/2026-11-13-1.log`)。shim 日志里三条 argv 分别对应 saves 目录、截图、日志 ✓ |

**仍未测出(如实)**:媒体行右侧的「**导出**」。卡点是环境:此时盒子上已经有 `xdg-desktop-portal-gtk`,
点它走的是 **GTK 门户对话框**(默认目录 `/`,不在可写范围),屏上还叠着 4~6 个别的会话遗留的 «导出存档» 门户窗抢点击,
取不到"媒体文件被复制到指定位置"的证据。补测建议:停掉 portal 后端(回到进程内选择器、默认目录=进程 cwd)或把 cwd 设到可写目录。

**顺带修正 R21e 的一条口径(重要)**:R21e 当时写的是"「导入本地」弹的**不是** GTK/portal,是 Avalonia 托管的
`ManagedFileChooser`"。这次看到的是**两种形态都会出现**,取决于环境:

* 没有 portal 后端时:进程内 Avalonia 对话框(`WM_CLASS="TipCL","TipCL"`、`_NET_WM_PID` = 应用 pid、按钮 `Save|Cancel`);
* 盒子上跑起 `xdg-desktop-portal(-gtk)` 之后:同一次点击变成 **GTK 门户对话框**(`WM_CLASS="xdg-desktop-portal-gtk"`、另一个 pid、按钮顺序 `Cancel|Save`、默认目录 `/`)。

所以那条结论应读作"(当日、无 portal 后端的盒子上)不是 GTK/portal",**而不是"这个构建不会用 portal"**
—— 这类"文件选择器的形态"是环境相关的,证据口径必须连同环境一起写。

---

## 6. 并发教训(这一轮最贵的经验)

同一台盒子上**同时**跑两个验证会话(两个 `DISPLAY`:一个 `:0`、一个隔离的 `:9`),而且是**同一个 AppImage**:

1. **坐标会漂**:一个会话重启 App 之后,窗口从 902x575@(192,146) 变成 900x550,另一个会话的点击基准当场失效;
2. **焦点/层级会被抢**:新开的窗口被抬到最前,点击落到别的窗口上(桌面截图证据全都对不上);
3. **环境本身会被对方改变**:对方启动了 `xdg-desktop-portal-gtk`,于是**文件选择器的形态变了**(见上),
   两边对"同一个点击会弹出什么"的记录因此不一致;
4. 各自的 DOI(证据)如果不同时记录"当时的可见窗口清单 / 哪个 pid / 有没有 portal",事后无法复盘。

**落实的规矩**:1同时只跑一个**会点界面**的会话;2非要并行就用隔离 `DISPLAY`(Xvfb)且**各自的 TIPCL_HOME 与输出目录**;
3凡与外程序交互的证据,必须同时记录"环境快照"(窗口清单 + `WM_CLASS` + pid + 有没有 portal);
4`pkill` 这类收尾命令要按**自己的路径**匹配,别用宽匹配(会杀掉对方的进程)。

---

## 7. 数字与收尾

| 项 | 值 |
|---|---|
| 测试 | **446**(445 通过 + 1 跳过 + 0 失败);Windows 分批 245 + 201、Linux 分批 245 + 201 |
| 构建 | Debug/Release 0 警告 0 错误;**Linux 上 `dotnet publish` 0 警告 0 错误**(修掉 CS1998 之后) |
| 出包 | Linux 上一次跑完 publish → linuxdeploy → AppImage + .deb,冒烟 exit 0、0 WARN/0 ERROR |
| 安装 | `.deb` 装 → 从 `/opt/TipCL/TipCL.App` 拉起 GUI(窗口 `TipCL` 900x550)→ 卸载干净 |
| chip | 7 项逐点有证据;媒体行「导出」未测出(原因见上) |
| 产物 | AppImage `beb216fc…`(38,984,184 B)、`.deb` `601873a7…`(37,540,562 B)、单体 `9f5f8462…`(45,002,391 B) |

**遗留(如实)**:1媒体行「导出」的真机补测(需一个没有 portal 后端的干净会话);
2CI 仍未在真 runner 上跑过;3测试宿主内存增长的根因未定位(R21i 只做了量化 + 分批规避);
4盒子 `chips/` 下留了约 46 个脚本、约 55 张截图和那个透传 shim(需要清理时按目录整体删即可)。
