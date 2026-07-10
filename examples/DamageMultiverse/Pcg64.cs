using System.Runtime.CompilerServices;

/// <summary>
/// Minimal PCG64 random number generator.
/// Fixed seed produces deterministic sequences for reproducible multiverse simulations.
/// </summary>
public struct Pcg64
{
    private ulong _state;
    private ulong _inc;

    public Pcg64(ulong seed)
    {
        _state = 0;
        _inc = (seed << 1) | 1;
        NextUint();
        _state += seed;
        NextUint();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint NextUint()
    {
        ulong old = _state;
        _state = old * 6364136223846793005UL + _inc;
        uint xor = (uint)(((old >> 18) ^ old) >> 27);
        int rot = (int)(old >> 59);
        return (xor >> rot) | (xor << ((-rot) & 31));
    }

    /// <summary>Returns a float in [0, 1).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float NextFloat()
    {
        // Use top 24 bits for uniform float
        return (NextUint() >> 8) * (1f / 16777216f);
    }
}
