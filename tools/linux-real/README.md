# tools/linux-real —— 在真 Linux 上复验(AI3-linux-real)

`verify.sh` 在一台真 Linux(含 WSL2)上依次跑:

1. `dotnet build TipCL.Linux.sln -c Debug --no-incremental`
2. 全量 `dotnet test`(无头:清掉 `DISPLAY` / `WAYLAND_DISPLAY`)
3. `--ai-selftest`(临时 `TIPCL_HOME`)
4. `probe/`:对照 `/proc/cpuinfo`、`/proc/meminfo`(以及装了的话 `nvidia-smi`)核对 `SystemInfo.Snapshot()`;
   用一个**只记录参数**的假 `xdg-open` 核对 `Crash.ShellOpen` / `Crash.OpenPath` / `ShellOpen.Url|TryUrl|Folder` /
   `Auth.OpenBrowser` 传给 xdg-open 的 argv(中文、空格、引号、反斜杠、制表符、换行、shell 元字符)。

约束:不起界面、不启动游戏、不真打开任何东西;探针发现 PATH 上的 xdg-open 不是记录脚本就拒绝运行。
需要 PATH 上有 .NET 8 SDK(可用微软官方 `dotnet-install.sh --channel 8.0 --install-dir ~/.dotnet` 装到用户目录)。

```bash
export PATH=$HOME/.dotnet:$PATH DOTNET_ROOT=$HOME/.dotnet
bash tools/linux-real/verify.sh /tmp/tipcl-linux-real
```

第一次实测结果见 `progress/AI3-linux-real.md`。
