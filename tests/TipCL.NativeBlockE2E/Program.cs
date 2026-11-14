using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using DotNetBlockSample;
using TipCL.Core.Launcher.Ai;
using TipCL.Core.Plugins;
using TipCL.Pages.Layout;
using TipCL.Pages.Plugins;
using TipCL.Plugins;
using TipCL.Services;

namespace TipCL.NativeBlockE2E;

/// <summary>
/// .NET 原生插件 <c>kind = block</c> 的**运行时**端到端驱动。
///
/// 它走的是启动器自己的启动路径(不是模拟):
/// <list type="number">
/// <item><see cref="BridgeHost.StartAsync"/> —— 进程内后端(BackendApi + EventBus + 真实 127.0.0.1 桥)
///   并在这里面 <c>PluginManager.StartGlobal</c>;</item>
/// <item>插件目录安装 + <c>plugins.json</c> 里预置"已启用 + 已授予 ui 权限"(等价于用户在插件页点过启用);</item>
/// <item><c>PluginManager</c> 发现 → <c>DotNetPluginLoader.Load</c> → <c>Assembly.LoadFrom</c> → <c>Apply</c>
///   → <c>ctx.Ui.RegisterBlock</c> → <c>PluginUiRegistry</c>;</item>
/// <item><c>plugins_ui</c> RPC(界面真正读的那条)拿到 <c>kind=block</c> 行;</item>
/// <item>布局侧内容工厂 <c>WidgetRegistry.Build("plugin:&lt;插件&gt;:&lt;块&gt;")</c> 拿到插件自己的控件;</item>
/// <item>停用插件后:注册表条目消失、RPC 行消失、同一块 id 回到「插件区块不可用」占位(块保留、不删数据)。</item>
/// </list>
///
/// 退出码:0 = 全部通过;1 = 有断言失败;2 = 环境/前置不满足(找不到 dll 等)。
/// </summary>
internal static class Program
{
	private const string PluginId = "dotnet-block-sample";

	private const string BlockRef = "plugin:dotnet-block-sample:hello";

	private static int _pass;

	private static int _fail;

	private static readonly StringBuilder Log = new StringBuilder();

