namespace SourceSerializer.Generator
{
    internal static class EmitHelpers
    {
        private static int _varCounter;
        private static int _repCounter;
        private static int _emitCounter;

        public static void ResetCounters()
        {
            _varCounter = 0;
            _repCounter = 0;
            _emitCounter = 0;
        }

        public static int NextRepId() => _repCounter++;
        public static int NextEmitId() => _emitCounter++;

        public static string GetMethodName(string prefix, string structTypeName)
            => $"{prefix}_{structTypeName.Replace(".", "_").Replace("<", "_").Replace(">", "").Replace(",", "_")}";

        public static string GetUniqueVar(string fieldName) => $"_{fieldName}_{_varCounter++}";
    }
}
