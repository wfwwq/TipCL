# 外部编程工具接入指南(MCP)

TipCL 的内置 AI 助手**不写、也不编译** Java 模组。写模组交给外部编程工具(Claude Code、Cursor 等),它们通过 TipCL 的本机 MCP Server 把 TipCL 当测试环境用:建测试实例、装刚编好的 jar、试启动、读日志和崩溃报告。

> 来源:`progress/AI2-moddev-mcp.md` §2(第二轮 AI2-moddev-mcp 包),合并时挪到这里。决策见 DECISIONS D-82。

## 1. 先在 TipCL 里打开 MCP Server

设置 → AI 助手 → MCP Server:打开开关(默认端口 `25585`,Linux 版这一行可以直接改端口)→「复制地址」。

地址是 `http://127.0.0.1:25585/mcp`,只监听本机。打开后 AI 页标题栏会出现「MCP 已开启」状态条,外部每次调用都会更新为「MCP 已开启 · 工具名 时间」。

## 2. Claude Code

命令行(推荐):

```bash
claude mcp add --transport http tipcl http://127.0.0.1:25585/mcp
```

或在项目根放 `.mcp.json`:

```json
{
  "mcpServers": {
    "tipcl": { "type": "http", "url": "http://127.0.0.1:25585/mcp" }
  }
}
```

`run_test_launch` 带 `wait=true` 时最长要等 `timeout_seconds + 60` 秒。Claude Code 的单次工具超时不够时,可以设环境变量 `MCP_TOOL_TIMEOUT`(毫秒);更推荐用 `wait=false`,再用 `get_task_status(wait_seconds=30)` 轮询。

## 3. Cursor

`~/.cursor/mcp.json`(全局)或项目里的 `.cursor/mcp.json`:

```json
{
  "mcpServers": {
    "tipcl": { "url": "http://127.0.0.1:25585/mcp" }
  }
}
```

在 Cursor 设置 → MCP 里确认 `tipcl` 是绿点、能列出工具。

只支持 stdio 的客户端(如 Claude Desktop)可以用 `npx mcp-remote http://127.0.0.1:25585/mcp` 接入。

## 4. 安全口径(给外部工具的使用者)

- 只听 127.0.0.1、只收本机来源;带外站 `Origin` 的请求一律 403。没有 token 鉴权(同 Windows 参考版)。
- **所有写操作都要坐在电脑前的人在 TipCL 窗口里点「允许这一次」。** 弹确认时主窗口会自动置前;120 秒没人点按拒绝处理,返回 `permission_denied`(原因 `confirm_timeout`)。外部 agent 收到拒绝后应该停下来问用户,不要重试刷屏。
- Linux 版里,即使权限档是全自动,「以后都允许」对外部调用也不生效。
- 往非测试实例装 jar 需要 `allow_non_dev=true`,而且一定会再弹一次确认。

## 5. 推荐的开发 → 测试循环

1. **一次性准备**:`create_test_instance {mc_version:"1.20.1", loader:"fabric", dependencies:["fabric-api"]}`,记下返回的 `instance` 和 `task_id`;用 `get_task_status {task_id, wait_seconds:30}` 轮询到 `state=succeeded`。测试实例的 `.instance.json` 里会标 `dev=true`,名字带 `[dev]` 前缀。
2. **改代码 → 编译**:外部工具自己做,例如 `./gradlew build`,产物在 `build/libs/*.jar`。注意挑不带 `-sources` / `-dev` 后缀的那个。
3. **装进测试实例**:`install_local_mod {jar_path:"<绝对路径>/build/libs/mymod-1.0.1.jar", instance:"[dev] 1.20.1-fabric"}`。同 id 的旧版本会自动换下,返回里看 `warnings`(MC 版本不符、缺依赖提示)。被拒时看 `error`:`loader_mismatch` / `invalid_jar` / `not_a_mod` / `needs_confirmation` / `file_exists`。
4. **试启动**:`run_test_launch {instance:"[dev] 1.20.1-fabric"}`,再用 `get_task_status {task_id, wait_seconds:30}` 循环到 `finished=true`:
   - `result.outcome == "ready"`:进了主菜单,测试通过,游戏已自动关闭;
   - `crashed`:看 `result.crash.description / exception / suspected_mods` 和 `result.log_excerpt`,回到第 2 步改;
   - `timeout`:加大 `timeout_seconds`,或用 `ready_pattern` 指定你的模组在标题画面打的日志;
   - `launch_failed`:多半是没装好版本、没账号或缺 Java,看 `message`。
5. **退回上一个能用的 jar**:`revert_local_mod {change_id}`。
6. **要更细的信息**:照常用 `get_latest_log` / `get_crash_report` / `diagnose_launch` / `scan_mod_conflicts`,都是只读,不弹确认。

## 6. 已知限制

- 「进入主菜单」的判定靠日志特征行,还没在真游戏的各加载器、各版本上逐一核对过,必要时用 `ready_pattern` 自己指定。
- 测试启动结束时关的是启动器记住的那个游戏进程;开了「允许多开」又同时在玩别的实例时,可能关错。
- 依赖只能从 Modrinth 装;只在 CurseForge 上的依赖可以下载后用 `install_local_mod` 装。
- 任务记录只存在内存里,启动器重启后旧 `task_id` 查不到,返回 `not_found`。
- 长任务进度用查询工具 `get_task_status`,不用 MCP 的 progress notification(本机 MCP Server 没有 SSE 流)。
