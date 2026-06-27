using FluxFormula.Core;
using UnityEngine;

namespace FluxFormula
{
    /// <summary>
    /// FluxFormula 项目级全局配置——ScriptableObject 容器。
    /// 放置于 <c>Resources/FluxConfig.asset</c> 即可在启动时自动加载。
    /// </summary>
    /// <remarks>
    /// <para>创建方式：右键 Project 窗口 → Create → FluxFormula → Config，
    /// 或手动创建后放入任意 Resources 目录。</para>
    ///
    /// <para>配置值在 <c>[RuntimeInitializeOnLoadMethod]</c> 时自动注入
    /// <see cref="FluxConfig.Current"/>。</para>
    /// </remarks>
    [CreateAssetMenu(menuName = "FluxFormula/Config", fileName = "FluxConfig", order = 100)]
    public class FluxConfigAsset : ScriptableObject
    {
        [Header("Cache")]
        [Tooltip("FormulaCache 哈希表槽位数。增大可减少碰撞，但增加内存占用。")]
        [Min(256)]
        public int formulaCacheCapacity = 256;

        [Tooltip("NativeBytecodeCache 哈希表槽位数。Jobs 路径中唯一公式种类数通常远小于实例数。")]
        [Min(64)]
        public int nativeBytecodeCacheCapacity = 64;

        [Header("Chain")]
        [Tooltip("链式公式合并阈值——链长超过此值时合并为原子公式。")]
        [Range(2, 64)]
        public int mergeThreshold = 8;

        [Header("File & Paths")]
        [Tooltip("Blob 二进制文件路径。留空使用默认路径 (StreamingAssets/flux.blob)。")]
        public string blobFilePath;

        [Tooltip("磁盘缓存目录——编译产物/中间文件的持久化路径。留空使用 Application.persistentDataPath。")]
        public string diskCacheDirectory;

        /// <summary>将 SO 配置写入 <see cref="FluxConfig.Current"/>。</summary>
        public void Apply()
        {
            FluxConfig.Set(new FluxConfig
            {
                FormulaCacheCapacity        = formulaCacheCapacity,
                NativeBytecodeCacheCapacity = nativeBytecodeCacheCapacity,
                MergeThreshold              = mergeThreshold,
                BlobFilePath                = string.IsNullOrEmpty(blobFilePath) ? null : blobFilePath,
                DiskCacheDirectory          = string.IsNullOrEmpty(diskCacheDirectory) ? null : diskCacheDirectory,
            });
        }

        // ═══════════════════════════════════════════════════════
        // 自动加载
        // ═══════════════════════════════════════════════════════

#if !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#endif
        private static void AutoLoad()
        {
            var asset = Resources.Load<FluxConfigAsset>("FluxConfig");
            asset?.Apply();
        }
    }
}
