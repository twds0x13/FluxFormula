using System;

namespace FluxFormula.Core
{
    /// <summary>
    /// 标记程序集包含 <see cref="IFluxBlobRegistry"/> 实现。
    /// Source generator 产出 <c>BlobRegistry.g.cs</c> 时自动添加此 attribute。
    /// </summary>
    /// <remarks>
    /// <para><c>FluxBlobScanner</c> 通过此 attribute 快速筛选需扫描的程序集，
    /// 避免对所有已加载程序集做完整的 <c>GetTypes()</c> 遍历。</para>
    ///
    /// <para>用法（由 source generator 自动生成）：
    /// <code>
    /// [assembly: FluxFormula.Core.FluxBlobRegistryAssembly]
    /// </code>
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public sealed class FluxBlobRegistryAssemblyAttribute : Attribute
    {
    }
}
