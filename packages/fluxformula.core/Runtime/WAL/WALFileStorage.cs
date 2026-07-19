using System;
using System.IO;

namespace FluxFormula.Core
{
    public sealed class WALFileStorage : IWALStorage
    {
        private readonly string _filePath;

        public WALFileStorage(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath))
                throw new ArgumentException("Directory path is required.", nameof(directoryPath));
            Directory.CreateDirectory(directoryPath);
            _filePath = Path.Combine(directoryPath, "flux.wal");
        }

        public bool Exists => File.Exists(_filePath);

        public byte[] ReadAll()
        {
            if (!File.Exists(_filePath)) return null;
            return File.ReadAllBytes(_filePath);
        }

        public void Create(byte[] data)
        {
            string tmp = _filePath + ".tmp";
            File.WriteAllBytes(tmp, data);
            if (File.Exists(_filePath))
                File.Delete(_filePath);
            File.Move(tmp, _filePath);
        }

        public void OverwritePreamble(byte[] data)
        {
            using var fs = new FileStream(_filePath, FileMode.Open,
                FileAccess.Write, FileShare.Read);
            fs.Write(data, 0, data.Length);
        }

        public void Append(byte[] data)
        {
            if (data == null || data.Length == 0) return;
            using var fs = new FileStream(_filePath, FileMode.Append,
                FileAccess.Write, FileShare.Read);
            fs.Write(data, 0, data.Length);
        }

        public void Delete()
        {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }
    }
}
