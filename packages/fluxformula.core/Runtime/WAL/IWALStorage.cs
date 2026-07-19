using System;

namespace FluxFormula.Core
{
    /// <summary>
    /// WAL 文件存储抽象。
    /// </summary>
    public interface IWALStorage
    {
        /// <summary>读取 WAL 文件的全部字节。文件不存在时返回 null。</summary>
        byte[] ReadAll();

        /// <summary>全量写入 WAL 文件。</summary>
        void Create(byte[] data);

        /// <summary>从文件头开始覆写指定长度。仅当覆写长度与已有 preamble 一致时安全。</summary>
        void OverwritePreamble(byte[] data);

        /// <summary>在文件末尾追加字节。</summary>
        void Append(byte[] data);

        /// <summary>文件是否已存在。</summary>
        bool Exists { get; }

        /// <summary>删除 WAL 文件。文件不存在时无操作。</summary>
        void Delete();
    }
}
