using System;

namespace FluxFormula.Core
{
    public struct FluxToken<TData>
        where TData : unmanaged
    {
        public byte Oper;
        public TData Data;

        public override readonly string ToString() => $"Token(0x{Oper:X2}, Data: {Data})";
    }
}
