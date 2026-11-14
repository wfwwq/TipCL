// 来源: Windows 版参考仓库 src/TipCL/Core/DefAttribute.cs（M1-A4 搬运，仅改跨平台适配；Win32/DPAPI 分支由 Utils.IS_WINDOWS 运行时守卫）
using System;

namespace TipCL.Core;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class DefAttribute : Attribute
{
	public object Value { get; }

	public DefAttribute(object value)
	{
		Value = ((value is int num) ? ((object)(long)num) : value);
	}
}
