using UnityEngine;

namespace FluxFormula.Core
{
    /// <summary>
    /// 公式资产的 Unity 序列化容器。
    /// 存编译后的字节码 + 类型标记——Unity 完全可存储，
    /// 绕过开放泛型 ScriptableObject 的限制。
    /// 创建和加载由 <see cref="FormulaLibrary{TData, TDef}"/> 完成。
    /// </summary>
    public class FluxAsset : ScriptableObject
    {
        // ═══════════════════════════════════════════════
        // 核心：运行时从字节码重建公式
        // ═══════════════════════════════════════════════

        [SerializeField, HideInInspector]
        private byte[] _rawData;

        /// <summary>TDef 的程序集限定名，用于加载时跨程序集类型校验</summary>
        [SerializeField, HideInInspector]
        private string _typeId;

        /// <summary>格式版本号，用于未来格式迁移（如 "1.0.0"）</summary>
        [SerializeField, HideInInspector]
        private string _formatVersion = "1.0.0";

        // ═══════════════════════════════════════════════
        // 附加：编辑器创作信息（运行时完全不碰）
        // ═══════════════════════════════════════════════

        [SerializeField, HideInInspector]
        private string _source;

        [SerializeField, HideInInspector]
        private VariablePatternRule[] _variablePatterns;

        // ═══════════════════════════════════════════════
        // 公开属性
        // ═══════════════════════════════════════════════

        public string   TypeId            => _typeId;
        public string   FormatVersion     => _formatVersion;
        public string   Source            => _source;
        public int      RawDataLength     => _rawData?.Length ?? 0;
        public byte[]   RawData           => _rawData;

        /// <summary>从字节码头部直接读取指令数（不反序列化整个公式）</summary>
        public int      InstructionCount
        {
            get
            {
                if (_rawData == null || _rawData.Length < 4) return 0;
                return _rawData[0] | (_rawData[1] << 8) | (_rawData[2] << 16) | (_rawData[3] << 24);
            }
        }

        /// <summary>变量名列表，从字节码 VariableSlot 段实时解包</summary>
        public string[] VariableNames
        {
            get
            {
                if (_rawData == null || _rawData.Length < 13) return System.Array.Empty<string>();

                int offset = 0;
                int count = _rawData[offset] | (_rawData[offset + 1] << 8) | (_rawData[offset + 2] << 16) | (_rawData[offset + 3] << 24);
                offset += 5; // count(4) + type(1)
                int immCount = _rawData[offset] | (_rawData[offset + 1] << 8) | (_rawData[offset + 2] << 16) | (_rawData[offset + 3] << 24);
                offset += 4;
                int varSlotCount = _rawData[offset] | (_rawData[offset + 1] << 8) | (_rawData[offset + 2] << 16) | (_rawData[offset + 3] << 24);
                offset += 4;

                if (varSlotCount <= 0) return System.Array.Empty<string>();

                // 跳过指令段
                offset += count * 8;

                // 读变量槽
                var names = new string[varSlotCount];
                var enc = System.Text.Encoding.UTF8;
                for (int i = 0; i < varSlotCount; i++)
                {
                    int nameLen = _rawData[offset] | (_rawData[offset + 1] << 8) | (_rawData[offset + 2] << 16) | (_rawData[offset + 3] << 24);
                    offset += 4;
                    names[i] = enc.GetString(_rawData, offset, nameLen);
                    offset += nameLen + 4; // name bytes + slotIndex(4)
                }
                return names;
            }
        }

        /// <summary>语法解析规则（变量模式），编辑器用于重建 LexerConfig</summary>
        public VariablePatternRule[] VariablePatterns =>
            _variablePatterns ?? System.Array.Empty<VariablePatternRule>();

        // ═══════════════════════════════════════════════
        // 公开方法
        // ═══════════════════════════════════════════════

        /// <summary>
        /// 将编译后的公式字节码写入资产。
        /// typeId 应为 TDef.AssemblyQualifiedName，用于加载时类型校验。
        /// </summary>
        public void SetRawData<TData, TDef>(
            FluxFormula<TData, TDef> formula,
            string typeId,
            string source = null,
            VariablePatternRule[] variablePatterns = null)
            where TData : unmanaged
            where TDef : unmanaged, IFluxExprDefinition<TData>
        {
            _rawData          = formula.ToBytes();
            _typeId           = typeId;
            _source           = source;
            _variablePatterns = variablePatterns;
        }

        /// <summary>
        /// 从字节码重建泛型公式。类型参数须与 SetRawData 时的 TDef 一致。
        /// 注意：该方法信任调用方类型——类型不一致会导致运行时误读字节码。
        /// </summary>
        public FluxFormula<TData, TDef> Load<TData, TDef>()
            where TData : unmanaged
            where TDef : unmanaged, IFluxExprDefinition<TData>
        {
            if (_rawData == null || _rawData.Length == 0)
                return FluxFormula<TData, TDef>.Empty;

            return FluxFormula<TData, TDef>.FromBytes(_rawData);
        }
    }
}
