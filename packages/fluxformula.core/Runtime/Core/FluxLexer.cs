using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FluxFormula.Core
{
    // ================================================================
    // 规则定义类型
    // ================================================================

    /// <summary>变量模式规则：用前后缀描述未知数语法，如 ("{var:", "}") 或 ("[", "]")</summary>
    [Serializable]
    public struct VariablePatternRule
    {
        public string Prefix;
        public string Suffix;

        public VariablePatternRule(string prefix, string suffix)
        {
            Prefix = prefix;
            Suffix = suffix;
        }
    }

    /// <summary>运算符符号 → byte 操作码映射规则</summary>
    public readonly struct OperatorRule
    {
        public readonly string Symbol;
        public readonly byte Oper;
        /// <summary>函数调用左括号符号，如 "("。null 表示不使用括号语法。</summary>
        public readonly string BracketOpen;
        /// <summary>函数调用右括号符号，如 ")"。</summary>
        public readonly string BracketClose;

        public OperatorRule(string symbol, byte oper, string bracketOpen = null, string bracketClose = null)
        {
            Symbol       = symbol;
            Oper         = oper;
            BracketOpen  = bracketOpen;
            BracketClose = bracketClose;
        }
    }

    /// <summary>括号对映射规则</summary>
    public readonly struct BracketRule
    {
        public readonly string Open;
        public readonly string Close;
        public readonly byte LeftOper;
        public readonly byte RightOper;

        public BracketRule(string open, string close, byte leftOper, byte rightOper)
        {
            Open      = open;
            Close     = close;
            LeftOper  = leftOper;
            RightOper = rightOper;
        }
    }

    // ================================================================
    // 委托
    // ================================================================

    /// <summary>
    /// 字面量扫描器委托。尝试从源码指定位置开始扫描一个字面量（数字、标识符等）。
    /// </summary>
    /// <param name="src">源码跨度</param>
    /// <param name="pos">扫描起始位置</param>
    /// <param name="value">命中时为解析后的 TData 值；未命中为 default</param>
    /// <returns>命中时返回扫描结束位置（&gt;pos）；未命中返回 pos</returns>
    public delegate int LiteralScanner<TData>(ReadOnlySpan<char> src, int pos, out TData value);

    // ================================================================
    // 词法配置
    // ================================================================

    /// <summary>
    /// 纯配置驱动的词法规则表。
    /// 用户不需要理解内部实现 —— 填表即可。
    /// </summary>
    public class LexerConfig<TData>
        where TData : unmanaged
    {
        /// <summary>字面量（数字/标识符）匹配正则，如 @"\d+(\.\d+)?f?"</summary>
        /// <remarks>
        /// 此字段仅为文档参考。实际扫描行为由 <see cref="LiteralScanner"/> 控制——
        /// 未设置时使用内置的默认数字扫描器（等价于 <c>\d+(\.\d+)?[fF]?</c> + <see cref="LiteralParser"/>）。
        /// </remarks>
        public string LiteralPattern = @"\d+(\.\d+)?f?";

        /// <summary>字面量对应的操作码（如 FloatOp.Const）</summary>
        public byte LiteralOper;

        /// <summary>字面量字符串 → TData 转换函数</summary>
        public Func<string, TData> LiteralParser = _ => default;

        /// <summary>
        /// 自定义字面量扫描器。设置后替代内置的数字扫描器。
        /// 未设置（null）时回退到 <see cref="CreateDefaultNumberScanner"/>。
        /// </summary>
        /// <example>
        /// <code>
        /// // 十六进制整数扫描器
        /// config.LiteralScanner = (src, pos, out value) => {
        ///     value = default;
        ///     if (pos + 2 >= src.Length || src[pos] != '0' || src[pos+1] != 'x') return pos;
        ///     int end = pos + 2;
        ///     while (end < src.Length && IsHex(src[end])) end++;
        ///     if (end == pos + 2) return pos;
        ///     value = (TData)(object)Convert.ToInt32(src.Slice(pos, end-pos).ToString(), 16);
        ///     return end;
        /// };
        /// </code>
        /// </example>
        public LiteralScanner<TData> LiteralScanner;

        /// <summary>
        /// 创建内置默认数字扫描器：匹配 <c>\d+(\.\d+)?[fF]?</c> 格式，通过 <paramref name="parser"/> 转换。
        /// 等价于设置 <see cref="LiteralScanner"/> 为此返回值。
        /// </summary>
        public static LiteralScanner<TData> CreateDefaultNumberScanner(Func<string, TData> parser)
        {
            return (ReadOnlySpan<char> src, int pos, out TData value) =>
            {
                value = default;
                if (pos >= src.Length) return pos;

                char c = src[pos];
                if (!char.IsDigit(c)) return pos;

                int start = pos;

                // 整数部分
                while (pos < src.Length && char.IsDigit(src[pos])) pos++;

                // 可选小数部分
                if (pos < src.Length && src[pos] == '.')
                {
                    pos++;
                    while (pos < src.Length && char.IsDigit(src[pos])) pos++;
                }

                // 必须有至少一个数字
                if (pos == start) return start;

                // 可选 'f' / 'F' 后缀
                if (pos < src.Length && (src[pos] == 'f' || src[pos] == 'F'))
                    pos++;

                value = parser(src.Slice(start, pos - start).ToString());
                return pos;
            };
        }

        /// <summary>空白/注释正则（匹配的内容被跳过）</summary>
        public string WhitespacePattern = @"\s+";

        /// <summary>运算符映射列表（按长度自动降序匹配，无需手动排序）</summary>
        public List<OperatorRule> Operators = new();

        /// <summary>括号映射列表</summary>
        public List<BracketRule> Brackets = new();

        /// <summary>可隐式插入的运算符列表（如 Mul）。
        /// 若仅配置一个，遇到 2(3) 或 (a)(b) 时自动插入。
        /// 若配置多个且产生歧义（如 2 3），抛异常提醒用户显式书写。</summary>
        public List<byte> ImplicitOperators = new();

        /// <summary>变量（未知数）模式列表，用前后缀定义包裹语法。
        /// 如 new("{var:", "}") 匹配 {var:a}，new("[", "]") 匹配 [enemy.def]</summary>
        public List<VariablePatternRule> VariablePatterns = new();
    }

    // ================================================================
    // 词法分析结果
    // ================================================================

    /// <summary>
    /// 词法分析完整结果 —— Token 数组 + 变量名并行数组。
    /// 变量名数组与 Token 数组等长：非变量位置为 null，变量位置为捕获的名称。
    /// </summary>
    public readonly struct LexResult<TData>
        where TData : unmanaged
    {
        public readonly FluxToken<TData>[] Tokens;
        public readonly string[] VarNames;

        public LexResult(FluxToken<TData>[] tokens, string[] varNames)
        {
            Tokens = tokens;
            VarNames = varNames;
        }
    }

    // ================================================================
    // 词法分析器
    // ================================================================

    /// <summary>
    /// 词法分析器 —— 将字符串转换为 FluxToken 流。
    /// </summary>
    public class FluxLexer<TData>
        where TData : unmanaged
    {
        private readonly LexerConfig<TData> _config;
        private readonly LiteralScanner<TData> _literalScanner;
        // 预索引数组 — 手写扫描器直接遍历，零字典查找，零堆分配集合
        private readonly VariablePatternRule[] _varRules;
        private readonly string[] _opSymbols;
        private readonly byte[] _opOpers;
        private readonly string[] _brOpen, _brClose;
        private readonly byte[] _brLeftOpers, _brRightOpers;

        public FluxLexer(LexerConfig<TData> config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));

            if (config.LiteralParser == null)
                throw new ArgumentException(
                    "LexerConfig.LiteralParser must be set.");

            // 用户自定义扫描器优先，否则回退到内置数字扫描器
            _literalScanner = config.LiteralScanner
                ?? LexerConfig<TData>.CreateDefaultNumberScanner(config.LiteralParser);

            // ── 变量模式 ──
            _varRules = config.VariablePatterns.ToArray();

            // ── 运算符（按长度降序确保 '**' 匹配先于 '*'）──
            config.Operators.Sort((a, b) => b.Symbol.Length.CompareTo(a.Symbol.Length));
            _opSymbols = new string[config.Operators.Count];
            _opOpers   = new byte[config.Operators.Count];
            for (int i = 0; i < config.Operators.Count; i++)
            {
                _opSymbols[i] = config.Operators[i].Symbol;
                _opOpers[i]   = config.Operators[i].Oper;
            }

            // ── 括号 ──
            int bc = config.Brackets.Count;
            _brOpen   = new string[bc];
            _brClose  = new string[bc];
            _brLeftOpers  = new byte[bc];
            _brRightOpers = new byte[bc];
            for (int i = 0; i < bc; i++)
            {
                var br = config.Brackets[i];
                _brOpen[i]   = br.Open;
                _brClose[i]  = br.Close;
                _brLeftOpers[i]  = br.LeftOper;
                _brRightOpers[i] = br.RightOper;
            }
        }

        /// <summary>解析字符串为 LexResult（Token 数组 + 变量名并行数组）</summary>
        public LexResult<TData> Lex(string source)
        {
            if (string.IsNullOrEmpty(source))
                return new LexResult<TData>(
                    Array.Empty<FluxToken<TData>>(),
                    Array.Empty<string>());

            int maxTokens = source.Length;
            var tokens   = new FluxToken<TData>[maxTokens];
            var varNames = new string[maxTokens];
            int tokenCount = 0;
            int pos = 0;
            ReadOnlySpan<char> src = source.AsSpan();

            while (pos < src.Length)
            {
                // ── 跳过空白（空格、tab、换行、回车等）──
                if (char.IsWhiteSpace(src[pos])) { pos++; continue; }

                // ── 尝试匹配字面量（数字）──
                int litEnd = TryScanLiteral(src, pos, out TData litVal);
                if (litEnd > pos)
                {
                    tokens[tokenCount] = new FluxToken<TData>
                        { Oper = _config.LiteralOper, Data = litVal };
                    varNames[tokenCount] = null;
                    tokenCount++;
                    pos = litEnd;
                    continue;
                }

                // ── 尝试匹配变量模式 ──
                int varEnd = TryScanVariable(src, pos, out string varName);
                if (varEnd > pos)
                {
                    tokens[tokenCount] = new FluxToken<TData>
                        { Oper = _config.LiteralOper, Data = default };
                    varNames[tokenCount] = varName;
                    tokenCount++;
                    pos = varEnd;
                    continue;
                }

                // ── 尝试匹配运算符（已按长度降序排列）──
                int opEnd = TryScanOperator(src, pos, out byte op);
                if (opEnd > pos)
                {
                    tokens[tokenCount] = new FluxToken<TData> { Oper = op };
                    varNames[tokenCount] = null;
                    tokenCount++;
                    pos = opEnd;
                    continue;
                }

                // ── 尝试匹配括号 ──
                int brEnd = TryScanBracket(src, pos, out byte brOp);
                if (brEnd > pos)
                {
                    tokens[tokenCount] = new FluxToken<TData> { Oper = brOp };
                    varNames[tokenCount] = null;
                    tokenCount++;
                    pos = brEnd;
                    continue;
                }

                // ── 无法识别 ──
                int rem = src.Length - pos;
                throw new FormatException(
                    $"Lexer error at position {pos}: unexpected '{src.Slice(pos, Math.Min(rem, 20)).ToString()}'");
            }

            var resultTokens   = new FluxToken<TData>[tokenCount];
            var resultVarNames = new string[tokenCount];
            Array.Copy(tokens, resultTokens, tokenCount);
            Array.Copy(varNames, resultVarNames, tokenCount);

            // ── 隐式运算符插入 ────────────────────────
            if (_config.ImplicitOperators.Count > 0)
            {
                int maxResolved = resultTokens.Length * 2;
                var resolvedTokens   = new FluxToken<TData>[maxResolved];
                var resolvedVarNames = new string[maxResolved];
                int resolvedCount = 0;
                for (int i = 0; i < resultTokens.Length; i++)
                {
                    resolvedTokens[resolvedCount]   = resultTokens[i];
                    resolvedVarNames[resolvedCount] = resultVarNames[i];
                    resolvedCount++;
                    if (i + 1 >= resultTokens.Length) break;

                    if (IsJuxtaposition(resultTokens[i], resultTokens[i + 1]))
                    {
                        if (_config.ImplicitOperators.Count == 1)
                        {
                            resolvedTokens[resolvedCount] = new FluxToken<TData>
                                { Oper = _config.ImplicitOperators[0] };
                            resolvedVarNames[resolvedCount] = null;
                            resolvedCount++;
                        }
                        else
                        {
                            throw new FormatException(
                                $"Ambiguous implicit operator between '{resultTokens[i]}' and '{resultTokens[i + 1]}'. " +
                                $"Use explicit operator.");
                        }
                    }
                }
                resultTokens   = new FluxToken<TData>[resolvedCount];
                resultVarNames = new string[resolvedCount];
                Array.Copy(resolvedTokens, resultTokens, resolvedCount);
                Array.Copy(resolvedVarNames, resultVarNames, resolvedCount);
            }

            return new LexResult<TData>(resultTokens, resultVarNames);
        }

        // ── 扫描辅助方法 ────────────────────────────

        /// <summary>
        /// 尝试匹配一个字面量。优先使用 <see cref="LexerConfig{TData}.LiteralScanner"/>，
        /// 未设置时回退到内置默认数字扫描器（<c>\d+(\.\d+)?[fF]?</c>）。
        /// </summary>
        private int TryScanLiteral(ReadOnlySpan<char> src, int pos, out TData value)
        {
            return _literalScanner(src, pos, out value);
        }

        /// <summary>尝试匹配一个变量模式</summary>
        private int TryScanVariable(ReadOnlySpan<char> src, int pos, out string varName)
        {
            varName = null;
            for (int i = 0; i < _varRules.Length; i++)
            {
                var rule = _varRules[i];
                var prefixSpan = rule.Prefix.AsSpan();
                var suffixSpan = rule.Suffix.AsSpan();

                if (!src.Slice(pos).StartsWith(prefixSpan)) continue;

                int bodyStart = pos + prefixSpan.Length;
                int bodyEnd;

                if (suffixSpan.IsEmpty)
                {
                    // 无后缀：匹配 \w+ (字母、数字、下划线)
                    bodyEnd = bodyStart;
                    while (bodyEnd < src.Length && IsWordChar(src[bodyEnd])) bodyEnd++;
                    if (bodyEnd == bodyStart) continue; // 需要至少一个字符
                }
                else
                {
                    // 有后缀：匹配 .+? 直到后缀出现
                    bodyEnd = bodyStart;
                    while (bodyEnd <= src.Length - suffixSpan.Length)
                    {
                        if (src.Slice(bodyEnd).StartsWith(suffixSpan))
                            break;
                        bodyEnd++;
                    }
                    if (bodyEnd > src.Length - suffixSpan.Length) continue;
                }

                varName = src.Slice(bodyStart, bodyEnd - bodyStart).ToString();
                return suffixSpan.IsEmpty ? bodyEnd : bodyEnd + suffixSpan.Length;
            }
            return pos;
        }

        /// <summary>尝试匹配一个运算符（已按长度降序）</summary>
        private int TryScanOperator(ReadOnlySpan<char> src, int pos, out byte op)
        {
            op = 0;
            for (int i = 0; i < _opSymbols.Length; i++)
            {
                if (src.Slice(pos).StartsWith(_opSymbols[i].AsSpan()))
                {
                    op = _opOpers[i];
                    return pos + _opSymbols[i].Length;
                }
            }
            return pos;
        }

        /// <summary>尝试匹配一个括号</summary>
        private int TryScanBracket(ReadOnlySpan<char> src, int pos, out byte brOp)
        {
            brOp = 0;
            for (int i = 0; i < _brOpen.Length; i++)
            {
                if (src.Slice(pos).StartsWith(_brOpen[i].AsSpan()))
                {
                    brOp = _brLeftOpers[i];
                    return pos + _brOpen[i].Length;
                }
                if (src.Slice(pos).StartsWith(_brClose[i].AsSpan()))
                {
                    brOp = _brRightOpers[i];
                    return pos + _brClose[i].Length;
                }
            }
            return pos;
        }

        /// <summary>检查字符是否为字母、数字或下划线（\w）</summary>
        private static bool IsWordChar(char c) =>
            char.IsLetterOrDigit(c) || c == '_';

        /// <summary>从匹配文本提取变量名：去掉前缀和后缀</summary>
        private static string ExtractVarName(string matched, VariablePatternRule rule)
        {
            int start = rule.Prefix.Length;
            int len   = matched.Length - rule.Prefix.Length - rule.Suffix.Length;
            if (len <= 0) return matched;
            return matched.Substring(start, len);
        }

        /// <summary>判断两个相邻 Token 是否需要隐式运算符。
        /// 内联数组扫描替代 HashSet：括号对数极少（1-3），线性扫描零堆分配且更快。</summary>
        private bool IsJuxtaposition(FluxToken<TData> left, FluxToken<TData> right)
        {
            bool leftEnd = left.Oper == _config.LiteralOper
                        || IsRightBracket(left.Oper);

            bool rightStart = right.Oper == _config.LiteralOper
                           || IsLeftBracket(right.Oper);

            return leftEnd && rightStart;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsLeftBracket(byte op)
        {
            for (int i = 0; i < _brLeftOpers.Length; i++)
                if (_brLeftOpers[i] == op) return true;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsRightBracket(byte op)
        {
            for (int i = 0; i < _brRightOpers.Length; i++)
                if (_brRightOpers[i] == op) return true;
            return false;
        }
    }
}
