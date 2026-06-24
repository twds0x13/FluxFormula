using System;
using System.IO;
using FluxFormula.Core;
using NUnit.Framework;

public class FileFluxFileFormatterTests
{
    private string _tempDir;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FFTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ═══════════════════════════════════════════════════════
    // Save
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Save_FormulaKind_AppendsFfExtension()
    {
        var formatter = new FileFluxFileFormatter();
        var data = new byte[] { 1, 2, 3 };
        string path = Path.Combine(_tempDir, "test");

        formatter.Save(data, FluxArtifactKind.Formula, path);

        Assert.That(File.Exists(path + ".ff"), Is.True);
        Assert.That(File.ReadAllBytes(path + ".ff"), Is.EqualTo(data));
    }

    [Test]
    public void Save_VirtualKind_AppendsVffExtension()
    {
        var formatter = new FileFluxFileFormatter();
        var data = new byte[] { (byte)'V', (byte)'F', (byte)'F', 0, 1, 0, 0, 0 };
        string path = Path.Combine(_tempDir, "virtual_test");

        formatter.Save(data, FluxArtifactKind.Virtual, path);

        Assert.That(File.Exists(path + ".vff"), Is.True);
        Assert.That(File.ReadAllBytes(path + ".vff"), Is.EqualTo(data));
    }

    [Test]
    public void Save_PathAlreadyHasExtension_NoDoubleExtension()
    {
        var formatter = new FileFluxFileFormatter();
        var data = new byte[] { 42 };
        string path = Path.Combine(_tempDir, "already.ff");

        formatter.Save(data, FluxArtifactKind.Formula, path);

        // Should not create "already.ff.ff"
        Assert.That(File.Exists(path), Is.True);
        Assert.That(File.Exists(path + ".ff"), Is.False);
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(data));
    }

    [Test]
    public void Save_PathAlreadyHasVffExtension_NoDoubleExtension()
    {
        var formatter = new FileFluxFileFormatter();
        var data = new byte[] { (byte)'V', (byte)'F', (byte)'F', 0 };
        string path = Path.Combine(_tempDir, "already.vff");

        formatter.Save(data, FluxArtifactKind.Virtual, path);

        Assert.That(File.Exists(path), Is.True);
        Assert.That(File.Exists(path + ".vff"), Is.False);
    }

    [Test]
    public void Save_EmptyData_WritesEmptyFile()
    {
        var formatter = new FileFluxFileFormatter();
        var data = Array.Empty<byte>();
        string path = Path.Combine(_tempDir, "empty");

        formatter.Save(data, FluxArtifactKind.Formula, path);

        Assert.That(File.Exists(path + ".ff"), Is.True);
        Assert.That(File.ReadAllBytes(path + ".ff").Length, Is.EqualTo(0));
    }

    // ═══════════════════════════════════════════════════════
    // Load
    // ═══════════════════════════════════════════════════════

    [Test]
    public void Load_FfFile_DetectsFormulaKind()
    {
        var formatter = new FileFluxFileFormatter();
        var data = new byte[] { 10, 20, 30 };
        string path = Path.Combine(_tempDir, "formula.ff");
        File.WriteAllBytes(path, data);

        var loaded = formatter.Load(path, out var kind);

        Assert.That(kind, Is.EqualTo(FluxArtifactKind.Formula));
        Assert.That(loaded, Is.EqualTo(data));
    }

    [Test]
    public void Load_VffFile_DetectsVirtualKind()
    {
        var formatter = new FileFluxFileFormatter();
        // VFF magic: "VFF\0" + version(1) + linkCount(0) + overrideCount(0) + flags(0)
        var data = new byte[] { (byte)'V', (byte)'F', (byte)'F', 0, 1, 0, 0, 0 };
        string path = Path.Combine(_tempDir, "virtual.vff");
        File.WriteAllBytes(path, data);

        var loaded = formatter.Load(path, out var kind);

        Assert.That(kind, Is.EqualTo(FluxArtifactKind.Virtual));
        Assert.That(loaded, Is.EqualTo(data));
    }

    [Test]
    public void Load_NonVffBytes_DetectsFormulaKind()
    {
        var formatter = new FileFluxFileFormatter();
        // Bytes that don't start with VFF\0 magic
        var data = new byte[] { 0xFF, 0xFE, 0xFD };
        string path = Path.Combine(_tempDir, "unknown.ff");
        File.WriteAllBytes(path, data);

        var loaded = formatter.Load(path, out var kind);

        Assert.That(kind, Is.EqualTo(FluxArtifactKind.Formula));
        Assert.That(loaded, Is.EqualTo(data));
    }

    // ═══════════════════════════════════════════════════════
    // Round-trip
    // ═══════════════════════════════════════════════════════

    [Test]
    public void SaveLoad_RoundTrip_FormulaKind()
    {
        var formatter = new FileFluxFileFormatter();
        var data = new byte[] { 1, 2, 3, 4, 5 };
        string path = Path.Combine(_tempDir, "roundtrip");

        formatter.Save(data, FluxArtifactKind.Formula, path);
        var loaded = formatter.Load(path + ".ff", out var kind);

        Assert.That(kind, Is.EqualTo(FluxArtifactKind.Formula));
        Assert.That(loaded, Is.EqualTo(data));
    }

    [Test]
    public void SaveLoad_RoundTrip_VirtualKind()
    {
        var formatter = new FileFluxFileFormatter();
        var data = new byte[] { (byte)'V', (byte)'F', (byte)'F', 0, 1, 1, 0, 0,
            0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0, 0,0,0,0,0,0 };
        string path = Path.Combine(_tempDir, "vff_roundtrip");

        formatter.Save(data, FluxArtifactKind.Virtual, path);
        var loaded = formatter.Load(path + ".vff", out var kind);

        Assert.That(kind, Is.EqualTo(FluxArtifactKind.Virtual));
        Assert.That(loaded, Is.EqualTo(data));
    }

    [Test]
    public void SaveLoad_RoundTrip_WithExistingExtension()
    {
        var formatter = new FileFluxFileFormatter();
        var data = new byte[] { 7, 8, 9 };
        string path = Path.Combine(_tempDir, "explicit.ff");

        formatter.Save(data, FluxArtifactKind.Formula, path);
        var loaded = formatter.Load(path, out var kind);

        Assert.That(kind, Is.EqualTo(FluxArtifactKind.Formula));
        Assert.That(loaded, Is.EqualTo(data));
    }
}
