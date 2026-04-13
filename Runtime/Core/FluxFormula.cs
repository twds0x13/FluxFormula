using System;

namespace FluxFormula.Core
{
    public readonly struct FluxFormula<TData, TOper>
        where TData : unmanaged
        where TOper : unmanaged, Enum
    {
        // 将 private 修改为 internal，允许 Evaluator 扫描位点
        internal readonly Instruction[] Buffer;
        public readonly int ValidCount;
        public readonly FluxType Type;

        internal FluxFormula(Instruction[] buffer, int count, FluxType type)
        {
            Buffer = buffer;
            ValidCount = count;
            Type = type;
        }

        public FluxFormula<TData, TOper> Connect(FluxFormula<TData, TOper> next)
        {
            // 去掉前者末尾的 Return (ValidCount - 1)，拼接后者全部
            int newCount = (this.ValidCount - 1) + next.ValidCount;
            Instruction[] newBuffer = new Instruction[newCount];

            Array.Copy(this.Buffer, 0, newBuffer, 0, this.ValidCount - 1);
            Array.Copy(next.Buffer, 0, newBuffer, this.ValidCount - 1, next.ValidCount);

            // 只要起始是 Source，结果就是 Source；否则为 Modifier 片段
            FluxType newType = (this.Type == FluxType.Formula) ? FluxType.Formula : FluxType.Modifier;
            return new FluxFormula<TData, TOper>(newBuffer, newCount, newType);
        }

        public ReadOnlySpan<Instruction> GetInstructions() => Buffer.AsSpan(0, ValidCount);

        public override readonly string ToString() =>
            $"FluxFormula<{typeof(TData).Name}> [Type: {Type}, Instructions: {ValidCount}]";
    }
}
