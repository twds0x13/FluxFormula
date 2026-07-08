using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FluxFormula.Editor
{
    /// <summary>
    /// 在 Player Build 前自动将 .bytes 文件注册到 Addressables 系统。
    /// </summary>
    /// <remarks>
    /// <para>执行顺序：<see cref="FluxBlobBuilder.BuildPreprocessor"/> (callbackOrder = -100)
    /// → 此 hook (callbackOrder = -90)，保证 .bytes 文件先生成再注册。</para>
    /// </remarks>
    public class FluxBlobBuildHook : IPreprocessBuildWithReport
    {
        public int callbackOrder => -90;

        public void OnPreprocessBuild(BuildReport report)
        {
            string blobPath = FluxConfig.Current.BlobFilePath;
            if (string.IsNullOrEmpty(blobPath))
                blobPath = "Assets/StreamingAssets/flux.bytes";

            if (!File.Exists(blobPath))
            {
                Debug.LogWarning($"[FluxBlobBuildHook] Blob file not found at: {blobPath}. Skipping Addressables registration.");
                return;
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogWarning("[FluxBlobBuildHook] No AddressableAssetSettings found. Skipping.");
                return;
            }

            string guid = AssetDatabase.AssetPathToGUID(blobPath);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogWarning($"[FluxBlobBuildHook] Could not resolve GUID for: {blobPath}");
                return;
            }

            var entry = settings.FindAssetEntry(guid);
            if (entry == null)
            {
                entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup,
                    readOnly: false, postEvent: false);
                if (entry != null)
                {
                    entry.address = Path.GetFileNameWithoutExtension(blobPath);
                    Debug.Log($"[FluxBlobBuildHook] Registered blob in Addressables: '{entry.address}' → {blobPath}");
                }
            }
        }
    }
}
