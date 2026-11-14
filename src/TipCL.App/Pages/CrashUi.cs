using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TipCL.Models;
using TipCL.Services;
using TipCL.Themes;

namespace TipCL.Pages;

/// <summary>
/// C3b:Windows 版 <c>Pages\CrashUi.cs</c>(WPF / net48)到 Avalonia 11 / net8 的移植 ——
/// 「崩溃 / 异常退出」对话框。
///
/// 它做一件事:把一份 <see cref="CrashReport"/>(标题 / 摘要 / 退出码 + 提示 / 帮助 / 明细)
/// 排成一张卡片,底下按 <see cref="CrashReport.Actions"/> 生成若干「一键处理」按钮,再加一排固定的
/// 工具按钮(打开日志文件 / 导出报告 / 上传给开发者 / 附说明反馈 / 复制全文),
/// 最后用 <see cref="Dlg.Choose"/> 弹出来,返回「是不是要重新启动」。
/// 调用形状与 Windows 一字不差:<see cref="ShowAsync"/>(<c>LaunchPage:1871</c>、
/// <c>TasksPage:210</c> 都是 <c>if (await CrashUi.ShowAsync(report, gameError))</c> 这么用的)。
///
/// Windows → Avalonia 的差异(逐条):
/// <list type="number">
/// <item><c>TextBox.Style = Ui.S("Input.Log")</c> → <c>StyleKeys.Apply(log, "Input.Log")</c>:
///   Avalonia 没有"按键取样式",C1b 把每个 WPF 样式键翻译成类名,<c>Ui.S</c> 返回的就是那个类名;</item>
/// <item><c>Button.ToolTip = ...</c> → <c>UiExt.Tip(...)</c>(Avalonia 11 的 ToolTip 是挂载属性
///   <c>ToolTip.SetTip</c>,<c>Tip</c> 就是它的一行包装);</item>
/// <item><c>Clipboard.SetText</c> → <see cref="CopyAsync"/>:WPF 有静态剪贴板入口,Avalonia 11 **没有**,
///   剪贴板挂在 TopLevel 上、而且要走 async。所以这里留了一个可注入挂点
///   <see cref="ClipboardWriter"/>,测试与离屏模式换掉它,**绝不碰真剪贴板**;</item>
/// <item><c>new AiFixWindow(report).Show()</c> → <see cref="AiFixLauncher"/>:那个窗口(C8b 的整窗,
///   <c>Pages\AiFixWindow.cs</c>)**已经移植**,正常启动路径由 <see cref="TipCL.Pages.AiFixLauncher"/>
///   的 <c>[ModuleInitializer]</c> 把这个缝接上(接线形状与 Windows 那句一字不差)。保留缝是为了测试与离屏能换掉它;
///   缝没接时**如实提示**而不是静默空操作 —— 这是**防御性兜底**,不再是"窗口还没移植"的降级
///   (D-118 / D-119 记的那条降级已因此关闭);</item>
/// <item><c>UIElement</c> / <c>FrameworkElement</c> → <c>Control</c>;<c>Visibility</c> 在 Avalonia 11 是
///   <c>IsVisible</c>(本文件没用到);<c>Thickness</c> 换成 <c>Avalonia.Thickness</c>。</item>
/// </list>
///
/// 忠实保留的一处 Windows 写法:<c>report.Actions ?? new List&lt;CrashAction&gt;()</c> —— 动作列表为空时
/// 卡片里照样有一个空的 <c>WrapPanel</c>(它高度 0,视觉无差),没有"顺手"去掉。
/// </summary>
public static class CrashUi
{
	/// <summary>
	/// 写剪贴板的注入缝。为 <c>null</c> 时走 Avalonia 的平台路径(<see cref="PlatformClipboardWrite"/>);
	/// 测试与离屏(<c>--smoke</c>)替掉它 —— 与 <see cref="Dlg"/> 的
	/// <c>SmokeAutoAnswer</c> / <c>SmokeAutoCancelSystemDialog</c> 同一套口径。
	/// 签名里的 <see cref="Control"/> 是"从哪个控件往上找 TopLevel"。
	/// </summary>
	internal static Func<Control, string, Task<bool>>? ClipboardWriter { get; set; }

	/// <summary>
	/// 「交给 AI 修复」的注入缝。Windows 是 <c>new AiFixWindow(report).Show()</c>;
	/// mac 的 <c>AiFixWindow</c> 已经移植(<c>Pages\AiFixWindow.cs</c>,C8b),并由
	/// <see cref="TipCL.Pages.AiFixLauncher"/> 的 <c>[ModuleInitializer]</c> 在模块加载时接上。
	/// 保留这个缝是为了测试与离屏能换掉它;<b>为 <c>null</c> 时点击仍如实提示</b>(防御性兜底,不静默吞掉)。
	/// </summary>
	internal static Action<CrashReport>? AiFixLauncher { get; set; }

