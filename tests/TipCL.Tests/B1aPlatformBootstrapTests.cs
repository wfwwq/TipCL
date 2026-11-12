using System;
using TipCL.Core.Launcher;
using TipCL.Core.Platform;
using Xunit;

namespace TipCL.Tests;

/// <summary>
/// B1a:平台接线(桌面启动与 <c>--smoke</c> 都调 <see cref="PlatformBootstrap.Init()"/>)。
///
/// 纯函数放在 <c>TipCL.Core</c>,测试工程只引用 Core —— 不需要把 Avalonia 的界面初始化拉进测试进程。
/// 改动 <see cref="PlatformServices"/> 的用例在 finally 里恢复原值。
/// </summary>
public class B1aPlatformBootstrapTests
{
    [Fact]
    public void Only_mac_gets_a_secret_store()
    {
        Assert.IsType<MacKeychainSecretStore>(PlatformBootstrap.CreateSecretStore(isMacOs: true));
        Assert.Null(PlatformBootstrap.CreateSecretStore(isMacOs: false));
    }

    [Fact]
    public void The_image_codec_is_skia_on_every_platform()
    {
        Assert.IsType<SkiaImageCodec>(PlatformBootstrap.CreateImageCodec());
    }

    [Fact]
    public void Init_assigns_both_services_for_mac()
    {
        IImageCodec previousImage = PlatformServices.ImageCodec;
        ISecretStore previousSecret = PlatformServices.SecretStore;
        try
        {
            PlatformBootstrap.Init(isMacOs: true);

            Assert.IsType<SkiaImageCodec>(PlatformServices.ImageCodec);
            Assert.IsType<MacKeychainSecretStore>(PlatformServices.SecretStore);
        }
        finally
        {
            PlatformServices.ImageCodec = previousImage;
            PlatformServices.SecretStore = previousSecret;
        }
    }

    [Fact]
    public void Init_leaves_the_secret_store_null_off_mac()
    {
        IImageCodec previousImage = PlatformServices.ImageCodec;
        ISecretStore previousSecret = PlatformServices.SecretStore;
        try
        {
            PlatformBootstrap.Init(isMacOs: false);

            Assert.IsType<SkiaImageCodec>(PlatformServices.ImageCodec);
            Assert.Null(PlatformServices.SecretStore);
        }
        finally
        {
            PlatformServices.ImageCodec = previousImage;
            PlatformServices.SecretStore = previousSecret;
        }
    }

    [Fact]
    public void Init_without_arguments_follows_the_running_platform()
    {
        IImageCodec previousImage = PlatformServices.ImageCodec;
        ISecretStore previousSecret = PlatformServices.SecretStore;
        try
        {
            PlatformBootstrap.Init();

            Assert.IsType<SkiaImageCodec>(PlatformServices.ImageCodec);
            Assert.Equal(OperatingSystem.IsMacOS(), PlatformServices.SecretStore is MacKeychainSecretStore);
            // 本机是 Windows:接线后凭据库仍然是 null,绝不会去碰 Windows 凭据管理器
            if (!OperatingSystem.IsMacOS())
            {
                Assert.Null(PlatformServices.SecretStore);
            }
        }
        finally
        {
            PlatformServices.ImageCodec = previousImage;
            PlatformServices.SecretStore = previousSecret;
        }
    }

    [Fact]
    public void Init_does_not_touch_the_data_directory()
    {
        IImageCodec previousImage = PlatformServices.ImageCodec;
        ISecretStore previousSecret = PlatformServices.SecretStore;
        string root = Utils.ROOT;
        try
        {
            PlatformBootstrap.Init(isMacOs: true);

            // 只赋值:数据目录不变,也没有任何落盘动作(测试全程在 TestHome.Root 下)
            Assert.Equal(root, Utils.ROOT);
            Assert.StartsWith(TestHome.Root, root, StringComparison.Ordinal);
        }
        finally
        {
            PlatformServices.ImageCodec = previousImage;
            PlatformServices.SecretStore = previousSecret;
        }
    }
}
