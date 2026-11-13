using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using TipCL.Services;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// C2a 的临时目录:一律落在 <see cref="TestHome.Root"/> 之下(红线:测试临时文件不许出仓库)。
/// 用完在 finally 里删掉。
/// </summary>
internal sealed class C2aTempDir : IDisposable
{
    internal C2aTempDir(string tag)
    {
        Path = System.IO.Path.Combine(TestHome.Root, "c2a-" + tag + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    internal string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception)
        {
        }
    }

    internal string File(string name)
    {
        return System.IO.Path.Combine(Path, name);
    }

    internal string Dir(string name)
    {
        string dir = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>写一个只有 24 字节头的 PNG(签名 + IHDR 的宽高)。<c>FileKinds.PngSize</c> 只读这 24 字节。</summary>
    internal string WritePngHeader(string name, int width, int height)
    {
        byte[] bytes = new byte[24];
        byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        Array.Copy(signature, bytes, 8);
        bytes[11] = 13;
        bytes[12] = 73;
        bytes[13] = 72;
        bytes[14] = 68;
        bytes[15] = 82;
        BigEndian(bytes, 16, width);
        BigEndian(bytes, 20, height);
        string path = File(name);
        System.IO.File.WriteAllBytes(path, bytes);
        return path;
    }

    internal string WriteBytes(string name, byte[] content)
    {
        string path = File(name);
        System.IO.File.WriteAllBytes(path, content);
        return path;
    }

    internal string WriteZip(string name, params string[] entries)
    {
        string path = File(name);
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (string entry in entries)
        {
            using StreamWriter writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write("x");
        }

        return path;
    }

    internal string WriteZipText(string name, string entry, string content)
    {
        string path = File(name);
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using StreamWriter writer = new StreamWriter(zip.CreateEntry(entry).Open());
        writer.Write(content);
        return path;
    }

    internal string WriteText(string name, string content)
    {
        string path = File(name);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    private static void BigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}

/// <summary>
/// C2a:<see cref="TaskStore"/> / <see cref="TaskRow"/> 的生命周期与行投影。
///
/// 期望值来自 Windows 版 <c>src\TipCL\Services\TaskStore.cs</c>(C2a 字节级复制)与
/// <c>TaskRow.cs</c>;写死的数字(60 / 600 / 400)都在源文件里能逐条对上。
///
/// 纪律:<see cref="TaskStore"/> 是全局静态表,所以
/// 1. 本类与其它碰全局的 C2a 用例同属一个 collection(不并发);
/// 2. 每条用例在 finally 里**退订事件 + 清空所有行**,并断言表的行数回到了进入时的值。
/// </summary>
[Collection("C2aGlobals")]
public class C2aTaskStoreTests
{
    private const string Added = "task_added";

    private const string Progress = "progress";

    private const string Log = "log";

    private const string Finished = "finished";

    /// <summary>进入/退出时的表格清账:把当前所有行标成完成再清掉,并断言表是空的。</summary>
    private static int ClearAll()
    {
        int count = TaskStore.Rows.Count;
        foreach (TaskRow row in TaskStore.Rows.ToList())
        {
            row.Finished = true;
        }

        TaskStore.ClearFinished();
        return count;
    }

    [Fact]
    public void Task_added_creates_a_row_and_raises_added_once()
    {
        int before = ClearAll();
        List<TaskRow> added = new List<TaskRow>();
        Action<TaskRow> onAdded = added.Add;
        TaskStore.Added += onAdded;
        try
        {
            TaskStore.Handle(new BridgeEvent { Event = Added, TaskId = "t1", Title = "my task" });
            TaskRow row = Assert.Single(TaskStore.Rows);
            Assert.Equal("t1", row.Id);
            Assert.Equal("my task", row.Title);
            Assert.Single(added);
            Assert.Same(row, added[0]);

            // Win 源:同一个 TaskId 不会建第二行。
            TaskStore.Handle(new BridgeEvent { Event = Added, TaskId = "t1", Title = "again" });
            Assert.Single(TaskStore.Rows);
            Assert.Single(added);
        }
        finally
        {
            TaskStore.Added -= onAdded;
            ClearAll();
            Assert.Equal(before, TaskStore.Rows.Count);
        }
    }

    [Fact]
    public void Task_added_falls_back_to_the_placeholder_title_and_ignores_an_empty_id()
    {
        int before = ClearAll();
        try
        {
            TaskStore.Handle(new BridgeEvent { Event = Added, TaskId = "t2", Title = "" });
            TaskRow row = Assert.Single(TaskStore.Rows);
            Assert.Equal(TaskStore.PlaceholderTitle, row.Title);

            TaskStore.Handle(new BridgeEvent { Event = Added, TaskId = "", Title = "no id" });
            Assert.Single(TaskStore.Rows);
        }
        finally
        {
            ClearAll();
            Assert.Equal(before, TaskStore.Rows.Count);
        }
    }

    [Fact]
    public void Newly_added_row_starts_queued_and_indeterminate()
    {
        // TaskRow 的默认值(Windows 源):Status = I18n.L("排队中..."),Indeterminate = true,
        // Progress = 0,Finished/Success = false,LogLines = 0,Started 取当天。
        int before = ClearAll();
        try
        {
            TaskStore.Handle(new BridgeEvent { Event = Added, TaskId = "t3", Title = "x" });
            TaskRow row = TaskStore.Get("t3")!;
            // 注意:键里的省略号是 U+2026(源文件实测),不是三个 ASCII 点 —— D-58 那类坑。
            Assert.Equal(I18n.L("\u6392\u961f\u4e2d\u2026"), row.Status);
            Assert.True(row.Indeterminate);
            Assert.False(row.Finished);
            Assert.False(row.Success);
            Assert.Equal(0.0, row.Progress);
            Assert.Equal("", row.Speed);
            Assert.Equal(0, row.LogLines);
            Assert.Equal(DateTime.Now.Date, row.Started.Date);
        }
        finally
        {
            ClearAll();
            Assert.Equal(before, TaskStore.Rows.Count);
        }
    }

    [Fact]
    public void Progress_updates_status_speed_and_percentage()
    {
        int before = ClearAll();
        List<TaskRow> updated = new List<TaskRow>();
        Action<TaskRow> onUpdated = updated.Add;
        TaskStore.Updated += onUpdated;
        try
        {
            TaskStore.Handle(new BridgeEvent { Event = Added, TaskId = "p1", Title = "x" });
            TaskStore.Handle(new BridgeEvent
            {
                Event = Progress,
                TaskId = "p1",
                Message = "downloading  |  1.2 MB/s",
                Current = 3,
                Total = 12
            });
            TaskRow row = TaskStore.Get("p1")!;
            Assert.Equal("downloading", row.Status);
            Assert.Equal("1.2 MB/s", row.Speed);
            Assert.Equal(25.0, row.Progress);
            Assert.False(row.Indeterminate);
            Assert.Single(updated);
        }
        finally
        {
            TaskStore.Updated -= onUpdated;
            ClearAll();
            Assert.Equal(before, TaskStore.Rows.Count);
        }
    }

    [Fact]
    public void Progress_without_total_stays_indeterminate_and_keeps_progress()
    {
        int before = ClearAll();
        try
        {
            TaskStore.Handle(new BridgeEvent { Event = Added, TaskId = "p2", Title = "x" });
            TaskStore.Handle(new BridgeEvent { Event = Progress, TaskId = "p2", Message = "working", Current = 5, Total = 0 });
            TaskRow row = TaskStore.Get("p2")!;
            Assert.True(row.Indeterminate);
            Assert.Equal(0.0, row.Progress);
            // Win 源:status 为空时保留原状态;message 有内容时按 SplitMsg 拆出 status。
            Assert.Equal("working", row.Status);
        }
        finally
        {
            ClearAll();
            Assert.Equal(before, TaskStore.Rows.Count);
        }
    }

    [Fact]
    public void Progress_is_clamped_into_zero_to_hundred()
    {
        int before = ClearAll();
        try
        {
            TaskStore.Handle(new BridgeEvent { Event = Added, TaskId = "p3", Title = "x" });
            TaskStore.Handle(new BridgeEvent { Event = Progress, TaskId = "p3", Current = 30, Total = 10 });
            Assert.Equal(100.0, TaskStore.Get("p3")!.Progress);
            TaskStore.Handle(new BridgeEvent { Event = Progress, TaskId = "p3", Current = -5, Total = 10 });
            Assert.Equal(0.0, TaskStore.Get("p3")!.Progress);
        }
        finally
        {
            ClearAll();
            Assert.Equal(before, TaskStore.Rows.Count);
        }
    }

    [Fact]
    public void Events_for_an_unknown_task_id_change_nothing()
    {
        int before = ClearAll();
        List<object> seen = new List<object>();
        Action<TaskRow> onUpdated = row => seen.Add(row);
        TaskStore.Updated += onUpdated;
        try
        {
            TaskStore.Handle(new BridgeEvent { Event = Progress, TaskId = "nope", Current = 1, Total = 2 });
            TaskStore.Handle(new BridgeEvent { Event = Log, TaskId = "nope", Text = "line" });
            TaskStore.Handle(new BridgeEvent { Event = Finished, TaskId = "nope", Success = true });
            Assert.Empty(TaskStore.Rows);
            Assert.Empty(seen);
        }
        finally
        {
            TaskStore.Updated -= onUpdated;
            ClearAll();
            Assert.Equal(before, TaskStore.Rows.Count);
        }
    }

    [Fact]
    public void Log_appends_lines_and_counts_them()
    {
        int before = ClearAll();
        try
        {
            TaskStore.Handle(new BridgeEvent { Event = Added, TaskId = "l1", Title = "x" });
            TaskStore.Handle(new BridgeEvent { Event = Log, TaskId = "l1", Text = "first" });
            TaskStore.Handle(new BridgeEvent { Event = Log, TaskId = "l1", Text = "second" });
            // Win 源:Append 忽略空行。
            TaskStore.Handle(new BridgeEvent { Event = Log, TaskId = "l1", Text = "" });
            TaskRow row = TaskStore.Get("l1")!;
            Assert.Equal(2, row.LogLines);
            Assert.Equal("first" + Environment.NewLine + "second" + Environment.NewLine, row.Log.ToString());
        }
        finally
        {
            ClearAll();
            Assert.Equal(before, TaskStore.Rows.Count);
        }
    }

    [Fact]
    public void Finished_success_sets_full_progress_and_the_done_status()
    {
        int before = ClearAll();
        try
        {
            TaskStore.Handle(new BridgeEvent { Event = Added, TaskId = "f1", Title = "x" });
            TaskStore.Handle(new BridgeEvent { Event = Progress, TaskId = "f1", Message = "half", Current = 5, Total = 10 });
            TaskStore.Handle(new BridgeEvent { Event = Finished, TaskId = "f1", Success = true });
            TaskRow row = TaskStore.Get("f1")!;
            Assert.True(row.Finished);
            Assert.True(row.Success);
            Assert.False(row.Indeterminate);
            Assert.Equal(100.0, row.Progress);
            Assert.Equal("", row.Speed);
            Assert.Equal(I18n.L("\u5df2\u5b8c\u6210"), row.Status);
        }
        finally
        {
            ClearAll();
            Assert.Equal(before, TaskStore.Rows.Count);
        }
    }

    [Fact]
    public void Finished_failure_keeps_the_progress_and_uses_the_message_when_given()
    {
        int before = ClearAll();
        try
        {
            TaskStore.Handle(new BridgeEvent { Event = Added, TaskId = "f2", Title = "x" });
            TaskStore.Handle(new BridgeEvent { Event = Progress, TaskId = "f2", Current = 4, Total = 10 });
            TaskStore.Handle(new BridgeEvent { Event = Finished, TaskId = "f2", Success = false });
            TaskRow row = TaskStore.Get("f2")!;
            Assert.True(row.Finished);
            Assert.False(row.Success);
            Assert.Equal(40.0, row.Progress);
            Assert.Equal(I18n.L("\u5931\u8d25"), row.Status);

            TaskStore.Handle(new BridgeEvent { Event = Added, TaskId = "f3", Title = "x" });
            TaskStore.Handle(new BridgeEvent { Event = Finished, TaskId = "f3", Success = false, Message = "boom" });
            Assert.Equal("boom", TaskStore.Get("f3")!.Status);
        }
        finally
        {
            ClearAll();
            Assert.Equal(before, TaskStore.Rows.Count);
        }
    }

    [Fact]
    public void Unknown_event_name_is_ignored()
    {
        int before = ClearAll();
        try
        {
            TaskStore.Handle(new BridgeEvent { Event = Added, TaskId = "u1", Title = "x" });
            TaskStore.Handle(new BridgeEvent { Event = "something_else", TaskId = "u1", Success = true });
            TaskRow row = TaskStore.Get("u1")!;
            Assert.False(row.Finished);
        }
        finally
        {
            ClearAll();
            Assert.Equal(before, TaskStore.Rows.Count);
        }
    }

    [Fact]
    public void Running_count_and_newest_prefer_the_last_unfinished_row()
    {
        int before = ClearAll();
        try
        {
            TaskStore.Handle(new BridgeEvent { Event = Added, TaskId = "r1", Title = "a" });
            TaskStore.Handle(new BridgeEvent { Event = Added, TaskId = "r2", Title = "b" });
            Assert.Equal(2, TaskStore.RunningCount);
            Assert.Equal("r2", TaskStore.Newest!.Id);

            TaskStore.Handle(new BridgeEvent { Event = Finished, TaskId = "r2", Success = true });
            Assert.Equal(1, TaskStore.RunningCount);
            Assert.Equal("r1", TaskStore.Newest!.Id);

            TaskStore.Handle(new BridgeEvent { Event = Finished, TaskId = "r1", Success = true });
            Assert.Equal(0, TaskStore.RunningCount);
            // Win 源:没有未完成的行时 Newest 退化成**最后一行**(此时最后一行是后加的 r2)。
            Assert.Equal("r2", TaskStore.Newest!.Id);
        }
        finally
        {
            ClearAll();
            Assert.Equal(before, TaskStore.Rows.Count);
        }
    }

    [Fact]
    public void Clear_finished_keeps_running_rows_and_raises_cleared()
    {
        int before = ClearAll();
        int cleared = 0;
        Action onCleared = () => cleared++;
        TaskStore.Cleared += onCleared;
        try
        {
            TaskStore.Handle(new BridgeEvent { Event = Added, TaskId = "c1", Title = "a" });
            TaskStore.Handle(new BridgeEvent { Event = Added, TaskId = "c2", Title = "b" });
            TaskStore.Handle(new BridgeEvent { Event = Finished, TaskId = "c1", Success = true });
            TaskStore.ClearFinished();
            TaskRow row = Assert.Single(TaskStore.Rows);
            Assert.Equal("c2", row.Id);
            Assert.Equal(1, cleared);
        }
        finally
        {
            TaskStore.Cleared -= onCleared;
            ClearAll();
            Assert.Equal(before, TaskStore.Rows.Count);
        }
    }

    [Fact]
    public void Remove_only_removes_finished_rows()
    {
        int before = ClearAll();
        try
        {
            TaskStore.Handle(new BridgeEvent { Event = Added, TaskId = "d1", Title = "a" });
            Assert.False(TaskStore.Remove("d1"));
            Assert.False(TaskStore.Remove("missing"));
            TaskStore.Handle(new BridgeEvent { Event = Finished, TaskId = "d1", Success = true });
            Assert.True(TaskStore.Remove("d1"));
            Assert.Empty(TaskStore.Rows);
            Assert.Null(TaskStore.Get("d1"));
        }
        finally
        {
            ClearAll();
            Assert.Equal(before, TaskStore.Rows.Count);
        }
    }

    [Fact]
    public void Reconcile_adds_running_rows_and_reports_a_change()
    {
        int before = ClearAll();
        try
        {
            bool changed = TaskStore.Reconcile(
                new[] { ("rec1", "title 1"), ("", "ignored") },
                Array.Empty<(string, bool, string)>());
            Assert.True(changed);
            TaskRow row = Assert.Single(TaskStore.Rows);
            Assert.Equal("rec1", row.Id);
            Assert.Equal("title 1", row.Title);
            Assert.False(row.Finished);

            // Win 源:已经在这个状态上的行不算变化(返回 false)。
            bool again = TaskStore.Reconcile(Array.Empty<(string, string)>(), new[] { ("rec1", false, "") });
            Assert.True(again);
            Assert.True(TaskStore.Get("rec1")!.Finished);
            Assert.Equal(I18n.L("\u5931\u8d25"), TaskStore.Get("rec1")!.Status);

            bool idempotent = TaskStore.Reconcile(Array.Empty<(string, string)>(), new[] { ("rec1", false, "") });
            Assert.False(idempotent);
        }
        finally
        {
            ClearAll();
            Assert.Equal(before, TaskStore.Rows.Count);
        }
    }

    [Fact]
    public void Reconcile_creates_a_row_for_an_unknown_finished_task()
    {
        int before = ClearAll();
        Action<TaskRow> noop = _ => { };
        TaskStore.Added += noop;
        try
        {
            bool changed = TaskStore.Reconcile(Array.Empty<(string, string)>(), new[] { ("ghost", true, "done") });
            Assert.True(changed);
            TaskRow row = TaskStore.Get("ghost")!;
            Assert.Equal(TaskStore.PlaceholderTitle, row.Title);
            Assert.True(row.Finished);
            Assert.True(row.Success);
            Assert.Equal("done", row.Status);
            Assert.Equal(100.0, row.Progress);
        }
        finally
        {
            TaskStore.Added -= noop;
            ClearAll();
            Assert.Equal(before, TaskStore.Rows.Count);
        }
    }

    [Fact]
    public void Table_is_capped_at_sixty_rows()
    {
        // Win 源:task_added 之后若行数 > 60,从最前面裁掉多余的。
        int before = ClearAll();
        try
        {
            for (int i = 0; i < 65; i++)
            {
                TaskStore.Handle(new BridgeEvent { Event = Added, TaskId = "cap" + i, Title = "t" + i });
            }

            Assert.Equal(60, TaskStore.Rows.Count);
            Assert.Null(TaskStore.Get("cap0"));
            Assert.NotNull(TaskStore.Get("cap64"));
        }
        finally
        {
            ClearAll();
            Assert.Equal(before, TaskStore.Rows.Count);
        }
    }

    [Fact]
    public void Row_log_is_trimmed_from_six_hundred_lines_down_to_four_hundred()
    {
        // Win 源:Append 里超过 600 行就从 1/3 处的换行之后截断,并把 LogLines 记成 400。
        TaskRow row = new TaskRow { Id = "row" };
        for (int i = 0; i < 600; i++)
        {
            row.Append("line " + i);
        }

        Assert.Equal(600, row.LogLines);
        row.Append("the 601st line");
        Assert.Equal(400, row.LogLines);
        string text = row.Log.ToString();
        Assert.EndsWith("the 601st line" + Environment.NewLine, text, StringComparison.Ordinal);
        Assert.Contains("line 599", text, StringComparison.Ordinal);
        Assert.DoesNotContain("line 0" + Environment.NewLine, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Row_append_ignores_null_and_empty_lines()
    {
        TaskRow row = new TaskRow { Id = "row" };
        row.Append("");
        row.Append(null!);
        Assert.Equal(0, row.LogLines);
        Assert.Equal("", row.Log.ToString());
    }
}

/// <summary>
/// C2a:<see cref="FileKinds"/> 的扩展名映射、识别结果与**优先级顺序**。
///
/// 期望值来自 Windows 版 <c>src\TipCL\Services\FileKinds.cs</c>(C2a 字节级复制):
/// 常量数组、<c>Identify</c> 的分支顺序、<c>IdentifyArchive</c> 的判定顺序都在源文件里逐条可查。
/// 断言尽量落在 ASCII 标志串(Kinds / Sure / 扩展名 / 标志文件名)上 —— Detail 的中文前缀不钉死,
/// 只验它确实带上了那条分支的 ASCII 标志。
///
/// 临时文件全部落在 <see cref="TestHome.Root"/> 之下并在 finally 里删除。
/// </summary>
public class C2aFileKindsTests
{
    [Fact]
    public void All_kinds_and_suffix_arrays_match_the_source_order()
    {
        Assert.Equal(
            new[] { "modpack", "mod", "resourcepack", "shaderpack", "datapack", "world", "skin", "wallpaper", "plugin" },
            FileKinds.AllKinds);
        Assert.Equal(new[] { ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv" }, FileKinds.VideoSuffixes);
        Assert.Equal(new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif" }, FileKinds.ImageSuffixes);
        Assert.Equal("modpack", FileKinds.Modpack);
        Assert.Equal("mod", FileKinds.Mod);
        Assert.Equal("resourcepack", FileKinds.ResourcePack);
        Assert.Equal("shaderpack", FileKinds.ShaderPack);
        Assert.Equal("datapack", FileKinds.DataPack);
        Assert.Equal("world", FileKinds.World);
        Assert.Equal("skin", FileKinds.Skin);
        Assert.Equal("wallpaper", FileKinds.Wallpaper);
    }

    [Theory]
    [InlineData("a.mp4", true)]
    [InlineData("a.MKV", true)]
    [InlineData("a.webm", true)]
    [InlineData("a.avi", true)]
    [InlineData("a.wmv", true)]
    [InlineData("a.m4v", true)]
    [InlineData("a.mov", true)]
    [InlineData("a.png", false)]
    [InlineData("a", false)]
    [InlineData("", false)]
    public void IsVideo_matches_the_video_suffix_table_case_insensitively(string path, bool expected)
    {
        Assert.Equal(expected, FileKinds.IsVideo(path));
    }

    [Theory]
    [InlineData("a.png", true)]
    [InlineData("a.JPG", true)]
    [InlineData("a.jpeg", true)]
    [InlineData("a.bmp", true)]
    [InlineData("a.webp", true)]
    [InlineData("a.gif", true)]
    [InlineData("a.mp4", false)]
    [InlineData("a.txt", false)]
    public void IsImage_matches_the_image_suffix_table_case_insensitively(string path, bool expected)
    {
        Assert.Equal(expected, FileKinds.IsImage(path));
    }

    [Fact]
    public void IsWallpaper_accepts_images_and_videos_only()
    {
        Assert.True(FileKinds.IsWallpaper("a.mp4"));
        Assert.True(FileKinds.IsWallpaper("a.PNG"));
        Assert.False(FileKinds.IsWallpaper("a.txt"));
        Assert.False(FileKinds.IsWallpaper(""));
    }

    [Fact]
    public void Label_and_action_come_from_the_source_tables_with_a_fallback()
    {
        // Win 源:Labels/Actions 两张表在静态构造时按 I18n.L(...) 取值(所以与语言一致)。
        Assert.Equal(I18n.L("\u6574\u5408\u5305"), FileKinds.Label("modpack"));
        Assert.Equal(I18n.L("\u6a21\u7ec4"), FileKinds.Label("mod"));
        Assert.Equal(I18n.L("TipCL \u63d2\u4ef6"), FileKinds.Label("plugin"));
        Assert.Equal(I18n.L("\u88c5\u6210\u4e00\u4e2a\u65b0\u7248\u672c"), FileKinds.Action("modpack"));

        // 认不出的 kind:Label 返回 kind 本身,Action 返回空串(都不抛)。
        Assert.Equal("mystery", FileKinds.Label("mystery"));
        Assert.Equal("", FileKinds.Action("mystery"));
    }

    [Fact]
    public void Identify_of_a_missing_path_is_unsure_and_has_no_kinds()
    {
        using var temp = new C2aTempDir("missing");
        FileKinds.Verdict verdict = FileKinds.Identify(temp.File("nope.bin"));
        Assert.Empty(verdict.Kinds);
        Assert.False(verdict.Sure);
        Assert.Equal(I18n.L("\u8fd9\u4e2a\u8def\u5f84\u4e0d\u5b58\u5728"), verdict.Detail);
        Assert.Equal(verdict.Path, temp.File("nope.bin"));
        Assert.Equal("nope.bin", verdict.Name);
        Assert.Equal("", verdict.KindSig);
    }

    [Fact]
    public void Identify_of_a_video_file_is_a_confident_wallpaper()
    {
        using var temp = new C2aTempDir("video");
        string path = temp.WriteText("clip.mp4", "not really a video");
        FileKinds.Verdict verdict = FileKinds.Identify(path);
        Assert.Equal(new[] { "wallpaper" }, verdict.Kinds);
        Assert.True(verdict.Sure);
        Assert.Equal("wallpaper", verdict.KindSig);
    }

    [Theory]
    [InlineData(64, 64)]
    [InlineData(64, 32)]
    public void Identify_of_a_skin_sized_png_is_both_skin_and_wallpaper_without_certainty(int width, int height)
    {
        using var temp = new C2aTempDir("skin");
        string path = temp.WritePngHeader("skin.png", width, height);
        FileKinds.Verdict verdict = FileKinds.Identify(path);
        Assert.Equal(new[] { "skin", "wallpaper" }, verdict.Kinds);
        Assert.False(verdict.Sure);
        Assert.Contains("PNG", verdict.Detail, StringComparison.Ordinal);
        Assert.Contains(width + "x" + height, verdict.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(32, 32)]
    [InlineData(512, 512)]
    public void Identify_of_a_plain_png_is_only_a_wallpaper(int width, int height)
    {
        using var temp = new C2aTempDir("png");
        string path = temp.WritePngHeader("wall.png", width, height);
        FileKinds.Verdict verdict = FileKinds.Identify(path);
        Assert.Equal(new[] { "wallpaper" }, verdict.Kinds);
        Assert.True(verdict.Sure);
        Assert.DoesNotContain("skin", verdict.Kinds);
    }

    [Fact]
    public void Identify_of_a_png_with_a_broken_signature_falls_back_to_wallpaper()
    {
        using var temp = new C2aTempDir("badpng");
        byte[] bytes = new byte[24];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)i;
        }

        string path = temp.WriteBytes("fake.png", bytes);
        FileKinds.Verdict verdict = FileKinds.Identify(path);
        Assert.Equal(new[] { "wallpaper" }, verdict.Kinds);
        Assert.True(verdict.Sure);
    }

    [Fact]
    public void Identify_of_a_non_archive_file_is_unsure_with_a_detail()
    {
        using var temp = new C2aTempDir("text");
        string path = temp.WriteText("notes.txt", "nothing to see here");
        FileKinds.Verdict verdict = FileKinds.Identify(path);
        Assert.Empty(verdict.Kinds);
        Assert.False(verdict.Sure);
        Assert.False(string.IsNullOrEmpty(verdict.Detail));
    }

    [Fact]
    public void Unpacked_directory_with_two_plain_markers_is_a_modpack()
    {
        using var temp = new C2aTempDir("plain");
        string root = temp.Dir("minecraft");
        Directory.CreateDirectory(System.IO.Path.Combine(root, "mods"));
        Directory.CreateDirectory(System.IO.Path.Combine(root, "config"));
        FileKinds.Verdict verdict = FileKinds.Identify(root);
        Assert.Equal(new[] { "modpack" }, verdict.Kinds);
        Assert.True(verdict.Sure);
        // Win 源:Format = I18n.L("直接压缩的 .minecraft 目录"),目录形态再追加 I18n.L(" · 已解开的目录")。
        Assert.Contains(".minecraft", verdict.Detail, StringComparison.Ordinal);
        Assert.Equal("minecraft", verdict.Name);
    }

    [Fact]
    public void Directory_without_markers_is_not_a_modpack()
    {
        using var temp = new C2aTempDir("emptydir");
        string root = temp.Dir("somewhere");
        File.WriteAllText(System.IO.Path.Combine(root, "readme.txt"), "x");
        FileKinds.Verdict verdict = FileKinds.Identify(root);
        Assert.Empty(verdict.Kinds);
        Assert.False(verdict.Sure);
    }

    [Fact]
    public void Zip_with_level_dat_is_a_world()
    {
        using var temp = new C2aTempDir("world");
        string path = temp.WriteZip("save.zip", "level.dat", "region/r.0.0.mca");
        FileKinds.Verdict verdict = FileKinds.Identify(path);
        Assert.Equal(new[] { "world" }, verdict.Kinds);
        Assert.True(verdict.Sure);
        Assert.Contains("level.dat", verdict.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Zip_with_a_shaders_directory_is_a_shaderpack()
    {
        using var temp = new C2aTempDir("shader");
        string path = temp.WriteZip("shaders.zip", "shaders/composite.fsh");
        FileKinds.Verdict verdict = FileKinds.Identify(path);
        Assert.Equal(new[] { "shaderpack" }, verdict.Kinds);
        Assert.True(verdict.Sure);
        Assert.Contains("shaders", verdict.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Zip_with_pack_mcmeta_and_assets_only_is_a_resourcepack()
    {
        using var temp = new C2aTempDir("rp");
        string path = temp.WriteZip("rp.zip", "pack.mcmeta", "assets/icon.png");
        FileKinds.Verdict verdict = FileKinds.Identify(path);
        Assert.Equal(new[] { "resourcepack" }, verdict.Kinds);
        Assert.True(verdict.Sure);
        Assert.Contains("assets", verdict.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Zip_with_pack_mcmeta_and_data_only_is_a_datapack()
    {
        using var temp = new C2aTempDir("dp");
        string path = temp.WriteZip("dp.zip", "pack.mcmeta", "data/recipe.json");
        FileKinds.Verdict verdict = FileKinds.Identify(path);
        Assert.Equal(new[] { "datapack" }, verdict.Kinds);
        Assert.True(verdict.Sure);
        Assert.Contains("data", verdict.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Zip_with_both_pack_directories_is_unsure_and_lists_both_kinds()
    {
        using var temp = new C2aTempDir("both");
        string path = temp.WriteZip("both.zip", "pack.mcmeta", "assets/icon.png", "data/recipe.json");
        FileKinds.Verdict verdict = FileKinds.Identify(path);
        Assert.Equal(new[] { "resourcepack", "datapack" }, verdict.Kinds);
        Assert.False(verdict.Sure);
        Assert.Equal("resourcepack,datapack", verdict.KindSig);
    }

    [Fact]
    public void Zip_without_known_markers_has_no_kinds()
    {
        using var temp = new C2aTempDir("nomarker");
        string path = temp.WriteZip("plain.zip", "readme.txt", "docs/help.md");
        FileKinds.Verdict verdict = FileKinds.Identify(path);
        Assert.Empty(verdict.Kinds);
        Assert.False(verdict.Sure);
        Assert.False(string.IsNullOrEmpty(verdict.Detail));
    }

    [Fact]
    public void World_wins_over_shaderpack_and_pack_mcmeta()
    {
        // Win 源 IdentifyArchive 的判定顺序:level.dat -> shaders -> pack.mcmeta。
        using var temp = new C2aTempDir("prio1");
        string path = temp.WriteZip("mixed.zip", "level.dat", "shaders/a.fsh", "pack.mcmeta", "assets/x.png");
        FileKinds.Verdict verdict = FileKinds.Identify(path);
        Assert.Equal(new[] { "world" }, verdict.Kinds);
    }

    [Fact]
    public void Shaderpack_wins_over_pack_mcmeta()
    {
        using var temp = new C2aTempDir("prio2");
        string path = temp.WriteZip("mixed2.zip", "shaders/a.fsh", "pack.mcmeta", "assets/x.png");
        FileKinds.Verdict verdict = FileKinds.Identify(path);
        Assert.Equal(new[] { "shaderpack" }, verdict.Kinds);
    }

    [Fact]
    public void Modpack_probe_wins_over_archive_markers()
    {
        using var temp = new C2aTempDir("prio3");
        string path = temp.WriteZipText(
            "pack.zip",
            "modrinth.index.json",
            "{\"name\":\"Demo\",\"versionId\":\"1.2.3\",\"dependencies\":{\"minecraft\":\"1.20.1\",\"fabric-loader\":\"0.15.0\"},\"files\":[{\"path\":\"a.jar\"}]}");
        FileKinds.Verdict verdict = FileKinds.Identify(path);
        Assert.Equal(new[] { "modpack" }, verdict.Kinds);
        Assert.True(verdict.Sure);
        Assert.Contains("Minecraft 1.20.1", verdict.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Plugin_manifest_wins_over_archive_markers()
    {
        using var temp = new C2aTempDir("prio4");
        string path = temp.WriteZipText(
            "plugin.zip",
            "tipcl-plugin.json",
            "{\"id\":\"demo\",\"name\":\"Demo Plugin\",\"version\":\"0.2.0\"}");
        FileKinds.Verdict verdict = FileKinds.Identify(path);
        Assert.Equal(new[] { "plugin" }, verdict.Kinds);
        Assert.True(verdict.Sure);
        Assert.Contains("Demo Plugin", verdict.Detail, StringComparison.Ordinal);
        Assert.Contains("0.2.0", verdict.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Jar_without_mod_metadata_is_a_mod_but_not_sure()
    {
        using var temp = new C2aTempDir("jar");
        string path = temp.WriteZip("thing.jar", "com/example/A.class");
        FileKinds.Verdict verdict = FileKinds.Identify(path);
        Assert.Equal(new[] { "mod" }, verdict.Kinds);
        Assert.False(verdict.Sure);
    }

    [Theory]
    [InlineData("fabric.mod.json")]
    [InlineData("quilt.mod.json")]
    [InlineData("META-INF/mods.toml")]
    [InlineData("META-INF/neoforge.mods.toml")]
    [InlineData("mcmod.info")]
    public void Jar_with_a_mod_descriptor_is_a_sure_mod(string marker)
    {
        using var temp = new C2aTempDir("modjar");
        string path = temp.WriteZip("mod.jar", marker, "com/example/A.class");
        FileKinds.Verdict verdict = FileKinds.Identify(path);
        Assert.Equal(new[] { "mod" }, verdict.Kinds);
        Assert.True(verdict.Sure);
    }

    [Fact]
    public void Loader_installer_jar_is_not_a_mod()
    {
        // Win 源:带 install_profile.json 的 jar 判定为"加载器安装器",Kinds 为空。
        using var temp = new C2aTempDir("installer");
        string path = temp.WriteZip("forge.jar", "install_profile.json", "META-INF/mods.toml");
        FileKinds.Verdict verdict = FileKinds.Identify(path);
        Assert.Empty(verdict.Kinds);
        Assert.False(verdict.Sure);
        Assert.False(string.IsNullOrEmpty(verdict.Detail));
    }

    [Fact]
    public void Entry_names_of_a_zip_are_lowercased_and_slash_normalised()
    {
        using var temp = new C2aTempDir("entrynames");
        string path = temp.WriteZip("x.zip", "A/B.TXT");
        List<string>? names = FileKinds.EntryNames(path);
        Assert.NotNull(names);
        Assert.Equal(new[] { "a/b.txt" }, names!.ToArray());
    }

    [Fact]
    public void Entry_names_of_a_directory_are_lowercased_and_depth_limited()
    {
        // Win 源:WalkNames 只递归到第 3 层(depth + 1 < 3),名字全部小写。
        using var temp = new C2aTempDir("walknames");
        string root = temp.Dir("tree");
        Directory.CreateDirectory(System.IO.Path.Combine(root, "One", "Two", "Three"));
        File.WriteAllText(System.IO.Path.Combine(root, "Top.TXT"), "x");
        File.WriteAllText(System.IO.Path.Combine(root, "One", "Two", "Three", "Deep.TXT"), "x");
        List<string>? names = FileKinds.EntryNames(root);
        Assert.NotNull(names);
        Assert.Contains("top.txt", names!);
        Assert.Contains("one", names);
        Assert.Contains("one/two", names);
        Assert.Contains("one/two/three", names);
        Assert.DoesNotContain("one/two/three/deep.txt", names);
    }

    [Fact]
    public void Entry_names_of_a_missing_path_is_null()
    {
        using var temp = new C2aTempDir("noentry");
        Assert.Null(FileKinds.EntryNames(temp.File("nope.zip")));
        Assert.Null(FileKinds.EntryNames(temp.File("nope.txt")));
    }

    [Fact]
    public void Modpack_probe_reads_the_mrpack_metadata()
    {
        using var temp = new C2aTempDir("mrpack");
        string path = temp.WriteZipText(
            "m.mrpack",
            "modrinth.index.json",
            "{\"name\":\"Demo\",\"versionId\":\"2.0.0\",\"dependencies\":{\"minecraft\":\"1.21\",\"neoforge\":\"21.0.1\"},\"files\":[{\"path\":\"a.jar\"},{\"path\":\"b.jar\"}]}");
        FileKinds.ModpackProbe.Info? info = FileKinds.ModpackProbe.Probe(path);
        Assert.NotNull(info);
        Assert.Equal("mrpack", info!.Kind);
        Assert.Equal("Modrinth .mrpack", info.Format);
        Assert.Equal("Demo", info.Name);
        Assert.Equal("2.0.0", info.Version);
        Assert.Equal("1.21", info.McVersion);
        Assert.Equal("neoforge", info.Loader);
        Assert.Equal("21.0.1", info.LoaderVersion);
        Assert.Equal(2, info.Files);
        Assert.Equal(path, info.Path);
    }

    [Fact]
    public void Modpack_probe_reads_the_curseforge_manifest_and_splits_the_loader_id()
    {
        using var temp = new C2aTempDir("curseforge");
        string path = temp.WriteZipText(
            "cf.zip",
            "manifest.json",
            "{\"name\":\"CF Pack\",\"version\":\"3\",\"minecraft\":{\"version\":\"1.20.1\",\"modLoaders\":[{\"id\":\"forge-47.2.0\",\"primary\":true}]},\"files\":[]}");
        FileKinds.ModpackProbe.Info? info = FileKinds.ModpackProbe.Probe(path);
        Assert.NotNull(info);
        Assert.Equal("curseforge", info!.Kind);
        Assert.Equal("CurseForge .zip", info.Format);
        Assert.Equal("CF Pack", info.Name);
        Assert.Equal("3", info.Version);
        Assert.Equal("1.20.1", info.McVersion);
        Assert.Equal("forge", info.Loader);
        Assert.Equal("47.2.0", info.LoaderVersion);
        Assert.Equal(0, info.Files);
    }

    [Fact]
    public void Modpack_probe_returns_null_for_a_non_modpack()
    {
        using var temp = new C2aTempDir("notpack");
        string zip = temp.WriteZip("plain.zip", "readme.txt");
        Assert.Null(FileKinds.ModpackProbe.Probe(zip));
        Assert.Null(FileKinds.ModpackProbe.Probe(temp.File("missing.zip")));
    }

    [Fact]
    public void Modpack_loader_label_maps_known_loaders_and_falls_back_to_the_raw_name()
    {
        Assert.Equal("Forge", FileKinds.ModpackProbe.LoaderLabel("forge"));
        Assert.Equal("NeoForge", FileKinds.ModpackProbe.LoaderLabel("NEOFORGE"));
        Assert.Equal("Fabric", FileKinds.ModpackProbe.LoaderLabel("fabric-loader"));
        Assert.Equal("Quilt 1.2", FileKinds.ModpackProbe.LoaderLabel("quilt", "1.2"));
        Assert.Equal("mystery", FileKinds.ModpackProbe.LoaderLabel("mystery"));
        // 键里的括号是全角(源文件实测 U+FF08/U+FF09),照抄必须用转义(D-58)。
        Assert.Equal(I18n.L("\u539f\u7248\uFF08\u672a\u58f0\u660e\u52a0\u8f7d\u5668\uFF09"), FileKinds.ModpackProbe.LoaderLabel(""));
        Assert.Equal(I18n.L("\u539f\u7248\uFF08\u672a\u58f0\u660e\u52a0\u8f7d\u5668\uFF09"), FileKinds.ModpackProbe.LoaderLabel(null));
    }
}