	/// <summary>
	/// 平台剪贴板路径被**真的走到**的次数(只增不减)。
	/// 留给测试用:接上 <see cref="ClipboardWriter"/> 时这个计数必须一动不动 ——
	/// 这就是"测试绝不碰真剪贴板"那条红线的机器化证明(口径同 C3a 的只读快照)。
	/// </summary>
	internal static int ClipboardPlatformAttempts { get; private set; }

	/// <summary>
	/// 弹出崩溃报告卡片,返回"是不是点了重新启动"。
	/// 【Windows 源】<c>CrashUi.cs:13-148</c>:<c>Dlg.Choose(...) == 1</c>,选项顺序 = 关闭(0)/ 重新启动(1)。
	/// </summary>
	/// <param name="report">崩溃报告(后端给的那份 DTO)。</param>
	/// <param name="gameError">是不是"进游戏时出的错"(为 true 时多一个「交给 AI 修复」主按钮)。</param>
	public static async Task<bool> ShowAsync(CrashReport report, bool gameError = false)
	{
		BridgeClient api = AppServices.Client;
		string head = HeadOf(report);
		SPanel panel = Ui.V(12.0);
		panel.MinWidth = 520.0;
		panel.Children.Add(Ui.Txt(head, 15.0, bold: true).Wrap());
		if (!string.IsNullOrWhiteSpace(report.Summary))
		{
			panel.Children.Add(Ui.Muted(report.Summary));
		}

		int? exitCode = report.ExitCode;
		if (exitCode.HasValue)
		{
			int code = exitCode.GetValueOrDefault();
			string hint = string.IsNullOrWhiteSpace(report.ExitHint) ? "" : (" · " + report.ExitHint);
			panel.Children.Add(Ui.Small(I18n.L("退出码 {0}{1}", code, hint)));
		}

		if (!string.IsNullOrWhiteSpace(report.Help))
		{
			panel.Children.Add(Ui.Muted(report.Help));
		}

		if (!string.IsNullOrWhiteSpace(report.Detail))
		{
			// WPF:Style = Ui.S("Input.Log")。Avalonia 靠类名命中选择器(C1b 的 StyleKeys)。
			TextBox log = new TextBox
			{
				Text = report.Detail,
				Height = 190.0,
				TextWrapping = TextWrapping.Wrap,
			};
			StyleKeys.Apply(log, "Input.Log");
			panel.Children.Add(log);
		}

		WrapPanel actionRow = new WrapPanel();
		panel.Children.Add(actionRow);
		foreach (CrashAction action in report.Actions ?? new List<CrashAction>())
		{
			Button b = Ui.Btn(action.Label, BtnKind.Soft);
			b.Margin = new Thickness(0.0, 0.0, 7.0, 7.0);
			b.Click += delegate
			{
				PageBase.Run(async delegate
				{
					b.IsEnabled = false;
					try
					{
						OpResult? result = await api.CallAsync<OpResult>("apply_crash_action", new
						{
							action = action,
							report = report,
						});
						AppServices.Toast(
							(result != null && result.Ok) ? I18n.L("已处理") : I18n.L("没能处理"),
							result?.Message ?? "",
							(result != null && result.Ok) ? ToastKind.Success : ToastKind.Warning);
					}
					finally
					{
						b.IsEnabled = true;
					}
				}, I18n.L("处理失败"));
			};
			actionRow.Children.Add(b);
		}

		if (gameError)
		{
			Button aiFix = Ui.Btn(I18n.L("交给 AI 修复"), BtnKind.Primary, null, "\ue99a");
			aiFix.Margin = new Thickness(0.0, 0.0, 7.0, 7.0);
			aiFix.Tip(I18n.L("把这份报错直接交给 AI 助手诊断和修复"));
			aiFix.Click += delegate
			{
				Action<CrashReport>? launch = AiFixLauncher;
				if (launch != null)
				{
					launch(report);
					return;
				}

				// 兜底分支(防御性,不再是"未移植"的降级):正常启动路径上 AiFixLauncher 的
				// [ModuleInitializer] 已经把缝接好,走到这里只可能是缝被显式置空(测试 / 离屏)。
				// 口径照旧:如实提示,不做静默空操作。下面那句 Toast 保留作兜底。
				AppServices.Toast(I18n.L("暂未实现"), I18n.L("AI 修复窗口尚未移植"), ToastKind.Info);
			};
			actionRow.Children.Add(aiFix);
		}

		Extra(I18n.L("打开日志文件"), "\ue8a7", async delegate
		{
			string opened = await api.CallAsync<string>("open_crash_file", new
			{
				task_id = report.TaskId,
				path = report.DirectFile,
			});
			AppServices.Toast(I18n.L("已打开"), opened ?? "", ToastKind.Success);
		});
		Extra(I18n.L("导出报告"), "\uede1", async delegate
		{
			string? dest = Dlg.SaveFile(I18n.L("文本 (*.txt)|*.txt"), "tipcl-crash.txt", I18n.L("导出崩溃报告"));
			if (dest != null)
			{
				string exported = await api.CallAsync<string>("export_crash_report", new
				{
					task_id = report.TaskId,
					dest = dest,
				});
				AppServices.Toast(I18n.L("已导出"), exported ?? dest, ToastKind.Success);
			}
		});
		Extra(I18n.L("上传给开发者"), "\ue724", async delegate
		{
			string uploaded = await api.CallAsync<string>("submit_crash_report", new
			{
				task_id = report.TaskId,
			});
			AppServices.Toast(I18n.L("已上传"), uploaded ?? "", ToastKind.Success);
		});
		Extra(I18n.L("附说明反馈"), "\ue8bd", async delegate
		{
			TextBox note = Ui.Multi(I18n.L("崩溃前在做什么?装了什么模组?(可空)"), "", 110.0);
			bool send = await Dlg.Ask(
				I18n.L("反馈这次崩溃"),
				Ui.V(8.0, Ui.Muted(I18n.L("会把崩溃报告连同下面这段说明发给开发者,不含账号密码。")), note),
				I18n.L("发送"));
			if (send)
			{
				OpResult? result = await api.CallAsync<OpResult>("submit_crash_feedback", new
				{
					report = report,
					extra = note.Text?.Trim() ?? "",
				});
				AppServices.Toast(
					(result != null && result.Ok) ? I18n.L("已反馈") : I18n.L("反馈失败"),
					result?.Message ?? "",
					(result != null && result.Ok) ? ToastKind.Success : ToastKind.Error);
			}
		});
		Extra(I18n.L("复制全文"), "\ue8c8", async delegate
		{
			if (await CopyAsync(panel, CopyText(head, report)))
			{
				AppServices.Toast(I18n.L("已复制"));
			}
		});

		return await Dlg.Choose(I18n.L("启动出问题了"), panel, new string[2]
		{
			I18n.L("关闭"),
			I18n.L("重新启动"),
		}, 760.0) == 1;

		void Extra(string text, string glyph, Func<Task> work)
		{
			Button chip = Ui.Btn(text, BtnKind.Chip, null, glyph);
			chip.Margin = new Thickness(0.0, 0.0, 7.0, 7.0);
			chip.Click += delegate
			{
				PageBase.Run(work, I18n.L("失败"));
			};
			actionRow.Children.Add(chip);
		}
	}

