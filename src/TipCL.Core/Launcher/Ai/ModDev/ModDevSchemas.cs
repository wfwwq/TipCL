// AI2-moddev-mcp:本包 5 个工具的 schema 与风险分级。 // i18n:ignore-file(工具描述是给模型 / 外部 agent 看的,不上界面)
//
// 工具(风险分级登记在 AiToolTypes.cs 的 ToolMeta.ALL 末尾,口径见那里的注释):
//   create_test_instance  写(write_local, medium)       建测试实例(dev=true)并在后台装 MC / 加载器 / 依赖;
//   install_local_mod     写(write_local, medium)       装本地 jar(非 dev 实例要 allow_non_dev 且执行器强制再确认);
//   revert_local_mod      删除类(delete, high)          按 change_id 回滚(会删掉新装的 jar),不给「本对话都允许」;
//   run_test_launch       启动类(launch, medium, 长任务) 起游戏等主菜单 / 崩溃 / 超时,自动关游戏;
//   get_task_status       只读(read, low, 标 long_running 不并行;读的是进程内共享任务表)。
using System.Collections.Generic;
using TipCL.Core;

namespace TipCL.Core.Launcher.Ai;

/// <summary>本包工具的 schema / 元信息表。</summary>
public static class ModDevSchemas
{
    public static readonly string[] TOOL_NAMES =
    {
        "create_test_instance", "install_local_mod", "revert_local_mod", "run_test_launch", "get_task_status",
    };

    public static bool Handles(string name) => System.Array.IndexOf(TOOL_NAMES, name) >= 0;

    private const string SchemaJson = """
    [
    {"type":"function","function":{"name":"create_test_instance","description":"新建一个模组开发用的测试实例(.instance.json 标 dev=true,名字带 [dev] 前缀),并在后台安装指定 MC 版本、加载器和依赖模组(如 fabric-api)。返回 instance 与 task_id,用 get_task_status 查安装进度。只用于测试外部工具编译出来的模组,不要拿日常实例当测试环境","parameters":{"type":"object","properties":{"mc_version":{"type":"string","description":"如 1.20.1"},"loader":{"type":"string","description":"fabric / forge / neoforge / quilt / vanilla"},"loader_version":{"type":"string","description":"加载器版本,空则用最新稳定版"},"name":{"type":"string","description":"实例名,会自动加 [dev] 前缀;空则用「版本-加载器」"},"dependencies":{"type":"array","items":{"type":"string"},"description":"要一起装的依赖模组 slug(Modrinth),如 [\"fabric-api\"]"},"install":{"type":"boolean","description":"是否立刻安装游戏本体与依赖,默认 true"}},"required":["mc_version","loader"]}}},
    {"type":"function","function":{"name":"install_local_mod","description":"把本机编译好的模组 jar 装进实例的 mods。只接受 .jar(合法 zip),会读 fabric.mod.json / quilt.mod.json / META-INF/mods.toml / META-INF/neoforge.mods.toml 里的 id、版本、依赖与适用 MC 版本;加载器与实例不匹配直接拒绝。同 id 的旧 jar 会先挪到备份目录再替换,并做检查点,返回 change_id 可用 revert_local_mod 撤回。默认只装进测试实例(dev);装进日常实例必须带 allow_non_dev=true,且会再次请用户确认","parameters":{"type":"object","properties":{"jar_path":{"type":"string","description":"jar 的绝对路径,如 build/libs 下的产物"},"instance":{"type":"string","description":"目标实例名,建议用 create_test_instance 返回的 instance"},"allow_non_dev":{"type":"boolean","description":"目标不是测试实例时必须为 true 才会执行(风险更高,会额外确认)"}},"required":["jar_path"]}}},
    {"type":"function","function":{"name":"revert_local_mod","description":"撤回一次 install_local_mod:删掉那次装进去的 jar,把被替换的旧 jar 放回 mods(按检查点还原)","parameters":{"type":"object","properties":{"change_id":{"type":"string","description":"install_local_mod 返回的 change_id"}},"required":["change_id"]}}},
    {"type":"function","function":{"name":"run_test_launch","description":"把实例当测试环境启动一次:等到日志出现「进入主菜单」特征行、游戏崩溃或超时(默认 180 秒)就结束并自动关闭游戏,返回 outcome(ready / crashed / exited / timeout / launch_failed)、耗时、日志关键片段、崩溃报告摘要与诊断。默认立刻返回 task_id,用 get_task_status 查;wait=true 时等结束再返回。同一时间只跑一个","parameters":{"type":"object","properties":{"instance":{"type":"string","description":"实例名,建议用测试实例"},"version":{"type":"string","description":"版本 id,空则用测试实例装好的那个 / 实例里唯一的那个"},"timeout_seconds":{"type":"integer","description":"最长等多少秒进主菜单,默认 180(5–1800)"},"ready_pattern":{"type":"string","description":"自定义「进入主菜单」的日志正则,空则用内置特征行"},"wait":{"type":"boolean","description":"true = 等测试结束再返回结果;默认 false 立刻返回 task_id"}}}}},
    {"type":"function","function":{"name":"get_task_status","description":"查长任务的进度与结果:create_test_instance / run_test_launch 的 task_id,以及 install_* / launch_game 起的启动器任务。结束后 result 里是完整结果。wait_seconds>0 时最多等这么久直到任务结束(减少轮询)","parameters":{"type":"object","properties":{"task_id":{"type":"string"},"wait_seconds":{"type":"number","description":"最多等几秒(0–60),默认 0 立即返回"},"log_lines":{"type":"integer","description":"带回最后多少行日志,默认 40"}},"required":["task_id"]}}}
    ]
    """;

    public static List<object> Schemas() => Py.ListOr(PyJson.Loads(SchemaJson));
}
