using System;
using FluxFormula.Core;

public static class InstructionExtensions
{
    /// <summary>
    /// 带标签的可读性增强版二进制显示
    /// </summary>
    public static string ToBinary(this Instruction ins)
    {
        return $"Op:{ByteToB(ins.OpCode)} | De:{ByteToB(ins.Dest)} | "
            + $"R0:{ByteToB(ins.Arg0)} R1:{ByteToB(ins.Arg1)} R2:{ByteToB(ins.Arg2)} "
            + $"R3:{ByteToB(ins.Arg3)} R4:{ByteToB(ins.Arg4)} R5:{ByteToB(ins.Arg5)}";
    }

    // 快速将字节转换为8位二进制
    private static string ByteToB(byte b) => Convert.ToString(b, 2).PadLeft(8, '0');
}
