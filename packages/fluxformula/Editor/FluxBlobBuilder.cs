using System;
using System.Collections.Generic;
using System.IO;
using FluxFormula.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FluxFormula.Editor
{
    /// <summary>
    /// Blob 构建管线：扫描项目所有 FluxAsset，拼接为单一二进制 .bytes 文件。
    /// </summary>
    /// <remarks>
    /// <para>产出：<c>Assets/StreamingAssets/flux.bytes</c>（.bytes 扩展名确保 Unity 原生导入为 TextAsset）</para>
    ///
    /// <para>运行时：source generator 读取 .bytes 文件 header + entry table，
    /// 生成 <c>BlobRegistry.g.cs</c>（编译期偏移表常量）。
    /// <c>FluxBlobAddressablesLoader</c> 或 <c>FluxBundleScanner</c> 负责加载 blob 数据
    /// 并调用 <c>FluxBlob.Load()</c>。</para>
    ///
    /// <para>触发时机：
    /// <list type="bullet">
    ///   <item><c>FluxFormula &gt; Build Blob</c> 菜单手动触发</item>
    ///   <item>Player Build 前自动触发（<see cref="IPreprocessBuildWithReport"/>）</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static class FluxBlobBuilder
    {
        // ═══════════════════════════════════════════════════════
        // 常量
        // ═══════════════════════════════════════════════════════

        private const string DefaultBlobDir = "Assets/StreamingAssets";
        /// <summary>.bytes 扩展名确保 Unity 原生导入为 TextAsset。</summary>
        private const string DefaultBlobFileName = "flux.bytes";
        private const string MenuPath = "FluxFormula/Build Blob";

        // ═══════════════════════════════════════════════════════
        // 菜单
        // ═══════════════════════════════════════════════════════

        [MenuItem(MenuPath, priority = 200)]
        public static void BuildFromMenu()
        {
            try
            {
                int count = Build();
                EditorUtility.DisplayDialog("FluxBlob Builder",
                    $"Blob built successfully: {count} formulas.\n\nOutput: {GetOutputPath()}",
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FluxBlobBuilder] Build failed: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog("FluxBlob Builder - Error",
                    $"Build failed:\n{ex.Message}", "OK");
            }
        }

        [MenuItem("FluxFormula/Clear Blob", priority = 201)]
        public static void ClearFromMenu()
        {
            string path = GetOutputPath();
            if (!File.Exists(path))
            {
                EditorUtility.DisplayDialog("Clear Blob", $"No blob file found at:\n{path}", "OK");
                return;
            }

            bool confirm = EditorUtility.DisplayDialog(
                "Clear Blob",
                $"Delete blob file at:\n{path}\n\nRebuild via FluxFormula > Build Blob.",
                "Delete", "Cancel");

            if (!confirm) return;

            try
            {
                File.Delete(path);
                // 也删除同目录的 .meta 文件
                string metaPath = path + ".meta";
                if (File.Exists(metaPath))
                    File.Delete(metaPath);
                AssetDatabase.Refresh();
                Debug.Log($"[FluxBlobBuilder] Deleted: {path}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FluxBlobBuilder] Failed to delete blob file: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════
        // 构建
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 执行完整的 blob 构建：扫描 FluxAsset → 拼接字节码 → 写入二进制 .bytes 文件。
        /// </summary>
        /// <returns>生成的公式条目数</returns>
        public static int Build()
        {
            // 1. 扫描所有 FluxAsset
            string[] guids = AssetDatabase.FindAssets("t:FluxAsset");
            var entries = new List<(DualHash64 hash, byte[] rawData)>();
            var seenHashes = new HashSet<DualHash64>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<FluxAsset>(path);

                if (asset == null || asset.RawData == null || asset.RawData.Length == 0)
                {
                    Debug.LogWarning($"[FluxBlobBuilder] Skipping FluxAsset with no bytecode: {path}");
                    continue;
                }

                var rawData = asset.RawData;
                var hash = DualHash64.Compute(new ReadOnlySpan<byte>(rawData));

                if (!seenHashes.Add(hash))
                {
                    Debug.LogWarning($"[FluxBlobBuilder] Duplicate hash — identical formula content. Skipping: {path}");
                    continue;
                }

                // 根据配置决定是否压缩
                if (FluxConfig.Current.CompressBlob)
                {
                    byte[] stored = FluxCompression.Compress(rawData);
                    entries.Add((hash, stored));
                }
                else
                {
                    entries.Add((hash, rawData));
                }
            }

            if (entries.Count == 0)
            {
                Debug.LogWarning("[FluxBlobBuilder] No FluxAssets with bytecode found. Empty blob written.");
                WriteEmptyBlob();
                return 0;
            }

            // 2. 按哈希排序
            entries.Sort((a, b) =>
            {
                int cmp = a.hash.XxHash64.CompareTo(b.hash.XxHash64);
                if (cmp != 0) return cmp;
                return a.hash.FnvHash64.CompareTo(b.hash.FnvHash64);
            });

            // 3. 拼接 blob data 段并构建 BlobEntry[]
            int totalBlobSize = 0;
            foreach (var (_, raw) in entries)
                totalBlobSize += raw.Length;

            var blobData = new byte[totalBlobSize];
            var blobEntries = new BlobEntry[entries.Count];
            int currentOffset = 0;

            for (int i = 0; i < entries.Count; i++)
            {
                var (hash, raw) = entries[i];
                Buffer.BlockCopy(raw, 0, blobData, currentOffset, raw.Length);

                blobEntries[i] = new BlobEntry(hash, currentOffset, raw.Length);
                currentOffset += raw.Length;
            }

            // 4. 写入 .bytes 文件
            bool compressed = FluxConfig.Current.CompressBlob;
            WriteBlobFile(blobData, blobEntries, compressed);

            // 5. 统计 .ff / .vff 数量
            int formulaCount = 0, vffCount = 0;
            foreach (var (_, raw) in entries)
            {
                if (VffFormat.IsVff(new ReadOnlySpan<byte>(raw)))
                    vffCount++;
                else
                    formulaCount++;
            }

            // 6. 刷新 AssetDatabase
            AssetDatabase.Refresh();

            var parts = new List<string>();
            if (formulaCount > 0) parts.Add($"{formulaCount} .ff");
            if (vffCount > 0) parts.Add($"{vffCount} .vff");
            string compressionNote = compressed ? " [Brotli]" : "";
            Debug.Log($"[FluxBlobBuilder] Blob written: {string.Join(", ", parts)}, {totalBlobSize} bytes{compressionNote} → {GetOutputPath()}");
            return entries.Count;
        }

        // ═══════════════════════════════════════════════════════
        // 文件写入
        // ═══════════════════════════════════════════════════════

        private static void WriteBlobFile(byte[] blobData, BlobEntry[] blobEntries, bool compressed)
        {
            string filePath = GetOutputPath();
            EnsureDirectory(Path.GetDirectoryName(filePath));

            int headerSize = BlobFormat.HeaderSize;
            int entryTableSize = blobEntries.Length * BlobFormat.EntrySize;
            int totalSize = headerSize + entryTableSize + blobData.Length;

            byte[] fileBytes = new byte[totalSize];
            var span = fileBytes.AsSpan();

            // Header
            BlobFormat.WriteHeader(span, blobEntries.Length, blobData.Length, compressed);

            // Entry table
            int entryOffset = headerSize;
            for (int i = 0; i < blobEntries.Length; i++)
            {
                var e = blobEntries[i];
                BlobFormat.WriteEntry(
                    span.Slice(entryOffset),
                    e.Hash.XxHash64,
                    e.Hash.FnvHash64,
                    e.Offset,
                    e.Length);
                entryOffset += BlobFormat.EntrySize;
            }

            // Blob data
            Buffer.BlockCopy(blobData, 0, fileBytes, entryOffset, blobData.Length);

            File.WriteAllBytes(filePath, fileBytes);
        }

        private static void WriteEmptyBlob()
        {
            string filePath = GetOutputPath();
            EnsureDirectory(Path.GetDirectoryName(filePath));

            byte[] fileBytes = new byte[BlobFormat.HeaderSize];
            var span = fileBytes.AsSpan();
            BlobFormat.WriteHeader(span, entryCount: 0, blobDataSize: 0, compressed: false);

            File.WriteAllBytes(filePath, fileBytes);
            AssetDatabase.Refresh();
        }

        // ═══════════════════════════════════════════════════════
        // 路径
        // ═══════════════════════════════════════════════════════

        private static string GetOutputPath()
        {
            string configured = FluxConfig.Current.BlobFilePath;
            if (!string.IsNullOrEmpty(configured))
                return configured;
            return Path.Combine(DefaultBlobDir, DefaultBlobFileName);
        }

        private static void EnsureDirectory(string dir)
        {
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        // ═══════════════════════════════════════════════════════
        // 构建预处理器
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 在 Player Build 前自动触发 blob 构建。
        /// </summary>
        public class BuildPreprocessor : IPreprocessBuildWithReport
        {
            public int callbackOrder => -100;

            public void OnPreprocessBuild(BuildReport report)
            {
                Debug.Log("[FluxBlobBuilder] Auto-building formula blob before player build...");
                int count = Build();
                Debug.Log($"[FluxBlobBuilder] Blob built with {count} formulas.");
            }
        }
    }
}
