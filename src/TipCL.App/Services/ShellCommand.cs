using System;
using System.Windows.Input;

namespace TipCL.Services;

/// <summary>
/// 外壳命令(C2b 新增的小工具,不属 Windows 移植面)。
///
/// 为什么需要:任务书要求"程序化地触发一条快捷键/菜单项,不许模拟按键"。mac 侧把每个外壳动作
/// 都做成一个 <see cref="ICommand"/>,菜单项挂 <c>Command</c>,测试直接
/// <c>command.Execute(parameter)</c>,键盘路径与菜单路径走的是**同一个** <c>ICommand</c>,
/// 于是"⌘N 与菜单第 N 项指向同一页"这件事是结构上成立的,不是靠对拍字符串。
/// </summary>
internal sealed class ShellCommand : ICommand
{
	private readonly Action<object?> _run;

	private readonly Func<object?, bool>? _can;

	internal ShellCommand(Action run)
		: this(delegate(object? _)
		{
			run();
		})
	{
	}

	internal ShellCommand(Action<object?> run, Func<object?, bool>? can = null)
	{
		_run = run ?? throw new ArgumentNullException(nameof(run));
		_can = can;
	}

	public event EventHandler? CanExecuteChanged;

	public bool CanExecute(object? parameter)
	{
		return _can?.Invoke(parameter) ?? true;
	}

	public void Execute(object? parameter)
	{
		_run(parameter);
	}

	/// <summary>命令的可执行性变了(本包的动作一直是可执行的,留接口给以后带状态的动作)。</summary>
	internal void RaiseCanExecuteChanged()
	{
		CanExecuteChanged?.Invoke(this, EventArgs.Empty);
	}
}
