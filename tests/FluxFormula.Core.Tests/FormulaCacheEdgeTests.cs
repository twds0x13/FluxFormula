using System;
using System.Reflection;
using FluxFormula.Core;
using NUnit.Framework;

/// <summary>
/// FormulaCache 边界路径：DumpSlot 诊断、delegate 逐出、墓碑回收、满表压力。
/// </summary>
public unsafe class FormulaCacheEdgeTests
{
    // ═══════════════════════════════════════════════════════
    // DumpSlot
    // ═══════════════════════════════════════════════════════

    [Test]
    public void DumpSlot_EmptyAndFilled()
    {
        var dumpMethod = typeof(FormulaCache).GetMethod("DumpSlot",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var cache = new FormulaCache(16);
        byte* p = stackalloc byte[1];

        Assert.That((string)dumpMethod.Invoke(cache, new object[] { 0 }), Does.Contain("Empty"));

        var key = DualHash64.Compute(new byte[] { 1 });
        cache.Put(key, (IntPtr)p, 1);
        Assert.That((string)dumpMethod.Invoke(cache, new object[] { 0 }), Does.Contain("Len="));

        // 满表多次逐出——覆盖 EvictAndWrite 活跃条目分支
        for (int i = 0; i < 30; i++)
            cache.Put(DualHash64.Compute(BitConverter.GetBytes(i)), (IntPtr)p, 1);

        int filled = 0;
        for (int i = 0; i < 16; i++)
        {
            string s = (string)dumpMethod.Invoke(cache, new object[] { i });
            if (!s.Contains("Empty")) filled++;
        }
        Assert.That(filled, Is.GreaterThan(0));
    }

    // ═══════════════════════════════════════════════════════
    // Delegate 逐出
    // ═══════════════════════════════════════════════════════

    [Test]
    public void DelegateSlot_Eviction_DoesNotCrash()
    {
        var cache = new FormulaCache(4);
        byte* p = stackalloc byte[1];

        for (int i = 0; i < 4; i++)
            cache.Put(DualHash64.Compute(BitConverter.GetBytes(i)), (IntPtr)p, 1);
        Assert.That(cache.Count, Is.EqualTo(4));

        var dKey = DualHash64.Compute(BitConverter.GetBytes(999));
        Action del = () => { };
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(del);
        try
        {
            Assert.That(() => cache.PutDelegate(dKey,
                System.Runtime.InteropServices.GCHandle.ToIntPtr(handle)),
                Throws.Nothing);
            Assert.That(cache.TryGetDelegate(dKey, out _), Is.True);
        }
        finally
        {
            if (cache.TryGetDelegate(dKey, out IntPtr rp))
            {
                var h = System.Runtime.InteropServices.GCHandle.FromIntPtr(rp);
                if (h.IsAllocated) h.Free();
            }
        }
    }

    // ═══════════════════════════════════════════════════════
    // 墓碑回收 + 满表压力
    // ═══════════════════════════════════════════════════════

    [Test]
    public void TombstoneReuse_KeepsCountStable()
    {
        var cache = new FormulaCache(64);
        byte* p = stackalloc byte[1];

        for (int i = 0; i < 64; i++)
            cache.Put(DualHash64.Compute(BitConverter.GetBytes(i)), (IntPtr)p, 1);

        for (int i = 0; i < 32; i++)
            cache.Put(DualHash64.Compute(BitConverter.GetBytes(i + 64)), (IntPtr)p, 1);

        Assert.That(cache.TombstoneCount, Is.LessThan(32));
    }

    [Test]
    public void EvictAndWrite_ManyCycles_DoesNotCorrupt()
    {
        var cache = new FormulaCache(16);
        byte* p = stackalloc byte[1];

        for (int i = 0; i < 100; i++)
            cache.Put(DualHash64.Compute(BitConverter.GetBytes(i)), (IntPtr)p, 1);

        Assert.That(cache.Count, Is.GreaterThan(0));
        Assert.That(cache.TombstoneCount, Is.LessThan(cache.Capacity));
    }
}
