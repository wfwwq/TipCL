using System;
using System.Collections.Generic;
using System.Threading;

// .NET 插件 SDK（DESIGN §3.2）。插件引用 TipCL.exe 编译，实现 ITipCLPlugin。
// 数据类型约定：JSON 对象用 TipCL.Core.PyDict（也接受 IDictionary<string, object>），数组用 List<object>，整数 long，小数 double。
namespace TipCL.Plugins;

/// <summary>.NET 插件入口：manifest 的 entryType 指向实现这个接口的类型（要有无参构造）。</summary>
public interface ITipCLPlugin
{
	/// <summary>加载时调用一次。注册的东西用 <see cref="IPluginContext.Effect"/> 包起来，卸载时会逆序撤销。</summary>
	void Apply(IPluginContext ctx);
}

/// <summary>插件的几个目录。</summary>
public sealed class PluginPaths
{
	/// <summary>插件安装目录（只读看待）。</summary>
	public string Plugin { get; set; }

	/// <summary>插件自己的数据目录 plugin-data/&lt;id&gt;。</summary>
	public string Data { get; set; }

	/// <summary>缓存目录 plugin-data/&lt;id&gt;/cache。</summary>
	public string Cache { get; set; }

	/// <summary>日志目录 plugin-logs。</summary>
	public string Logs { get; set; }
}

public interface IPluginLogger
{
	void Debug(string text);

	void Info(string text);

	void Warn(string text);

	void Error(string text);
}

/// <summary>给 AI 的工具声明。</summary>
public sealed class PluginToolSpec
{
	public string Name { get; set; }

	public string Description { get; set; }

	/// <summary>JSON Schema（type:object），PyDict 或 IDictionary&lt;string, object&gt;。</summary>
	public object Parameters { get; set; }

	/// <summary>超时毫秒，默认 30 分钟；0 = 不限。</summary>
	public long TimeoutMs { get; set; } = 1800000L;

	public string Label { get; set; }

	/// <summary>true：只在白名单匹配的预设对话里出现。</summary>
	public bool PresetOnly { get; set; }
}

/// <summary>一次工具调用。</summary>
public sealed class PluginToolCall
{
	public string CallId { get; set; }

	public string ChatId { get; set; }

	public string Name { get; set; }

	/// <summary>模型给的参数（TipCL.Core.PyDict）。</summary>
	public TipCL.Core.PyDict Args { get; set; }

	/// <summary>发起这次调用的那条 AI 消息里的文字（DESIGN §5）。</summary>
	public string MessageText { get; set; }
}

/// <summary>工具结果的一段：文字或图片。</summary>
public sealed class PluginContentPart
{
	/// <summary>text | image</summary>
	public string Type { get; set; }

	public string Text { get; set; }

	/// <summary>图片：已保存的附件 id（IPluginContext.SaveImage 的返回值）。</summary>
	public string AttachmentId { get; set; }

	/// <summary>图片：原始字节（和 AttachmentId 二选一）。</summary>
	public byte[] Data { get; set; }

	public string MediaType { get; set; }
}

public sealed class PluginToolResult
{
	public List<PluginContentPart> Content { get; } = new List<PluginContentPart>();

	public bool IsError { get; set; }

	public static PluginToolResult FromText(string text, bool isError = false)
	{
		PluginToolResult r = new PluginToolResult
		{
			IsError = isError
		};
		r.Content.Add(new PluginContentPart
		{
			Type = "text",
			Text = text
		});
		return r;
	}

	public PluginToolResult AddImage(byte[] data, string mediaType = "image/png")
	{
		Content.Add(new PluginContentPart
		{
			Type = "image",
			Data = data,
			MediaType = mediaType
		});
		return this;
	}
}

/// <summary>Agent 预设（字段同 manifest contributes.agentPresets）。</summary>
public sealed class AgentPresetSpec
{
	public string Id { get; set; }

	public string Name { get; set; }

	public string Description { get; set; }

	public long Order { get; set; } = 100L;

	public string SystemPrompt { get; set; }

	public bool ReplaceSystemPrompt { get; set; }

	public bool IncludeRuntimeContext { get; set; } = true;

	public List<string> ToolInclude { get; set; } = new List<string>();

	public List<string> ToolExclude { get; set; } = new List<string>();

	public bool Builtin { get; set; } = true;

	/// <summary>0 = 不限步数；-1 = 用全局设置。</summary>
	public long MaxRounds { get; set; } = -1L;

	public string View { get; set; }
}

public interface IPluginLlm
{
	/// <summary>[{id, name}]</summary>
	List<object> Providers();

	/// <summary>[{id, name}]</summary>
	List<object> Models(string provider);

	/// <summary>
	/// 流式调用。request 字段同 NDJSON llm.stream（provider, model, system, messages, maxTokens, temperature, reasoningEffort, tools）。
	/// onChunk 收到 {type:"text-delta"|"reasoning-delta"|"tool-call"|"finish", ...}。返回 {text, finish:{kind}, usage}。
	/// </summary>
	TipCL.Core.PyDict Stream(object request, Action<TipCL.Core.PyDict> onChunk, CancellationToken token = default(CancellationToken));
}