	[STAThread]
	private static int Main(string[] args)
	{
		string repo = FindRepoRoot();
		string sampleDir = Path.Combine(repo, "samples", "dotnet-block-plugin");
		string dll = Option(args, "--dll") ?? Path.Combine(sampleDir, "bin", "Release", "DotNetBlockSample.dll");
		string home = Option(args, "--home") ?? Path.Combine(Path.GetTempPath(), "tipcl-native-block-e2e-" + Guid.NewGuid().ToString("N"));

		Say("=== TipCL .NET 插件 kind=block 运行时 E2E ===");
		Say("repo   = " + repo);
		Say("sample = " + sampleDir);
		Say("dll    = " + dll);
		Say("home   = " + home);
		if (!File.Exists(dll))
		{
			Say("[ENV ] 找不到插件 dll;先用 samples\\dotnet-block-plugin\\run-e2e.ps1 构建");
			Flush();
			return 2;
		}

		// 1) 装插件:插件目录 + plugins.json(已启用 + 已授予 ui)
		string pluginDir = Path.Combine(home, "plugins", PluginId);
		Directory.CreateDirectory(pluginDir);
		File.Copy(Path.Combine(sampleDir, "tipcl-plugin.json"), Path.Combine(pluginDir, "tipcl-plugin.json"), overwrite: true);
		File.Copy(dll, Path.Combine(pluginDir, "DotNetBlockSample.dll"), overwrite: true);
		PluginDirs dirs = new PluginDirs(home);
		new PluginStateStore(dirs.StateFile).Update(PluginId, delegate(PluginRecord r)
		{
			r.Enabled = true;
			r.Version = "0.1.0";
			r.Source = "local";
			r.InstalledAt = DateTime.Now.ToString("o");
			if (!r.GrantedPermissions.Contains("ui"))
			{
				r.GrantedPermissions.Add("ui");
			}
		});
		Say("state  = " + dirs.StateFile + "(enabled=true, granted=[ui])");

		// 2) 起真后端 + 真插件管理(这一步就是 App 的启动路径)
		Environment.SetEnvironmentVariable("TIPCL_HOME", home);
		BridgeHost host = null;
		try
		{
			host = BridgeHost.StartAsync().GetAwaiter().GetResult();
			AppServices.Host = host;
			AppServices.Client = host.Client;
			Say("bridge = 127.0.0.1:" + host.Port + " (" + host.Backend + ")");

			// 3) 布局侧口子先证明它是真的:不存在的插件块 => 不可用占位
			WidgetRegistry.EnsureRegistered();
			string unavailable = I18n.L("插件区块不可用");
			// 【非致命】这条只说明"口子是真的";它渲染的是 PluginBlocks.Unavailable(),
			// 而 Ui.Muted/Small 要走 Application.Current 的资源字典(风格键 T.Muted/T.Small)。
			// 独立驱动没有 App.xaml 资源 → 这里会抛 NRE,所以只记一行 WARN,不当失败
			// (在真应用里跑时它自然是占位文字)。
			WidgetRegistry provider = new WidgetRegistry();
			Exception buildErr;
			object made0 = BuildBlock(provider, "plugin:no-such-plugin:hello", out buildErr);
			Warn("布局口子存在(不存在的插件块 => 占位;独立驱动无 App 资源,可能报 " + (buildErr == null ? "无异常" : buildErr.GetType().Name) + ")",
				buildErr == null || Text(made0).IndexOf(unavailable, StringComparison.Ordinal) >= 0,
				Describe(made0) + (buildErr == null ? "" : " 工厂异常=" + buildErr.GetType().Name));

			// 4) 等插件被真实加载
			DateTime deadline = DateTime.UtcNow.AddSeconds(30.0);
			while (DateTime.UtcNow < deadline && PluginUiRegistry.Find(PluginId, "block", "hello") == null)
			{
				Thread.Sleep(100);
			}
			NativeUiEntry entry = PluginUiRegistry.Find(PluginId, "block", "hello");
			if (entry == null)
			{
				PluginInstance p = PluginManager.Instance == null ? null : PluginManager.Instance.Get(PluginId);
				Say("[FAIL] 插件没进注册表。诊断:");
				Say("       instance = " + ((p == null) ? "<null>" : (p.Id + " state=" + p.State + " error=" + p.Error)));
				Say("       manager  = " + ((PluginManager.Instance == null) ? "<null>" : PluginManager.Instance.Snapshot().Count.ToString() + " 个实例"));
				string plog = Path.Combine(home, "plugin-logs", PluginId + ".log");
				Say("       plugin-log(" + plog + ") = " + (File.Exists(plog) ? File.ReadAllText(plog) : "<不存在>"));
				_fail++;
			}
			else
			{
				_pass++;
				Say("[PASS] 插件注册 block 进注册表:plugin=" + entry.Plugin + " kind=" + entry.Kind + " id=" + entry.Id + " label=" + entry.Label);
			}

			// 5) plugins_ui RPC(界面真正读的那条)里要有它
			PluginUi.RefreshAsync().GetAwaiter().GetResult();
			string uiRaw = AppServices.Client.TryCallAsync<JsonElement>("plugins_ui").GetAwaiter().GetResult().GetRawText();
			string row = RowOf(uiRaw, PluginId, "block", "hello");
			Check("plugins_ui RPC 里出现该块(kind=block)", row.Length > 0, (row.Length > 0) ? row : uiRaw.Substring(0, Math.Min(300, uiRaw.Length)));
			Check("该行按契约是原生行(native:true、url 空)", row.IndexOf("\"native\": true", StringComparison.Ordinal) >= 0, row);
			// 【已知缺陷观测点】若 manifest 也声明了同一个块,RPC 会给出两行(一行 native:false),
			// 前端 PluginUi.Find 取第一行 => 拿到非原生行 => 控件渲染不出来。见 README 的"已知缺陷"。
			string[] dup = RowsOf(uiRaw, PluginId, "block", "hello");
			Warn("同一 (plugin,kind,id) 在 plugins_ui 里只有一行(多于一行 = 前端可能选错)", dup.Length <= 1, string.Join(" | ", dup));

			// 6) 布局里能用:内容工厂返回插件自己的控件(两条都验:工厂本体 + 宿主真走的口子)
			object made = BuildBlock(provider, BlockRef, out buildErr);
			string madeText = Text(made);
			Check("内容工厂把 " + BlockRef + " 解析成插件控件(含「" + BlockPlugin.BlockText + "」)",
				madeText.IndexOf(BlockPlugin.BlockText, StringComparison.Ordinal) >= 0,
				Describe(made) + (buildErr == null ? "" : " 工厂异常=" + buildErr.GetType().Name + ": " + buildErr.Message));
			Check("该控件不是「插件区块不可用」占位", madeText.IndexOf(unavailable, StringComparison.Ordinal) < 0, madeText.Trim());
			object hostMade = LayoutContentRegistry.Build(BlockRef, "launch");
			Check("宿主真走的口子 LayoutContentRegistry.Build(" + BlockRef + ") 也拿到插件控件",
				Text(hostMade).IndexOf(BlockPlugin.BlockText, StringComparison.Ordinal) >= 0,
				Describe(hostMade) + " 文字=" + OneLine(Text(hostMade)));

			// 7) 工厂可重复调用(宿主每次要内容都会再调一次),每次都是新实例
			if (entry != null)
			{
				FrameworkElement a = entry.Create();
				FrameworkElement b = entry.Create();
				Check("块工厂可重复调用且每次返回新控件",
					a != null && b != null && !ReferenceEquals(a, b) && Text(a).IndexOf(BlockPlugin.BlockText, StringComparison.Ordinal) >= 0 && Text(b).IndexOf(BlockPlugin.BlockText, StringComparison.Ordinal) >= 0,
					"a=" + ((a == null) ? "<null>" : a.GetType().Name) + " b=" + ((b == null) ? "<null>" : b.GetType().Name));
			}

			// 8) 停用插件:条目消失 + RPC 行消失 + 块保留但回到不可用
			PluginManager.Instance.SetEnabled(PluginId, false);
			bool gone = false;
			DateTime deadline2 = DateTime.UtcNow.AddSeconds(20.0);
			while (DateTime.UtcNow < deadline2 && !gone)
			{
				gone = PluginUiRegistry.Find(PluginId, "block", "hello") == null;
				if (!gone)
				{
					Thread.Sleep(100);
				}
			}
			Check("停用后注册表条目消失", gone || PluginUiRegistry.Find(PluginId, "block", "hello") == null, "find=" + (PluginUiRegistry.Find(PluginId, "block", "hello") == null ? "<null>" : "命中"));

			PluginUi.RefreshAsync().GetAwaiter().GetResult();
			string uiRaw2 = AppServices.Client.TryCallAsync<JsonElement>("plugins_ui").GetAwaiter().GetResult().GetRawText();
			Check("停用后 plugins_ui 里没有该块", RowOf(uiRaw2, PluginId, "block", "hello").Length == 0, "raw 长度=" + uiRaw2.Length);

			object made2 = BuildBlock(provider, BlockRef, out buildErr);
			string made2Text = Text(made2);
			Check("停用后同一块 id 不再是插件控件(块保留、不删数据)",
				made2Text.IndexOf(BlockPlugin.BlockText, StringComparison.Ordinal) < 0,
				Describe(made2) + " 文字=" + OneLine(made2Text) + (buildErr == null ? "" : " 工厂异常=" + buildErr.GetType().Name + ": " + buildErr.Message));
			Warn("停用后同一块 id 走「" + unavailable + "」占位(需 App 资源字典,独立驱动下可能报 " + (buildErr == null ? "无异常" : buildErr.GetType().Name) + ")",
				buildErr == null || made2Text.IndexOf(unavailable, StringComparison.Ordinal) >= 0,
				OneLine(made2Text));

			// ─────────── 第二段:对外开放的布局 v2 RPC(经真实桥的 HTTP 往返,不是直接调方法) ───────────
			string get1 = AppServices.Client.TryCallAsync<string>("layout_v2_get").GetAwaiter().GetResult() ?? "";
			Check("经真实桥 POST /rpc 调 layout_v2_get 拿到布局摘要", get1.Length > 40, OneLine(get1));
			string applyResult = AppServices.Client.TryCallAsync<string>("layout_v2_apply", new { page = "launch", mode = "grid", cols = 3 }).GetAwaiter().GetResult() ?? "";
			Check("经真实桥调 layout_v2_apply 改了布局并自动备份",
				applyResult.IndexOf("已更新布局", StringComparison.Ordinal) >= 0 && applyResult.IndexOf(LayoutTools.BackupPrefix, StringComparison.Ordinal) >= 0,
				OneLine(applyResult));
			string get2 = AppServices.Client.TryCallAsync<string>("layout_v2_get").GetAwaiter().GetResult() ?? "";
			Check("再读一次能读到刚才那次改动(grid)", get2.IndexOf("grid", StringComparison.Ordinal) >= 0 && !string.Equals(get1, get2, StringComparison.Ordinal), OneLine(get2));
		}
		catch (Exception ex)
		{
			_fail++;
			Say("[FAIL] 未捕获异常:" + ex);
		}
		finally
		{
			try
			{
				if (PluginManager.Instance != null)
				{
					PluginManager.Instance.Shutdown(TimeSpan.FromSeconds(3.0));
				}
			}
			catch (Exception)
			{
			}
			try
			{
				if (host != null)
				{
					host.Dispose();
				}
			}
			catch (Exception)
			{
			}
		}

		Say("");
		Say("RESULT: " + ((_fail == 0) ? "PASS" : "FAIL") + "  (通过 " + _pass + " / 失败 " + _fail + ")");
		Flush();
		return (_fail == 0) ? 0 : 1;
	}

