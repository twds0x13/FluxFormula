using System;
using System.Collections.Generic;
using System.IO;
using FluxFormula.Core;
using UnityEngine;

namespace FluxFormula
{
    /// <summary>
    /// /Mods/ 文件夹 mod 加载器——扫描 .bundle 文件、加载、发现公式注册表、
    /// 提取 blob 数据并注册到 <see cref="FluxBlob"/>。纯 UnityEngine，不依赖 Addressables。
    /// </summary>
    /// <remarks>
    /// <para>一次调用完成全部流程：
    /// <code>
    /// var handles = FluxBundleScanner.ScanAndLoad("/Mods/");
    /// // handles 可直接用于后续卸载: FluxBlob.Unload(handles[0])
    /// </code>
    /// </para>
    /// </remarks>
    public static class FluxBundleScanner
    {
        /// <summary>
        /// Bundle 搜索目录（相对于项目根目录）。默认 "Mods"。
        /// </summary>
        public static string BundleDirectory { get; set; } = "Mods";

        private static readonly List<AssetBundle> _loadedBundles = new();
        private static readonly HashSet<string> _loadedPaths = new();

        /// <summary>已加载的 bundle 列表</summary>
        public static IReadOnlyList<AssetBundle> LoadedBundles => _loadedBundles;

        /// <summary>
        /// 扫描目录中的 .bundle 文件，加载 bundle，发现程序集内的公式注册表，
        /// 提取 blob 数据并注册到 FormulaCache。已加载过的文件自动跳过。
        /// </summary>
        /// <returns>新加载的 blob handles</returns>
        public static List<FluxBlobHandle> ScanAndLoad(string directory = null)
        {
            var handles = new List<FluxBlobHandle>();
            string dir = directory ?? BundleDirectory;

            if (!Path.IsPathRooted(dir))
                dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", dir));

            if (!Directory.Exists(dir))
            {
                Debug.Log($"[FluxBundleScanner] Directory not found: {dir}");
                return handles;
            }

            // 1. 加载所有 .bundle 文件（触发程序集加载）
            var newBundles = new List<AssetBundle>();
            foreach (string path in Directory.GetFiles(dir, "*.bundle", SearchOption.AllDirectories))
            {
                string normalized = Path.GetFullPath(path);
                if (!_loadedPaths.Add(normalized)) continue;

                var bundle = AssetBundle.LoadFromFile(normalized);
                if (bundle == null)
                {
                    Debug.LogWarning($"[FluxBundleScanner] Failed to load: {normalized}");
                    continue;
                }

                _loadedBundles.Add(bundle);
                newBundles.Add(bundle);
                Debug.Log($"[FluxBundleScanner] Loaded bundle: {normalized}");
            }

            if (newBundles.Count == 0) return handles;

            // 2. 发现 bundle 内程序集里的注册表（FluxBlobScanner 增量扫描）
            var registries = FluxBlobScanner.DiscoverAll();

            // 3. 对每个注册表，从 bundle 中加载对应的 .bytes TextAsset
            foreach (var registry in registries)
            {
                if (registry.EntryCount == 0) continue;

                // BlobKey = bundle 内 TextAsset 名称
                TextAsset textAsset = null;
                foreach (var bundle in newBundles)
                {
                    textAsset = bundle.LoadAsset<TextAsset>(registry.BlobKey);
                    if (textAsset != null) break;
                }

                if (textAsset == null)
                {
                    Debug.LogWarning(
                        $"[FluxBundleScanner] TextAsset '{registry.BlobKey}' not found in any loaded bundle.");
                    continue;
                }

                byte[] blobData;
                try
                {
                    blobData = BlobFormat.ExtractBlobData(textAsset.bytes);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FluxBundleScanner] Failed to parse blob '{registry.BlobKey}': {ex.Message}");
                    Resources.UnloadAsset(textAsset);
                    continue;
                }

                var handle = FluxBlob.Load(blobData, registry.GetEntries());
                handles.Add(handle);
                Debug.Log($"[FluxBundleScanner] Loaded {handle.EntryCount} formulas: {registry.BlobKey}");
            }

            return handles;
        }

        /// <summary>
        /// 卸载所有已加载的 bundle。先调用 <see cref="FluxBlob.Unload"/> 释放对应的 handles。
        /// </summary>
        public static void UnloadAll(bool unloadAllLoadedObjects = true)
        {
            foreach (var bundle in _loadedBundles)
            {
                try { bundle.Unload(unloadAllLoadedObjects); }
                catch (Exception ex) { Debug.LogWarning($"[FluxBundleScanner] Error unloading bundle: {ex.Message}"); }
            }
            _loadedBundles.Clear();
            _loadedPaths.Clear();
        }
    }
}
