using System;
using System.Collections.Generic;
using TipCL.Services;

using TipCL.Core.Launcher.Layout;

namespace TipCL.Pages.Layout;

// 自建区块的**纯模型门禁层**(禁 WPF,net8 测试宿主可直接加载):
//   settings 取值、危险类型的启用开关、web 地址准入、button 内部动作白名单。
// 每个判定都是"默认拒绝":拿不准就返回 false / 给出中文原因,由界面层显示占位。
// 做成 public 是因为:①L6 的内容工厂要用;②L5 的区块编辑器要复用同一套校验;
// ③net8 单测宿主(不同程序集)要直接验证这些门禁 —— 仓库没有 InternalsVisibleTo。
public static class WidgetCommon
{
	/// <summary>dangerous 类型的启用开关:必须是 JSON 布尔 true 才生效(Q5 默认关闭)。</summary>
	public const string EnabledKey = "enabled";

	/// <summary>允许的 button 内部动作前缀(page / launch / folder / rpc)。</summary>
	public static readonly string[] ActionKinds = new string[4] { "page", "launch", "folder", "rpc" };

	/// <summary>
	/// button 允许触发的 RPC 白名单(只读、无副作用;其余一律拒绝)。
	/// 与"切页 / 启动指定实例 / 打开目录"一起构成 §4.F 的内部动作白名单。
	/// </summary>
	public static readonly string[] RpcAllowList = new string[3] { "get_launcher_state", "list_instances", "plugins_list" };

	/// <summary>web 地址长度上限(防御异常输入)。</summary>
	public const int UrlMaxLength = 2048;

	/// <summary>取 settings 里的字符串(只认 JSON 字符串)。</summary>
	public static string Str(Dictionary<string, object?>? settings, string key, string dflt)
	{
		if (settings != null && settings.TryGetValue(key, out object? value) && value is string text)
		{
			return text;
		}
		return dflt;
	}

	/// <summary>取 settings 里的布尔(只认 JSON 布尔)。</summary>
	public static bool Flag(Dictionary<string, object?>? settings, string key, bool dflt)
	{
		if (settings != null && settings.TryGetValue(key, out object? value) && value is bool flag)
		{
			return flag;
		}
		return dflt;
	}

	/// <summary>取 settings 里的数字(整数/浮点都收)。</summary>
	public static double Num(Dictionary<string, object?>? settings, string key, double dflt)
	{
		if (settings != null && settings.TryGetValue(key, out object? value))
		{
			if (value is long l)
			{
				return l;
			}
			if (value is int i)
			{
				return i;
			}
			if (value is double d && !double.IsNaN(d) && !double.IsInfinity(d))
			{
				return d;
			}
		}
		return dflt;
	}

	/// <summary>危险类型(web / button)是否已被用户显式开启。</summary>
	public static bool IsEnabled(WidgetDef? widget)
	{
		return widget != null && LayoutSchema.IsDangerousWidgetType(widget.Type) && Flag(widget.Settings, EnabledKey, false);
	}

	/// <summary>
	/// web 区块的地址准入:只允许 https,禁止 file:// 与任何非 http(s) 方案,必须有主机名。
	/// 不合法时给出中文原因(界面直接显示)。
	/// </summary>
	public static bool TryAcceptWebUrl(string? url, out string normalized, out string reason)
	{
		normalized = "";
		reason = "";
		string text = (url ?? "").Trim();
		if (text.Length == 0)
		{
			reason = I18n.L("没有填写网页地址");
			return false;
		}
		if (text.Length > UrlMaxLength)
		{
			reason = I18n.L("网页地址太长");
			return false;
		}
		if (text.IndexOf("://", StringComparison.Ordinal) < 0)
		{
			reason = I18n.L("网页地址必须以 https:// 开头");
			return false;
		}
		if (text.StartsWith("file://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || text.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
		{
			reason = I18n.L("只允许 https 网页,禁止本地文件与脚本地址");
			return false;
		}
		if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || uri == null)
		{
			reason = I18n.L("网页地址格式不对");
			return false;
		}
		if (uri.Scheme != Uri.UriSchemeHttps)
		{
			reason = I18n.L("只允许 https 网页(http 与其它协议一律拒绝)");
			return false;
		}
		if (string.IsNullOrEmpty(uri.Host))
		{
			reason = I18n.L("网页地址缺少主机名");
			return false;
		}
		normalized = uri.AbsoluteUri;
		return true;
	}

	/// <summary>
	/// button 的内部动作白名单解析。允许:<c>page:&lt;页面id&gt;</c>、<c>launch:&lt;实例&gt;</c>、
	/// <c>launch:&lt;实例&gt;|&lt;版本&gt;</c>、<c>folder:data</c>、<c>rpc:&lt;白名单RPC&gt;</c>;
	/// 其余(含任意命令、任意路径)一律拒绝并给出中文原因。
	/// </summary>
	public static bool TryParseAction(string? action, out string kind, out string arg1, out string arg2, out string reason)
	{
		kind = "";
		arg1 = "";
		arg2 = "";
		reason = "";
		string text = (action ?? "").Trim();
		if (text.Length == 0)
		{
			reason = I18n.L("没有填写按钮动作");
			return false;
		}
		int sep = text.IndexOf(':');
		if (sep <= 0)
		{
			reason = I18n.L("按钮动作要写成 类型:参数(例如 page:launch)");
			return false;
		}
		string head = text.Substring(0, sep);
		string rest = text.Substring(sep + 1).Trim();
		if (Array.IndexOf(ActionKinds, head) < 0)
		{
			reason = I18n.L("不支持的按钮动作:{0}(只允许 page / launch / folder / rpc)", head);
			return false;
		}
		if (rest.Length == 0)
		{
			reason = I18n.L("按钮动作缺少参数");
			return false;
		}
		switch (head)
		{
		case "page":
			kind = head;
			arg1 = rest;
			return true;
		case "folder":
			// 只允许打开启动器自己的数据目录,不接受任意路径
			if (!string.Equals(rest, "data", StringComparison.OrdinalIgnoreCase))
			{
				reason = I18n.L("只允许打开启动器数据目录(folder:data)");
				return false;
			}
			kind = head;
			arg1 = rest;
			return true;
		case "rpc":
			if (Array.IndexOf(RpcAllowList, rest) < 0)
			{
				reason = I18n.L("这个 RPC 不在白名单里:{0}", rest);
				return false;
			}
			kind = head;
			arg1 = rest;
			return true;
		default:
		{
			int bar = rest.IndexOf('|');
			kind = head;
			if (bar > 0)
			{
				arg1 = rest.Substring(0, bar).Trim();
				arg2 = rest.Substring(bar + 1).Trim();
			}
			else
			{
				arg1 = rest;
			}
			if (arg1.Length == 0)
			{
				reason = I18n.L("按钮动作缺少实例名");
				return false;
			}
			return true;
		}
		}
	}
}
