using System;

namespace FluxFormula.Core
{
    /// <summary>
    /// 编译缓存提供商接口。
    /// </summary>
    /// <remarks>
    /// <para>定义四组核心操作：字节码 和 JIT delegate 的 写/读。</para>
    /// <para>用户在 min 环境内可实现此接口以自定义缓存逻辑（如磁盘持久化、分布式缓存）；
    /// 也可直接使用内置的 <see cref="FormulaCache"/> 作为最简实现。</para>
    /// <para>设计约束：</para>
    /// <list type="bullet">
    ///   <item>接口方法不使用指针类型（<c>byte*</c>），改用 <see cref="IntPtr"/>：实现者不需要 unsafe 上下文</item>
    ///   <item>Delegate 通过 <see cref="System.Runtime.InteropServices.GCHandle"/> 转 IntPtr 存储：调用方负责创建/释放 GCHandle</item>
    ///   <item>缓存的生命周期管理（指针有效期、GCHandle 存活期）由实现者负责</item>
    /// </list>
    /// </remarks>
    public interface IFluxCacheProvider
    {
        // ═══════════════════════════════════════════════════════
        // 字节码缓存
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 按双重哈希查找缓存的字节码。
        /// </summary>
        /// <param name="key">字节码的 DualHash64</param>
        /// <param name="ptr">命中时为字节码起始指针；未命中时为 <see cref="IntPtr.Zero"/></param>
        /// <param name="length">命中时为字节码长度（byte count）；未命中时为 0</param>
        /// <returns>true 且 ptr/len 有效；false 表示缓存未命中</returns>
        bool TryGet(DualHash64 key, out IntPtr ptr, out int length);

        /// <summary>
        /// 将字节码写入缓存。
        /// 若同一 key 已存在则更新值。
        /// </summary>
        void Put(DualHash64 key, IntPtr ptr, int length);

        // ═══════════════════════════════════════════════════════
        // JIT delegate 缓存
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 按双重哈希查找缓存的 JIT delegate。
        /// </summary>
        /// <param name="key">公式字节码的 DualHash64</param>
        /// <param name="gcHandle">命中时为 GCHandle 转 IntPtr（调用 GCHandle.FromIntPtr 取回 delegate）；未命中为 Zero</param>
        /// <returns>true 表示命中</returns>
        bool TryGetDelegate(DualHash64 key, out IntPtr gcHandle);

        /// <summary>
        /// 将 JIT delegate 写入缓存。
        /// 调用方先创建 GCHandle.Alloc(func)，然后 GCHandle.ToIntPtr(handle) 传入。
        /// </summary>
        /// <param name="key">公式字节码的 DualHash64</param>
        /// <param name="gcHandle">GCHandle.Alloc(func).ToIntPtr() 的结果</param>
        void PutDelegate(DualHash64 key, IntPtr gcHandle);
    }
}