	// ────────────────────────────── 小工具 ──────────────────────────────

	private static void Check(string name, bool ok, string detail)
	{
		if (ok)
		{
			_pass++;
			Say("[PASS] " + name);
		}
		else
		{
			_fail++;
			Say("[FAIL] " + name);
		}
		Say("       " + OneLine(detail));
	}

	/// <summary>非致命:记录下来但不算失败(依赖真应用环境的那几条走这里)。</summary>
	private static void Warn(string name, bool ok, string detail)
	{
		Say((ok ? "[ ok ] " : "[WARN] ") + name);
		Say("       " + OneLine(detail));
	}

	/// <summary>
	/// 直接问内容工厂(public 实例)。宿主那条 <see cref="LayoutContentRegistry.Build"/> 会把工厂异常
	/// 吞掉只返回 null,排障时看不到原因,所以这里自己抓一次。
	/// </summary>
	private static object BuildBlock(WidgetRegistry provider, string blockId, out Exception err)
	{
		err = null;
		try
		{
			if (!provider.CanBuild(blockId))
			{
				err = new InvalidOperationException("CanBuild=false");
				return null;
			}
			return provider.Build(blockId, "launch");
		}
		catch (Exception ex)
		{
			err = ex;
			return null;
		}
	}

	private static string Describe(object node)
	{
		if (node == null)
		{
			return "<null>";
		}
		return node.GetType().Name + " 文字=" + OneLine(Text(node));
	}

