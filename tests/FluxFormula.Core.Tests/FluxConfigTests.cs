using System;
using FluxFormula.Core;
using NUnit.Framework;

/// <summary>
/// FluxConfig 全局配置：Default 值、Set/Current 注入、null 守卫。
/// </summary>
public class FluxConfigTests
{
    [Test]
    public void Default_HasReasonableValues()
    {
        var cfg = FluxConfig.Default;
        Assert.That(cfg.FormulaCacheCapacity, Is.GreaterThan(0));
        Assert.That(cfg.MergeThreshold, Is.GreaterThan(1));
        Assert.That(cfg.BlobFilePath, Is.Null, "Default BlobFilePath should be null (use built-in default path)");
        Assert.That(cfg.DiskCacheDirectory, Is.Null, "Default DiskCacheDirectory should be null (use persistentDataPath)");
    }

    [Test]
    public void Current_ReturnsDefault_WhenNotSet()
    {
        var cfg = FluxConfig.Current;
        Assert.That(cfg.FormulaCacheCapacity, Is.EqualTo(FluxConfig.Default.FormulaCacheCapacity));
        Assert.That(cfg.MergeThreshold, Is.EqualTo(FluxConfig.Default.MergeThreshold));
    }

    [Test]
    public void Set_ChangesCurrent()
    {
        var custom = new FluxConfig { FormulaCacheCapacity = 1024, MergeThreshold = 16 };
        var old = FluxConfig.Current;
        try
        {
            FluxConfig.Set(custom);
            Assert.That(FluxConfig.Current.FormulaCacheCapacity, Is.EqualTo(1024));
            Assert.That(FluxConfig.Current.MergeThreshold, Is.EqualTo(16));
        }
        finally { FluxConfig.Set(old); }
    }

    [Test]
    public void Set_Null_Throws()
    {
        Assert.That(() => FluxConfig.Set(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void Current_Setter_Works()
    {
        var custom = new FluxConfig { FormulaCacheCapacity = 512, MergeThreshold = 4 };
        var old = FluxConfig.Current;
        try
        {
            FluxConfig.Current = custom;
            Assert.That(FluxConfig.Current.FormulaCacheCapacity, Is.EqualTo(512));
        }
        finally { FluxConfig.Current = old; }
    }

    [Test]
    public void Custom_FilePaths_Propagate()
    {
        var custom = new FluxConfig
        {
            BlobFilePath       = "CustomBlob/flux.blob",
            DiskCacheDirectory = "/tmp/flux_cache",
        };
        var old = FluxConfig.Current;
        try
        {
            FluxConfig.Set(custom);
            Assert.That(FluxConfig.Current.BlobFilePath, Is.EqualTo("CustomBlob/flux.blob"));
            Assert.That(FluxConfig.Current.DiskCacheDirectory, Is.EqualTo("/tmp/flux_cache"));
        }
        finally { FluxConfig.Set(old); }
    }
}
