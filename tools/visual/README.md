# tools/visual —— 无窗口视觉巡检(AI3-visual)

全程不弹任何窗口、不联网、不启动 Minecraft。截图与报告输出到仓库外的 `<仓库同级>\_visual\`。

| 文件 | 作用 |
|---|---|
| `run-tour.ps1 [-Build] [-Script x.json] [-Out dir]` | 在私有临时 `TIPCL_HOME`(`_visual\home-tour`)里跑 `TipCL.App.dll --visual-tour <剧本> <输出目录>`,默认剧本 `tours/full.json`,默认输出 `_visual\tour` |
| `tours/full.json` | 完整巡检剧本:A 启动页 v3(1000 / 780)· B 下载页编辑态 · C 设置 → 布局 · D v2 / Windows 实拍对应状态 · E 全页面 / 分段 / 子页 / 对话框 / 浮层 / 深色 / 窄窗口 · F 主题色切换 · G 各页编辑态 |
| `tours/probe*.json`、`tours/bg-isolate.json` | 探路 / 隔离实验用的小剧本 |
| `shoot-mockups.ps1 [-Only pattern]` | 把 v3 效果图复制到 `_visual\mockups\states\` 并注入 `mockup-states.js`,用无头 Edge(`--headless=new`、独立 `--user-data-dir`、`-WindowStyle Hidden`)按 `mockup-states.json` 的 hash 逐状态截图并裁到窗口元素 |
| `compare.py` | 按 `pairs.json` 生成 `_visual\report\index.html`(效果图 / 截图 / 热力图 + 分数,按分数从差到好)。只需标准库;能 import Pillow 时自动用它加速 |
| `vtlib.py` / `crop.py` | PNG 读写 / 缩放 / 裁剪小工具(标准库实现) |

## 剧本格式(`src/TipCL.App/Session/VisualTourScript.cs` 头注释有完整说明)

```jsonc
{ "width": 900, "height": 550, "theme": "light", "fixture": true, "settleMs": 350,
  "steps": [
    { "op": "navigate", "page": "download" },
    { "op": "rightclick", "text": "26.3" },
    { "op": "click", "text": "自定义布局..." },
    { "op": "key", "keys": "Ctrl+Z" },
    { "op": "scroll", "to": { "text": "布局方案" } },
    { "op": "resize", "width": 780, "height": 600 },
    { "op": "theme", "value": "dark" },
    { "op": "shot", "name": "download-edit" }
  ] }
```

动作:navigate / click / rightclick / dblclick / hover / key / type / scroll / resize / theme / wait / shot / expect / close / dump / reset。
选择器:name / text / contains / tip / watermark / type / within / index(最上层优先:对话框 → 浮层 → 主窗口)。点击 = 算元素中心点(落不到目标上时在元素内找一个点得中的点)再 MouseDown / MouseUp,不写死坐标。

## 结果

- 每张截图:`<name>.png`(有对话框时主窗口 + 浮层 + 对话框合成,另存 `<name>.dialog.png`)+ `<name>.tree.txt`(元素清单)。
- `tour-result.json`:每步 OK / FAIL + 原因;每张截图的 `Truncated`(被截断的文字,实测);被挡的网络请求;被拦下的打开链接 / 文件夹 / 文件选择框。
- 退出码:0 全部 OK;8 有 FAIL 步骤;9 剧本 / 参数错误或没给 `TIPCL_HOME`。

## 安全护栏

- 必须显式设 `TIPCL_HOME`;假数据生成器拒绝真实用户目录,只在空目录或带 `.tipcl-visual-fixture` 标记的目录里重建。
- 出网:`HttpHandlerHook` 闸门,全部当场 503;Modrinth 搜索给固定假结果;版本清单 / 新闻走预置缓存。
- `ShellOpen` / `Crash.ShellOpenHook` / `Auth.OpenBrowserHook` 只记录;`Dlg.PickFilesOverride` 一律返回「取消」。
- 拒点「启动游戏 / 启动 / 仍然启动 / 强制启动」等;每步之后检查任务列表,出现「启动游戏」任务即记 FAIL。注意:启动页实例切换浮层里点版本 = 直接启动(见 findings D9),剧本里不要点那里。