public interface IPluginCredentials
{
	/// <summary>没有时返回 null。</summary>
	string Resolve(string name);

	/// <summary>value 为 null 删除。</summary>
	void Set(string name, string value);

	/// <summary>{name: {configured}}</summary>
	TipCL.Core.PyDict Status();
}

public interface IPluginSessions
{
	/// <summary>新建对话，返回 chatId。</summary>
	string Create(string preset = null, string title = null, string view = null, string prompt = null, bool open = true);

	List<object> List();

	/// <summary>{seq, events}；from 不含。</summary>
	TipCL.Core.PyDict Events(string chatId, long from = 0L);

	/// <summary>mode：auto | queue | steer。返回 {ok, mode, error?}。</summary>
	TipCL.Core.PyDict Send(string chatId, string text, string mode = "auto");

	void Cancel(string chatId);

	void Open(string chatId, string view = null);

	/// <summary>{running, seq}</summary>
	TipCL.Core.PyDict State(string chatId);
}

public interface IPluginConfig
{
	/// <summary>全部配置值 {key: value}。</summary>
	TipCL.Core.PyDict Get();

	object Get(string key);

	void Set(string key, object value);

	/// <summary>(key, value) 变化回调。</summary>
	IDisposable OnChange(Action<string, object> handler);
}

/// <summary>原生 WPF 页面（侧栏入口）。</summary>
public sealed class PluginPageSpec
{
	public string Id { get; set; }

	public string Label { get; set; }

	/// <summary>Fluent 字形（如 "\ue7fc"）或相对插件目录的图片路径。</summary>
	public string Icon { get; set; }

	public long Order { get; set; } = 100L;

	/// <summary>top | more</summary>
	public string Group { get; set; } = "more";
}

public sealed class PluginDashCardSpec
{
	public string Id { get; set; }

	public string Label { get; set; }

	public long DefaultW { get; set; } = 2L;

	public long DefaultH { get; set; } = 2L;
}

public sealed class PluginAiViewSpec
{
	public string Id { get; set; }

	public string Label { get; set; }

	public long Order { get; set; } = 100L;

	/// <summary>只在这些预设（完整 id 或预设 id）的对话里显示；空 = 所有对话。</summary>
	public List<string> Presets { get; set; } = new List<string>();
}

public interface IPluginUi
{
	IDisposable RegisterPage(PluginPageSpec spec, Func<object> factory);

	IDisposable RegisterDashCard(PluginDashCardSpec spec, Func<object> factory);

	IDisposable RegisterAiView(PluginAiViewSpec spec, Func<string, object> factory);

	void Toast(string text, string kind = "info");

	/// <summary>阻塞等用户点。</summary>
	bool Confirm(string title, string text, string ok = null, string cancel = null);

	/// <summary>阻塞等用户输入；取消返回 null。</summary>
	string Prompt(string title, string text, string placeholder = null, string @default = null);

	/// <summary>插件页 id（会自动加插件前缀）或内置页 id（launch、ai…）。</summary>
	void OpenPage(string page);

	void OpenExternal(string url);
}

/// <summary>插件上下文。</summary>
public interface IPluginContext
{
	string Id { get; }

	PluginPaths Paths { get; }

	IPluginLogger Logger { get; }

	/// <summary>可撤销的副作用：setup 返回撤销函数，插件卸载时逆序调用。</summary>
	void Effect(Func<Action> setup, string label = null);

	/// <summary>= launcher.call：调用 BackendApi 的任意 RPC 方法（需要 launcher 权限）。</summary>
	object Launcher(string method, IDictionary<string, object> kwargs = null);

	/// <summary>订阅事件（支持 * 通配，如 game.*）。handler(topic, data)。</summary>
	IDisposable OnEvent(string topic, Action<string, object> handler);

	IDisposable RegisterTool(PluginToolSpec spec, Func<PluginToolCall, CancellationToken, PluginToolResult> execute);

	IDisposable RegisterPreset(AgentPresetSpec spec);

	/// <summary>钩子（launch.before 等，DESIGN §4）。handler 收到 data（PyDict），返回修改后的 data 或 null。</summary>
	IDisposable RegisterHook(string hook, Func<object, object> handler);

	/// <summary>保存图片附件，返回附件 id（工具结果里用 PluginContentPart.AttachmentId 引用）。</summary>
	string SaveImage(byte[] data, string mediaType, string name = null);

	IPluginLlm Llm { get; }

	IPluginCredentials Credentials { get; }

	IPluginSessions Sessions { get; }

	IPluginConfig Config { get; }

	IPluginUi Ui { get; }

	/// <summary>提供跨插件服务（只在 .NET 插件之间共享）。</summary>
	void Provide(string name, object service);

	object Get(string name);
}
