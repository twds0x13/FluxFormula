using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FluxFormula.Core;
using UnityEngine;

namespace FluxFormula
{
    /// <summary>
    /// Mod blob 注册表发现器——反射扫描已加载程序集中实现 <see cref="IFluxBlobRegistry"/> 的类型。
    /// </summary>
    /// <remarks>
    /// <para>此文件在 <c>FluxFormula</c> 程序集中，不依赖 Addressables。
    /// 职责仅限于发现——加载由调用方负责（<c>FluxBlobAddressablesLoader</c> 或直接文件 I/O）。</para>
    ///
    /// <para>使用方式：
    /// <code>
    /// var registries = FluxBlobScanner.DiscoverAll();
    /// foreach (var r in registries)
    /// {
    ///     var data = await LoadBlobAsync(r.BlobKey);
    ///     FluxBlob.Load(data, r.GetEntries());
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    public static class FluxBlobScanner
    {
        private static readonly HashSet<string> _scannedAssemblies = new();

        /// <summary>已扫描的程序集数量</summary>
        public static int ScannedAssemblyCount => _scannedAssemblies.Count;

        /// <summary>
        /// 扫描所有已加载程序集中实现 <see cref="IFluxBlobRegistry"/> 的类型。
        /// 可多次调用（增量扫描——已扫描的程序集自动跳过）。
        /// </summary>
        /// <returns>本次新发现的 registry 列表</returns>
        public static List<IFluxBlobRegistry> DiscoverAll()
        {
            var registries = new List<IFluxBlobRegistry>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                if (!_scannedAssemblies.Add(assembly.FullName))
                    continue;

                if (!HasRegistryMarker(assembly))
                    continue;

                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (!typeof(IFluxBlobRegistry).IsAssignableFrom(type) ||
                            type.IsInterface || type.IsAbstract)
                            continue;

                        var registry = (IFluxBlobRegistry)Activator.CreateInstance(type);
                        if (registry.EntryCount == 0)
                            continue;

                        registries.Add(registry);
                    }
                }
                catch (ReflectionTypeLoadException ex)
                {
                    Debug.LogWarning($"[FluxBlobScanner] Skipping assembly {assembly.GetName().Name}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FluxBlobScanner] Error scanning assembly {assembly.GetName().Name}: {ex.Message}");
                }
            }

            return registries;
        }

        /// <summary>
        /// 重置扫描缓存——强制下次 DiscoverAll 重新扫描全部程序集。
        /// </summary>
        public static void ResetScanCache()
        {
            _scannedAssemblies.Clear();
        }

        // ═══════════════════════════════════════════════════════
        // 便捷：文件系统路径的 blob 加载（非 Addressables 降级路径）
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 从文件系统路径加载 blob 数据。若不使用 Addressables，
        /// 可将此作为 <c>BlobKey</c> 的解析方式。
        /// </summary>
        public static byte[] LoadBlobFromFile(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            if (File.Exists(key))
                return BlobFormat.ExtractBlobData(File.ReadAllBytes(key));

            string streamingPath = Path.Combine(Application.streamingAssetsPath, key);
            if (File.Exists(streamingPath))
                return BlobFormat.ExtractBlobData(File.ReadAllBytes(streamingPath));

            return null;
        }

        // ═══════════════════════════════════════════════════════
        // 内部
        // ═══════════════════════════════════════════════════════

        private static bool HasRegistryMarker(Assembly assembly)
        {
            try
            {
                return assembly.IsDefined(typeof(FluxBlobRegistryAssemblyAttribute), inherit: false);
            }
            catch
            {
                return false;
            }
        }
    }
}