	/// <summary>把控件树里的文字收集起来(纯 WPF,不走布局引擎)。</summary>
	private static string Text(object node)
	{
		StringBuilder sb = new StringBuilder();
		Collect(node, sb, 0);
		return sb.ToString();
	}

	private static void Collect(object node, StringBuilder sb, int depth)
	{
		if (node == null || depth > 32)
		{
			return;
		}
		if (node is TextBlock tb)
		{
			sb.Append(tb.Text).Append('\n');
			return;
		}
		if (node is string s)
		{
			sb.Append(s).Append('\n');
			return;
		}
		if (node is ContentControl cc)
		{
			Collect(cc.Content, sb, depth + 1);
			return;
		}
		if (node is Decorator dec)
		{
			Collect(dec.Child, sb, depth + 1);
			return;
		}
		if (node is Panel p)
		{
			foreach (UIElement child in p.Children)
			{
				Collect(child, sb, depth + 1);
			}
			return;
		}
		if (node is DependencyObject dep)
		{
			try
			{
				foreach (object child in LogicalTreeHelper.GetChildren(dep))
				{
					Collect(child, sb, depth + 1);
				}
			}
			catch (Exception)
			{
			}
		}
	}

	private static string RowOf(string raw, string plugin, string kind, string id)
	{
		string[] rows = RowsOf(raw, plugin, kind, id);
		return (rows.Length == 0) ? "" : rows[0];
	}

	/// <summary>所有匹配的 plugins_ui 行(同一 (plugin,kind,id) 出现多行时用来抓重复)。</summary>
	private static string[] RowsOf(string raw, string plugin, string kind, string id)
	{
		List<string> hits = new List<string>();
		using JsonDocument doc = JsonDocument.Parse(raw);
		if (doc.RootElement.ValueKind != JsonValueKind.Array)
		{
			return hits.ToArray();
		}
		foreach (JsonElement row in doc.RootElement.EnumerateArray())
		{
			if (row.ValueKind != JsonValueKind.Object)
			{
				continue;
			}
			if (Str(row, "plugin") == plugin && Str(row, "kind") == kind && Str(row, "id") == id)
			{
				hits.Add(row.GetRawText());
			}
		}
		return hits.ToArray();
	}

	private static string Str(JsonElement row, string name)
	{
		return (row.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String) ? (v.GetString() ?? "") : "";
	}

	private static string Option(string[] args, string name)
	{
		for (int i = 0; i < args.Length - 1; i++)
		{
			if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
			{
				return args[i + 1];
			}
		}
		return null;
	}

	private static string FindRepoRoot()
	{
		DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TipCL.sln")))
		{
			dir = dir.Parent;
		}
		return (dir == null) ? "" : dir.FullName;
	}

	private static string OneLine(string s)
	{
		return (s ?? "").Replace("\r", " ").Replace("\n", " / ").Trim();
	}

	private static void Say(string line)
	{
		Log.AppendLine(line);
		Console.WriteLine(line);
	}

	private static void Flush()
	{
		try
		{
			string path = Option(Environment.GetCommandLineArgs(), "--out");
			if (!string.IsNullOrEmpty(path))
			{
				File.WriteAllText(path, Log.ToString(), new UTF8Encoding(false));
			}
		}
		catch (Exception)
		{
		}
	}
}
