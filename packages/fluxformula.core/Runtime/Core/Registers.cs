using System;

namespace FluxFormula.Core
{
    /// <summary>
    /// 寄存器模型语义常量——项目中所有寄存器号引用的唯一来源。
    /// </summary>
    /// <remarks>
    /// <para><b>修改寄存器宽度（byte → ushort）的有序步骤：</b></para>
    /// <list type="number">
    /// <item><description><c>Registers.Max</c> — 更新为 <c>ushort.MaxValue</c>（上限约 65,535）</description></item>
    /// <item><description><c>Instruction.cs</c> — <c>Dest</c>、<c>Arg0..Arg5</c> 从 <c>byte</c> 改为 <c>ushort</c>；重排 <c>FieldOffset</c></description></item>
    /// <item><description><c>FluxCompiler.cs</c> — Register allocation casts: <c>(byte)</c> → <c>(ushort)</c></description></item>
    /// <item><description><c>FluxFormula.ToMultiplier/ToFormula/FindFreeRegister</c> — 寄存器比较类型同步更新</description></item>
    /// <item><description><c>FluxPlatform.MaxRegisters</c> — 更新为新区间的最大值</description></item>
    /// <item><description><c>FluxEvaluator.cs</c> — <c>stackalloc</c> 尺寸基于 <c>sizeof(TData) * MaxRegisters</c>，自动跟踪</description></item>
    /// <item><description><c>FluxJITCompiler.cs</c> — <c>ParameterExpression[]</c> 数量基于 <c>MaxRegisters</c>，自动跟踪</description></item>
    /// <item><description><c>FormulaFormat.InstructionSize</c> — 自动通过 <c>sizeof(Instruction)</c> 跟踪</description></item>
    /// <item><description>重新生成所有 blob（<c>FluxBlobBuilder.Build()</c>）——旧格式字节码与新版不兼容</description></item>
    /// <item><description>运行 152 tests 确认自举完成</description></item>
    /// </list>
    /// </remarks>
    public static class Registers
    {
        /// <summary>R0：错误哨兵寄存器。</summary>
        public const byte Error = 0;

        /// <summary>R1：输出总线寄存器——公式结果 / 链式链接返回值。</summary>
        public const byte Bus = 1;

        /// <summary>R2：首个可分配的通用寄存器。</summary>
        public const byte FirstAlloc = 2;

        /// <summary>
        /// 最大寄存器索引 = byte.MaxValue (255)。
        /// 与 <see cref="FluxPlatform.MaxRegisters"/> 保持一致。
        /// </summary>
        public const byte Max = 255;
    }
}
