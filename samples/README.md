# TipCL 插件示例

契约见 `docs/plugins/DESIGN.md`，类型见 `src/TipCL/Resources/plugin-host/tipcl-plugin.d.ts`。两个示例都不需要 npm 依赖和构建步骤。

| 目录 | 内容 |
|---|---|
| `hello-plugin/` | 覆盖全部能力：侧栏页面 `main`、AI 标签 `watch`（像 dsh-game 的游戏标签：显示对话事件、输入框 steer、停止按钮）、浮层 `bubble`、首页卡片 `card`、manifest 设置 + 凭据 `HELLO_API_KEY`、manifest 预设 `hello` + 运行时预设 `hello-quick`、阻塞工具 `hello_wait`（最多等 N 秒，被 steer / 「戳一下」唤醒或被停止，返回文字 + 生成的 PNG 附件）、`hello_badge`（原始图片数据）、`hello_ask`（`llm.stream`）、SSE / 二进制 / 通配路由、`launch.before` 钩子加 JVM 参数、`game.log` 订阅、给别的插件用的 `hello` 服务。`main` 是数组（`index` + `agent` 两个入口，和 dsh-game 一样）。 |
| `hello-consumer/` | CommonJS 插件：`ctx.get('hello')` 与 `ctx.inject(['hello'], …)`，提供者晚加载、卸载都能跟上。 |
| `host-tests/` | 假 TipCL（`fake-tipcl.mjs`）启动 `host.mjs`，走真实协议做端到端测试；`fixtures/dsh-compat` 是照抄 dsh-game 宿主端写法（含 `@deepseek-ai/dsh-tools` 导入）的兼容性用例。不随 exe 打包。 |

本地开发：TipCL 插件页「本地安装 → 链接文件夹（开发者模式）」选 `samples/hello-plugin`，改文件自动重载。

运行测试（Node ≥ 22）：

```
node --test "samples/host-tests/*.test.mjs"
```

`HOST_DEBUG=1` 时把宿主的 stderr 打出来。
