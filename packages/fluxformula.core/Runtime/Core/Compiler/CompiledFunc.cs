using FluxFormula.Core;

namespace FluxFormula.Compiler
{
    /// <summary>
    /// JIT / IL 编译产出的委托签名——接收立即数数据缓冲区，返回求值结果。
    /// 由 <see cref="FluxExprCompiler{TData, TDef}"/> 和 <see cref="FluxILCompiler{TData, TDef}"/>
    /// 共享，通过 <see cref="FluxFormula.Core.FormulaCache"/> 缓存。
    /// </summary>
    public delegate TData CompiledFunc<TData>(Instruction[] dataBuffer)
        where TData : unmanaged;
}
