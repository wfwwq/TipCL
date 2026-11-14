# 移植侧内置词表(`TipCL.Core.locales.*`)

这两份 JSON 是 **Linux/Avalonia 移植侧**的内嵌词表,由 `I18nCore` 通过
`CoreResources.ListDir("locales")` / `ReadText("locales/<name>.json")` 读取。

> ⚠️ **删掉它们 = 整站回退中文**:`TipCL.Core.csproj` 里的 `LogicalName` 必须是
> `TipCL.Core.locales.<name>.json`,少一个字都会静默变成空表,`Tr()` 就会一律返回 key 本身
> (表现:语言切成 English 后界面仍全是中文)。用例 `tests/TipCL.Tests/Core/I18nCoreLocalesTests.cs` 守着这条。

## 来源与生成方式(2026-11-14,task-30)

| 部分 | 出处 | 键数 |
|---|---|---|
| 底表 | `windos\tipcl\src\TipCL\Resources\locales\{en,zh_CN}.json`(Windows 侧最新) | 2951 |
| 追加 | 用户源码压缩包 `TipCL-552f3ac` → `windos\src\TipCL\Resources\locales\` 里**独有**的 16 个键 | +16 |
| 合计 | | **2967** |

* ⚠️ **三份词表互不包含**:2951 ⊄ 2809、2809 ⊄ 2951(2951 独有 158 键,压缩包独有 16 键,
  本仓 `linux/windos` 子树那份只有 2775 键,是最旧的)。**不要**以为最新那份是超集而把那 16 个键丢掉 ——
  它们里有 32c0b0b 改过的措辞(例如「安装时飞入底部任务条」「AI 助手在标题栏右上角」「新建实例...」),
  移植侧真的会查到。
* 重复键一律取**底表(最新)**的值;追加的 16 条用压缩包里的原文。
* 生成脚本与逐键清单:`D:\项目\tipcl\_tmp\tasks\linux-updater\{merge-locales.py,locale-merge-report.json}`
  (在仓库外;要重放就把底表换成更新的一份再跑一遍)。
* 为什么是**复制**而不是 link 到 `../../windos/...`:①`.github/workflows/ci.yml` 里 linux 构建只跑 `src/...`,
  且 `paths-ignore` 明确跳过"只动 `windos/**` 的提交" ⇒ 移植侧不能依赖 windos 子树;②最新那份词表不在本仓;
  ③移植侧历史上就是"复制 + 文件头注明来源"(见各 `来源: Windows 版参考仓库 …` 注释)。
* 分叉风险(将来 Windows 侧改了词表、这里没跟)由 `I18nCoreLocalesTests` 兜住:键数 ≥ 2951 且上述 16 键必须在;
  谁把旧表抄回来会立刻红。**下次更新词表时:换底表重跑脚本 → 同步更新断言里的下界与本文件的表。**

## 格式约定

raw UTF-8、**无 BOM**、LF、一行一"键: 值"(第一个 `{` 单独跟第一行、最后一个 `}` 单独一行),
和 Windows 侧那两份完全同形 —— 这样两边 diff 可比。
