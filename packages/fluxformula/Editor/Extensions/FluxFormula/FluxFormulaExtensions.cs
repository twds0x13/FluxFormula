using FluxFormula.Core;
using System;
using System.Text;

public static class FluxFormulaExtensions
{
    public static void Dump<TData, TDef>(this FluxFormula<TData, TDef> formula)
        where TData : unmanaged
        where TDef : unmanaged, IFluxJITDefinition<TData>
    {
        var insts = formula.Raw();
        UnityEngine.Debug.Log($"--- Formula Dump ({formula.Type}) ---");

        // 创建 StringBuilder 实例
        StringBuilder sb = new();

        for (int i = 0; i < insts.Length; i++)
        {
            sb.Clear();

            string prefix = $"[{i:D3}] ";

            sb.Append(prefix).AppendLine(insts[i].ToString());
            sb.Append(prefix).AppendLine(insts[i].ToBinary());
            sb.Append(prefix).Append(Convert.ToString((insts[i].Raw, 2)).PadLeft(64, '0'));

            UnityEngine.Debug.Log(sb.ToString());
        }
    }
}
