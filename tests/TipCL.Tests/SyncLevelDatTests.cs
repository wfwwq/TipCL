using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using TipCL.Core.Launcher.Sync;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// A4:`Sync\LevelDat.cs`(A4 起参与编译)的用例。
///
/// LevelDat 只读 level.dat 里 4 个字段(源码 8-11 行的说明),是纯函数:
///   Read 37-65(头 2 字节是 1F 8B 就先 gunzip;首字节不是 10(Compound)返回 null;
///   任何异常都被吞掉返回 null)、Reader 67-209(大端;U8/I16/I32/I64/Str;
///   Compound 121-134 顺序遍历,path 用 "." 连接;Payload 136-208 按类型跳过或取值,
///   ByteArray=7、IntArray=11、LongArray=12、List=9 走的是长度前缀,数组元素路径会加上 "[]")。
///   字段只在 full 精确等于 "Data.LevelName" / "Data.LastPlayed" / "Data.DataVersion" /
///   "Data.Version.Name" 时才赋值(149/158/176/180 行)。
///
/// 测试数据全部在用例里用字节数组现造(不读任何真实存档);
/// 只有 ReadFile 的两条用例会落盘,且落在 TestHome.Root 下的子目录里(红线:不碰用户真实数据目录)。
/// </summary>
public class SyncLevelDatTests
{
    // ---------- NBT 字节构造器(大端) ----------

    private sealed class Nbt
    {
        private readonly List<byte> _b = new List<byte>();

        public Nbt U8(int v) { _b.Add((byte)v); return this; }

        public Nbt I16(int v) { _b.Add((byte)(v >> 8)); _b.Add((byte)v); return this; }

        public Nbt I32(int v)
        {
            _b.Add((byte)(v >> 24)); _b.Add((byte)(v >> 16)); _b.Add((byte)(v >> 8)); _b.Add((byte)v);
            return this;
        }

        public Nbt I64(long v)
        {
            for (int s = 56; s >= 0; s -= 8) _b.Add((byte)(v >> s));
            return this;
        }

        public Nbt Str(string s)
        {
            byte[] u = Encoding.UTF8.GetBytes(s);
            I16(u.Length);
            _b.AddRange(u);
            return this;
        }

        public Nbt Raw(byte[] more) { _b.AddRange(more); return this; }

        public Nbt Zeros(int n) { for (int i = 0; i < n; i++) _b.Add(0); return this; }

        public byte[] Done() { return _b.ToArray(); }
    }

    /// <summary>Data{ LevelName, LastPlayed, DataVersion, Version{Name} } 的完整 NBT 文档。</summary>
    private static byte[] NbtDoc(string levelName, long lastPlayed, int dataVersion, string versionName, string dataTagName = "Data")
    {
        Nbt data = new Nbt();
        data.U8(8).Str("LevelName").Str(levelName);
        data.U8(4).Str("LastPlayed").I64(lastPlayed);
        data.U8(3).Str("DataVersion").I32(dataVersion);
        data.U8(10).Str("Version");
        data.U8(8).Str("Name").Str(versionName);
        data.U8(0); // 收 Version

        Nbt root = new Nbt();
        root.U8(10).Str("");            // 根 TAG_Compound + 空名
        root.U8(10).Str(dataTagName);
        root.Raw(data.Done());
        root.U8(0);                     // 收 Data
        root.U8(0);                     // 收根
        return root.Done();
    }