	/// <summary>
	/// 卡片与"复制全文"共用的标题:headline 优先,退回 title,都空就「游戏异常退出」。
	/// 【Windows 源】<c>CrashUi.cs:16-20</c>(从 <see cref="ShowAsync"/> 里抽出来的纯函数,逻辑一字未改)。
	/// </summary>
	internal static string HeadOf(CrashReport report)
	{
		string head = string.IsNullOrWhiteSpace(report.Headline) ? report.Title : report.Headline;
		if (string.IsNullOrWhiteSpace(head))
		{
			head = I18n.L("游戏异常退出");
		}

		return head;
	}

	/// <summary>
	/// 「复制全文」写进剪贴板的那段文本。
	/// 【Windows 源】<c>CrashUi.cs:136</c>:<c>head + "\n" + report.Summary + "\n\n" + report.Detail</c>
	/// —— 三段之间的空行是**一个**换行(标题/摘要)加**两个**换行(摘要/明细),逐字保留。
	/// </summary>
	internal static string CopyText(string head, CrashReport report)
	{
		return head + "\n" + report.Summary + "\n\n" + report.Detail;
	}

	/// <summary>
	/// 写剪贴板:优先走注入缝,否则走 Avalonia 的平台路径。
	/// 返回 <c>false</c> 表示"没写进去"——调用方据此决定要不要提示「已复制」,
	/// 与 Windows 的 <c>try { Clipboard.SetText(...); Toast(已复制); } catch { }</c> 等价(失败就当没发生)。
	/// </summary>
	internal static async Task<bool> CopyAsync(Control owner, string text)
	{
		Func<Control, string, Task<bool>>? sink = ClipboardWriter;
		try
		{
			if (sink != null)
			{
				return await sink(owner, text);
			}

			// 离屏(--smoke)不碰真剪贴板:与 Dlg 在 SmokeActive 下不弹真文件选择器同一口径。
			if (Dlg.SmokeActive)
			{
				return false;
			}

			return await PlatformClipboardWrite(owner, text);
		}
		catch (Exception)
		{
			// Windows 源是一个空 catch:复制失败不许崩对话框,也不许弹错误。
			return false;
		}
	}

	/// <summary>
	/// Avalonia 的平台剪贴板:WPF 的静态 <c>Clipboard.SetText</c> 在 Avalonia 11 不存在,
	/// 剪贴板挂在 <see cref="TopLevel"/> 上,且只有 async 入口。
	/// 取不到 TopLevel / 剪贴板实现时返回 <c>false</c>(离屏与无头环境就是这一支)。
	/// </summary>
	private static async Task<bool> PlatformClipboardWrite(Control owner, string text)
	{
		ClipboardPlatformAttempts++;
		TopLevel? top = TopLevel.GetTopLevel(owner);
		if (top?.Clipboard == null)
		{
			return false;
		}

		await top.Clipboard.SetTextAsync(text);
		return true;
	}
}