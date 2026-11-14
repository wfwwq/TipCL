using System.Collections.Generic;
using System.Linq;

namespace TipCL.App.Common;

/// <summary>
/// 批量导入的逐文件结果。三处多选入口(下载页「导入本地」/ 反馈页「添加附件」/
/// 模组对话框「导入模组」)共用同一份汇总口径:
///   * <see cref="Total"/> 是用户选中的文件个数,0 = 取消(调用方什么都不做、也不报错);
///   * 失败条目留「文件名 + 原因」两段,便于汇总里逐条写出是哪个文件失败、为什么;
///   * <see cref="SummaryText"/> **只在多选(Total &gt; 1)时给汇总** —— 单个文件的提示
///     一律沿用改动前的逐字行为,不加汇总(见 _cmp/r18-report.md 任务 A 第 2 节)。
/// </summary>
public sealed class ImportBatch
{
    private readonly List<string> _ok = new();
    private readonly List<ImportFailure> _failures = new();

    public ImportBatch(int total) => Total = total;

    /// <summary>用户选中的文件个数(0 = 取消)。</summary>
    public int Total { get; }

    public int OkCount => _ok.Count;

    public int FailedCount => _failures.Count;

    /// <summary>成功导入的文件名(按处理顺序)。</summary>
    public IReadOnlyList<string> Ok => _ok;

    public IReadOnlyList<ImportFailure> Failures => _failures;

    /// <summary>用户取消(一个文件都没选中)。</summary>
    public bool Canceled => Total <= 0;

    public bool AnyFailed => _failures.Count > 0;

    public void Success(string name) => _ok.Add(name);

    public void Failure(string name, string reason) => _failures.Add(new ImportFailure(name, reason));

    /// <summary>
    /// 可判读的汇总,形如 <c>导入 3 个:成功 2,失败 1\nc.zip:数据包不存在</c>。
    /// 取消(Total == 0)与单个文件(Total == 1)返回空串:这两种情况由调用方按改动前的原样提示。
    /// </summary>
    public string SummaryText()
    {
        if (Total <= 1) return "";
        string head = $"导入 {Total} 个:成功 {OkCount}" + (AnyFailed ? $",失败 {FailedCount}" : "");
        if (!AnyFailed) return head;
        return head + "\n" + string.Join("\n", _failures.Select(f => f.Name + ":" + f.Reason));
    }

    /// <summary>一个失败的文件(文件名 + 原因)。</summary>
    public readonly record struct ImportFailure(string Name, string Reason);
}