    private static byte[] Gzip(byte[] raw)
    {
        using MemoryStream ms = new MemoryStream();
        using (GZipStream gz = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
        {
            gz.Write(raw, 0, raw.Length);
        }
        return ms.ToArray();
    }

    // ---------- 正常读取 ----------

    [Fact]
    public void Reads_the_four_fields_from_a_raw_uncompressed_document()
    {
        LevelDat d = LevelDat.Read(NbtDoc("My World", 1700000000000L, 3955, "1.21.4"));

        Assert.NotNull(d);
        Assert.Equal("My World", d.LevelName);
        Assert.Equal(1700000000000L, d.LastPlayed);
        Assert.Equal(3955, d.DataVersion);
        Assert.Equal("1.21.4", d.VersionName);
    }

    [Fact]
    public void Gzipped_input_is_inflated_first()
    {
        LevelDat d = LevelDat.Read(Gzip(NbtDoc("Gz World", 42L, 3465, "1.20.1")));

        Assert.NotNull(d);
        Assert.Equal("Gz World", d.LevelName);
        Assert.Equal(42L, d.LastPlayed);
        Assert.Equal(3465, d.DataVersion);
        Assert.Equal("1.20.1", d.VersionName);
    }

    [Fact]
    public void Missing_fields_keep_their_defaults()
    {
        // 只有 Data{DataVersion},没有 LevelName / LastPlayed / Version
        Nbt data = new Nbt();
        data.U8(3).Str("DataVersion").I32(1234);
        data.U8(0);

        Nbt root = new Nbt();
        root.U8(10).Str("");
        root.U8(10).Str("Data");
        root.Raw(data.Done());
        root.U8(0);

        LevelDat d = LevelDat.Read(root.Done());

        Assert.NotNull(d);
        Assert.Equal("", d.LevelName);
        Assert.Equal(0L, d.LastPlayed);
        Assert.Equal(1234, d.DataVersion);
        Assert.Equal("", d.VersionName);
    }

    [Fact]
    public void LastPlayed_is_a_signed_64_bit_value()
    {
        LevelDat d = LevelDat.Read(NbtDoc("Neg", -1L, 1, "x"));

        Assert.NotNull(d);
        Assert.Equal(-1L, d.LastPlayed);
    }

    [Fact]
    public void DataVersion_is_a_signed_32_bit_value()
    {
        LevelDat d = LevelDat.Read(NbtDoc("Big", 0L, int.MaxValue, "x"));

        Assert.NotNull(d);
        Assert.Equal(int.MaxValue, d.DataVersion);
    }

    [Fact]
    public void Utf8_names_round_trip()
    {
        LevelDat d = LevelDat.Read(NbtDoc("\u4e16\u754c A", 7L, 99, "1.21"));

        Assert.NotNull(d);
        Assert.Equal("\u4e16\u754c A", d.LevelName);
    }

    // ---------- 路径筛选 ----------

    [Fact]
    public void Fields_outside_the_exact_Data_paths_are_ignored()
    {
        LevelDat d = LevelDat.Read(NbtDoc("Hidden", 5L, 6, "1.0", dataTagName: "Foo"));

        Assert.NotNull(d);
        // 整棵文档都在 Foo 下面,四个 full 路径都不匹配 → 全是默认值
        Assert.Equal("", d.LevelName);
        Assert.Equal(0L, d.LastPlayed);
        Assert.Equal(0, d.DataVersion);
        Assert.Equal("", d.VersionName);
    }

    [Fact]
    public void Version_name_must_be_Data_Version_Name_exactly()
    {
        // Data{ Version{ Sub{ Name="deep" } } } → full 是 Data.Version.Sub.Name,不匹配
        Nbt data = new Nbt();
        data.U8(10).Str("Version");
        data.U8(10).Str("Sub");
        data.U8(8).Str("Name").Str("deep");
        data.U8(0);
        data.U8(0);
        data.U8(10).Str("Version");          // 第二个 Version,这次路径正确
        data.U8(8).Str("Name").Str("1.21.4");
        data.U8(0);
        data.U8(0);

        Nbt root = new Nbt();
        root.U8(10).Str("");
        root.U8(10).Str("Data");
        root.Raw(data.Done());
        root.U8(0);

        LevelDat d = LevelDat.Read(root.Done());

        Assert.NotNull(d);
        Assert.Equal("1.21.4", d.VersionName);
    }

    // ---------- 跳过各种载荷 ----------

    [Fact]
    public void Every_payload_kind_is_skipped_with_the_right_width()
    {
        Nbt data = new Nbt();
        data.U8(1).Str("by").U8(7);                          // TAG_Byte
        data.U8(2).Str("sh").I16(300);                       // TAG_Short
        data.U8(5).Str("fl").Zeros(4);                       // TAG_Float
        data.U8(6).Str("db").Zeros(8);                       // TAG_Double
        data.U8(7).Str("ba").I32(3).Zeros(3);                // TAG_ByteArray
        data.U8(11).Str("ia").I32(2).Zeros(8);               // TAG_IntArray
        data.U8(12).Str("la").I32(2).Zeros(16);              // TAG_LongArray
        data.U8(9).Str("list").U8(8).I32(2).Str("a").Str("bb"); // TAG_List<TAG_String>
        data.U8(9).Str("clist").U8(10).I32(1).U8(0);          // TAG_List<TAG_Compound>,一个空 compound
        data.U8(10).Str("empty").U8(0);                      // 空 compound
        data.U8(8).Str("LevelName").Str("after skips");      // 跳过之后的真字段仍要能取到
        data.U8(4).Str("LastPlayed").I64(9L);
        data.U8(0);                                          // 收 Data

        Nbt root = new Nbt();
        root.U8(10).Str("");
        root.U8(10).Str("Data");
        root.Raw(data.Done());
        root.U8(0);

        LevelDat d = LevelDat.Read(root.Done());

        Assert.NotNull(d);
        Assert.Equal("after skips", d.LevelName);
        Assert.Equal(9L, d.LastPlayed);
    }

    // ---------- 坏数据 ----------

    [Fact]
    public void Empty_input_returns_null()
    {
        Assert.Null(LevelDat.Read(new byte[0]));
    }

    [Fact]
    public void Null_input_returns_null()
    {
        Assert.Null(LevelDat.Read(null));
    }

    [Fact]
    public void Non_compound_root_returns_null()
    {
        // 首字节 9(TAG_List)不是 10 → 53-56 行直接 return null
        Assert.Null(LevelDat.Read(new byte[] { 9, 0, 0, 0 }));
    }

    [Fact]
    public void Root_name_only_is_not_enough()
    {
        // 首字节 10 但没有根名 → 读 Str 越界 → 被 catch 吞掉返回 null
        Assert.Null(LevelDat.Read(new byte[] { 10 }));
    }

    [Fact]
    public void Unknown_tag_type_returns_null()
    {
        Nbt data = new Nbt();
        data.U8(13).Str("weird").Zeros(4);   // 13 不是合法 NBT 类型 → 205-207 行抛 InvalidDataException
        data.U8(0);

        Nbt root = new Nbt();
        root.U8(10).Str("");
        root.U8(10).Str("Data");
        root.Raw(data.Done());
        root.U8(0);

        Assert.Null(LevelDat.Read(root.Done()));
    }

    [Fact]
    public void Negative_byte_array_length_returns_null()
    {
        // Skip 112-119:_p + n > 长度就抛,负数也抛 → 整份文档解析失败
        Nbt data = new Nbt();
        data.U8(7).Str("ba").I32(-1);
        data.U8(0);

        Nbt root = new Nbt();
        root.U8(10).Str("");
        root.U8(10).Str("Data");
        root.Raw(data.Done());
        root.U8(0);

        Assert.Null(LevelDat.Read(root.Done()));
    }

    [Fact]
    public void Truncated_string_payload_returns_null()
    {
        byte[] full = NbtDoc("Truncated", 1L, 2, "3");
        byte[] cut = new byte[full.Length - 3];
        Array.Copy(full, cut, cut.Length);

        Assert.Null(LevelDat.Read(cut));
    }

    [Fact]
    public void Broken_gzip_header_returns_null()
    {
        // 头两字节是 gzip 魔数,但后面不是合法 gzip 流 → GZipStream 抛 → null
        Assert.Null(LevelDat.Read(new byte[] { 0x1F, 0x8B, 0x08, 0x00, 0x01, 0x02 }));
    }

    // ---------- ReadFile(落盘的两条,目录在 TestHome.Root 下) ----------

    [Fact]
    public void ReadFile_reads_a_gzipped_level_dat_from_disk()
    {
        string dir = Path.Combine(TestHome.Root, "leveldat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "level.dat");
        File.WriteAllBytes(path, Gzip(NbtDoc("On Disk", 1700000000123L, 4189, "1.21.5")));

        LevelDat d = LevelDat.ReadFile(path);

        Assert.NotNull(d);
        Assert.Equal("On Disk", d.LevelName);
        Assert.Equal(1700000000123L, d.LastPlayed);
        Assert.Equal(4189, d.DataVersion);
        Assert.Equal("1.21.5", d.VersionName);
        Assert.StartsWith(TestHome.Root, path, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadFile_returns_null_for_a_missing_file()
    {
        string path = Path.Combine(TestHome.Root, "leveldat-not-there", Guid.NewGuid().ToString("N"), "level.dat");

        Assert.Null(LevelDat.ReadFile(path));
    }
}
