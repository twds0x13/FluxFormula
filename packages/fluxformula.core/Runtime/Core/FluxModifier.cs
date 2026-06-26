using System;
using System.Runtime.CompilerServices;

namespace FluxFormula.Core
{
    /// <summary>
    /// 修饰符公式——缺少第一操作数的半成品，只能被 <see cref="Connect"/> 串联
    /// 或通过 <see cref="ToFormula"/> 转为完整公式。
    /// 无法独立求值（没有 Instantiate / Run）。
    /// </summary>
    /// <typeparam name="TData">公式计算的数据类型</typeparam>
    /// <typeparam name="TDef">定义体（操作符语义实现）</typeparam>
    public readonly struct FluxModifier<TData, TDef>
        where TData : unmanaged
        where TDef : unmanaged, IFluxJITDefinition<TData>
    {
        internal readonly FluxFormula<TData, TDef> Inner;

        internal FluxModifier(FluxFormula<TData, TDef> inner)
        {
            Inner = inner;
        }

        /// <summary>空 Modifier（Count=0），Connect 的单位元</summary>
        public static FluxModifier<TData, TDef> Empty =>
            new(FluxFormula<TData, TDef>.EmptyModifier);

        // ── 属性（委托给 Inner）──

        public int Count => Inner.Count;
        public int ImmediateCount => Inner.ImmediateCount;
        public VariableSlot[] VariableSlots => Inner.VariableSlots;
        public byte MaxRegister => Inner.MaxRegister;

        /// <summary>内部类型标记（始终为 Modifier）。测试用。</summary>
        internal FluxType Type => Inner.Type;

        // ── Modifier → Formula ──

        /// <summary>
        /// Modifier→Formula：插入命名变量替代 R1 输入。
        /// </summary>
        public FluxFormula<TData, TDef> ToFormula(string varName)
        {
            return Inner.ToFormula(varName);
        }

        // ── Connect ──

        /// <summary>
        /// 将两个 Modifier 串联，返回 <see cref="FluxChain{TData, TDef}"/>。
        /// 前者的 R1 输出流入后者的首操作数位置。
        /// 结果仍为 Modifier 链——仍然缺少第一操作数，
        /// 需通过 <see cref="FluxChain{TData, TDef}.ToAtomic"/> 或连接至 Formula 后才能求值。
        /// </summary>
        public FluxChain<TData, TDef> Connect(FluxModifier<TData, TDef> next)
        {
            if (Count == 0)
                return new FluxChain<TData, TDef>(new[] { next.Inner.ToLink() });
            if (next.Count == 0)
                return new FluxChain<TData, TDef>(new[] { Inner.ToLink() });

            return new FluxChain<TData, TDef>(
                FluxChain<TData, TDef>.ChainConnect(
                    new[] { Inner.ToLink() }, new[] { next.Inner.ToLink() }));
        }

        // ── 序列化 / 字节码访问 ──

        /// <summary>返回底层指令跨度。零分配。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<Instruction> Raw() => Inner.Raw();

        /// <summary>序列化为字节数组。</summary>
        public readonly byte[] ToBytes() => Inner.ToBytes();

        /// <summary>计算字节码哈希。</summary>
        public readonly DualHash64 GetByteHash() => Inner.GetByteHash();

        /// <summary>
        /// 从字节数组反序列化 Modifier。
        /// </summary>
        public static FluxModifier<TData, TDef> FromBytes(byte[] data)
        {
            var formula = FluxFormula<TData, TDef>.FromBytes(data);
            return new FluxModifier<TData, TDef>(formula);
        }

        /// <summary>
        /// 从只读字节跨度反序列化 Modifier。
        /// </summary>
        public static FluxModifier<TData, TDef> FromBytes(ReadOnlySpan<byte> data)
        {
            var formula = FluxFormula<TData, TDef>.FromBytes(data);
            return new FluxModifier<TData, TDef>(formula);
        }

        public override readonly string ToString() =>
            $"FluxModifier<{typeof(TData).Name}, {typeof(TDef).Name}> [Instructions: {Count}]";
    }
}
