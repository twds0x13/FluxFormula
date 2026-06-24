using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FluxFormula.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FluxFormula.Editor
{
    /// <summary>
    /// Blob 构建管线：扫描项目所有 FluxAsset，拼接为单一 blob，生成 C# 偏移表。
    /// </summary>
    /// <remarks>
    /// <para>产出（写入 <c>Assets/FluxFormula/Generated/</c>）：</para>
    /// <list type="bullet">
    ///   <item><c>BlobData.cs</c> — blob byte[] + <see cref="FluxBlob.Entry"/>[] 偏移表</item>
    ///   <item><c>BlobBootstrapper.cs</c> — 运行时自动调用 <see cref="FluxBlob.Initialize"/></item>
    ///   <item><c>FluxFormula.Generated.asmdef</c> — 引用 FluxFormula</item>
    /// </list>
    ///
    /// <para>触发时机：</para>
    /// <list type="bullet">
    ///   <item><c>FluxFormula &gt; Build Blob</c> 菜单手动触发</item>
    ///   <item>Player Build 前自动触发（<see cref="IPreprocessBuildWithReport"/>）</item>
    /// </list>
    /// </remarks>
    public static class FluxBlobBuilder
    {
        // ═══════════════════════════════════════════════════════
        // 常量
        // ═══════════════════════════════════════════════════════

        private const string GeneratedDir = "Assets/FluxFormula/Generated";
        private const string DataFilePath = "Assets/FluxFormula/Generated/BlobData.cs";
        private const string BootstrapperFilePath = "Assets/FluxFormula/Generated/BlobBootstrapper.cs";
        private const string AsmdefFilePath = "Assets/FluxFormula/Generated/FluxFormula.Generated.asmdef";
        private const string MenuPath = "FluxFormula/Build Blob";

        // ═══════════════════════════════════════════════════════
        // 菜单
        // ═══════════════════════════════════════════════════════

        [MenuItem(MenuPath, priority = 200)]
        public static void BuildFromMenu()
        {
            try
            {
                Build();
                EditorUtility.DisplayDialog("FluxBlob Builder",
                    "Blob built successfully.\n\n" +
                    $"Output: {GeneratedDir}",
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FluxBlobBuilder] Build failed: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog("FluxBlob Builder - Error",
                    $"Build failed:\n{ex.Message}",
                    "OK");
            }
        }

        [MenuItem("FluxFormula/Clear Blob", priority = 201)]
        public static void ClearFromMenu()
        {
            if (!Directory.Exists(GeneratedDir))
            {
                EditorUtility.DisplayDialog("Clear Blob", "No generated blob found.", "OK");
                return;
            }

            bool confirm = EditorUtility.DisplayDialog(
                "Clear Blob",
                $"Delete all generated files under:\n{GeneratedDir}\n\nRebuild via FluxFormula > Build Blob.",
                "Delete", "Cancel");

            if (!confirm) return;

            try
            {
                Directory.Delete(GeneratedDir, recursive: true);
                AssetDatabase.Refresh();
                Debug.Log($"[FluxBlobBuilder] Deleted: {GeneratedDir}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FluxBlobBuilder] Failed to delete generated files: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════
        // 构建
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// 执行完整的 blob 构建：扫描 FluxAsset → 拼接字节码 → 生成 C# 代码。
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

                entries.Add((hash, rawData));
            }

            if (entries.Count == 0)
            {
                Debug.LogWarning("[FluxBlobBuilder] No FluxAssets with bytecode found. Empty blob generated.");
                WriteEmpty();
                return 0;
            }

            // 2. 按哈希排序
            entries.Sort((a, b) =>
            {
                int cmp = a.hash.XxHash64.CompareTo(b.hash.XxHash64);
                if (cmp != 0) return cmp;
                return a.hash.FnvHash64.CompareTo(b.hash.FnvHash64);
            });

            // 3. 拼接 blob
            int totalBlobSize = 0;
            foreach (var (_, raw) in entries)
                totalBlobSize += raw.Length;

            var blob = new byte[totalBlobSize];
            var offsetTable = new FluxBlob.Entry[entries.Count];
            int currentOffset = 0;

            for (int i = 0; i < entries.Count; i++)
            {
                var (hash, raw) = entries[i];
                Buffer.BlockCopy(raw, 0, blob, currentOffset, raw.Length);

                offsetTable[i] = new FluxBlob.Entry(hash, currentOffset, raw.Length);
                currentOffset += raw.Length;
            }

            // 4. 确保输出目录存在
            if (!Directory.Exists(GeneratedDir))
                Directory.CreateDirectory(GeneratedDir);

            // 5. 写入生成文件
            WriteAsmdef();
            WriteDataFile(blob, offsetTable);
            WriteBootstrapper();

            // 6. 统计 .ff / .vff 数量
            int formulaCount = 0, vffCount = 0;
            foreach (var (_, raw) in entries)
            {
                if (VffFormat.IsVff(new ReadOnlySpan<byte>(raw)))
                    vffCount++;
                else
                    formulaCount++;
            }

            // 7. 刷新 AssetDatabase
            AssetDatabase.Refresh();

            var parts = new System.Collections.Generic.List<string>();
            if (formulaCount > 0) parts.Add($"{formulaCount} .ff");
            if (vffCount > 0) parts.Add($"{vffCount} .vff");
            Debug.Log($"[FluxBlobBuilder] Blob built: {string.Join(", ", parts)}, {totalBlobSize} bytes → {GeneratedDir}");
            return entries.Count;
        }

        // ═══════════════════════════════════════════════════════
        // 空构建
        // ═══════════════════════════════════════════════════════

        private static void WriteEmpty()
        {
            if (!Directory.Exists(GeneratedDir))
                Directory.CreateDirectory(GeneratedDir);

            WriteAsmdef();

            var dataSb = new StringBuilder();
            dataSb.AppendLine("// <auto-generated />");
            dataSb.AppendLine("// FluxFormula Blob Offset Table — no formulas found");
            dataSb.AppendLine();
            dataSb.AppendLine("using FluxFormula.Core;");
            dataSb.AppendLine();
            dataSb.AppendLine("namespace FluxFormula.Generated");
            dataSb.AppendLine("{");
            dataSb.AppendLine("    internal static class BlobData");
            dataSb.AppendLine("    {");
            dataSb.AppendLine("        internal static readonly byte[] Blob = System.Array.Empty<byte>();");
            dataSb.AppendLine();
            dataSb.AppendLine("        internal static readonly FluxBlob.Entry[] Entries =");
            dataSb.AppendLine("            System.Array.Empty<FluxBlob.Entry>();");
            dataSb.AppendLine("    }");
            dataSb.AppendLine("}");
            File.WriteAllText(DataFilePath, dataSb.ToString());

            WriteBootstrapper();
            AssetDatabase.Refresh();
        }

        // ═══════════════════════════════════════════════════════
        // 文件生成
        // ═══════════════════════════════════════════════════════

        private static void WriteAsmdef()
        {
            string json = "{\n" +
                "    \"name\": \"FluxFormula.Generated\",\n" +
                "    \"rootNamespace\": \"FluxFormula.Generated\",\n" +
                "    \"references\": [\n" +
                "        \"FluxFormula\"\n" +
                "    ],\n" +
                "    \"includePlatforms\": [],\n" +
                "    \"excludePlatforms\": [],\n" +
                "    \"allowUnsafeCode\": false,\n" +
                "    \"overrideReferences\": false,\n" +
                "    \"precompiledReferences\": [],\n" +
                "    \"autoReferenced\": true,\n" +
                "    \"defineConstraints\": [],\n" +
                "    \"versionDefines\": [],\n" +
                "    \"noEngineReferences\": false\n" +
                "}";

            File.WriteAllText(AsmdefFilePath, json);
        }

        private static void WriteDataFile(byte[] blob, FluxBlob.Entry[] offsetTable)
        {
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("// FluxFormula Blob Offset Table");
            sb.AppendLine($"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"// Formula count: {offsetTable.Length}");
            sb.AppendLine($"// Blob size: {blob.Length} bytes ({blob.Length / 1024.0:F1} KB)");
            sb.AppendLine();
            sb.AppendLine("using FluxFormula.Core;");
            sb.AppendLine();
            sb.AppendLine("namespace FluxFormula.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    internal static class BlobData");
            sb.AppendLine("    {");

            // ── blob byte[] ──
            sb.AppendLine($"        internal static readonly byte[] Blob = new byte[{blob.Length}]");
            sb.AppendLine("        {");

            const int bytesPerLine = 16;
            for (int i = 0; i < blob.Length; i += bytesPerLine)
            {
                sb.Append("           ");
                int lineEnd = Math.Min(i + bytesPerLine, blob.Length);
                for (int j = i; j < lineEnd; j++)
                {
                    sb.Append($"0x{blob[j]:X2}");
                    if (j < blob.Length - 1)
                        sb.Append(", ");
                }
                sb.AppendLine();
            }

            sb.AppendLine("        };");
            sb.AppendLine();

            // ── FluxBlob.Entry[] ──
            sb.AppendLine($"        internal static readonly FluxBlob.Entry[] Entries =");
            sb.AppendLine($"            new FluxBlob.Entry[{offsetTable.Length}]");
            sb.AppendLine("        {");

            for (int i = 0; i < offsetTable.Length; i++)
            {
                var e = offsetTable[i];
                sb.Append($"            new FluxBlob.Entry(" +
                    $"new DualHash64(0x{e.Hash.XxHash64:X16}UL, 0x{e.Hash.FnvHash64:X16}UL), " +
                    $"{e.Offset}, {e.Length})");
                if (i < offsetTable.Length - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
            }

            sb.AppendLine("        };");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            File.WriteAllText(DataFilePath, sb.ToString());
        }

        private static void WriteBootstrapper()
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine();
            sb.AppendLine("using FluxFormula.Core;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            sb.AppendLine("namespace FluxFormula.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    internal static class BlobBootstrapper");
            sb.AppendLine("    {");
            sb.AppendLine("        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]");
            sb.AppendLine("        internal static void Initialize()");
            sb.AppendLine("        {");
            sb.AppendLine("            FluxBlob.Initialize(BlobData.Blob, BlobData.Entries);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            File.WriteAllText(BootstrapperFilePath, sb.ToString());
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
