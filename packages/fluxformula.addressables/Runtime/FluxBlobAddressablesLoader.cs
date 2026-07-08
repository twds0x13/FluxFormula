using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluxFormula.Core;
using UnityEngine;
using UnityEngine.AddressableAssets;

// 此文件物理位于 fluxformula.addressables 包，namespace 声明为 FluxFormula.Core。
namespace FluxFormula.Core
{
    /// <summary>
    /// Blob 加载的统一入口——扫描所有 <see cref="IFluxBlobRegistry"/> 实现，
    /// 通过 Addressables 加载对应的 .bytes 文件，注册到 <see cref="FluxBlob"/>。
    /// </summary>
    /// <remarks>
    /// <para>游戏本体和所有 mod 走同一条路径：
    /// <list type="bullet">
    ///   <item><see cref="FluxBlobScanner.DiscoverAll"/> 反射发现所有 registry</item>
    ///   <item>对每个 registry，通过 Addressables 加载 .bytes TextAsset</item>
    ///   <item><see cref="FluxBlob.Load"/> 注册到 FormulaCache</item>
    /// </list>
    /// </para>
    ///
    /// <para>使用方式（游戏启动时调用一次，mod 加载后再次调用）：
    /// <code>
    /// var handles = await FluxBlobAddressablesLoader.ScanAndLoadAllAsync();
    /// </code>
    /// </para>
    /// </remarks>
    public static class FluxBlobAddressablesLoader
    {
        /// <summary>
        /// 扫描所有已加载程序集，发现 <see cref="IFluxBlobRegistry"/> 实现，
        /// 通过 Addressables 加载对应 blob，注册到 FormulaCache。
        /// 可多次调用（增量扫描——已扫描的程序集自动跳过）。
        /// </summary>
        /// <returns>本次新加载的 blob handles</returns>
        public static async ValueTask<List<FluxBlobHandle>> ScanAndLoadAllAsync()
        {
            var registries = FluxBlobScanner.DiscoverAll();
            var handles = new List<FluxBlobHandle>();

            foreach (var registry in registries)
            {
                var handle = await LoadBlobAsync(registry.BlobKey, registry.GetEntries());
                if (handle != null)
                    handles.Add(handle);
            }

            return handles;
        }

        /// <summary>
        /// 从 Addressables 加载单个 .bytes TextAsset，提取 data 段，
        /// 与给定的 entries 一起注册到 <see cref="FluxBlob"/>。
        /// </summary>
        /// <param name="addressablesKey">.bytes TextAsset 的 Addressables key</param>
        /// <param name="entries">编译期偏移表（来自 <c>BlobRegistry.GetEntries()</c>）</param>
        /// <returns>blob handle——用于后续卸载</returns>
        public static async ValueTask<FluxBlobHandle> LoadBlobAsync(
            string addressablesKey,
            BlobEntry[] entries)
        {
            if (string.IsNullOrEmpty(addressablesKey))
                throw new ArgumentNullException(nameof(addressablesKey));
            if (entries == null || entries.Length == 0)
                return null;

            var handle = Addressables.LoadAssetAsync<TextAsset>(addressablesKey);
            var asset = await handle.Task;

            if (asset == null)
            {
                Debug.LogError($"[FluxBlob] Failed to load blob via Addressables key: {addressablesKey}");
                return null;
            }

            byte[] blobData;
            try
            {
                blobData = BlobFormat.ExtractBlobData(asset.bytes);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FluxBlob] Failed to parse blob data from '{addressablesKey}': {ex.Message}");
                Addressables.Release(asset);
                return null;
            }

            var result = FluxBlob.Load(blobData, entries);
            Debug.Log($"[FluxBlob] Loaded {result.EntryCount} formulas via Addressables key: {addressablesKey}");
            return result;
        }
    }
}
