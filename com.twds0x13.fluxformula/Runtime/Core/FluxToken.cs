using System;

namespace FluxFormula.Core
{
    public struct FluxToken<TData, TOper>
        where TOper : unmanaged, Enum
        where TData : unmanaged
    {
        public TOper Oper;
        public TData Data;

        public override readonly string ToString() => $"Token({Oper}, Data: {Data})";
    }
}
