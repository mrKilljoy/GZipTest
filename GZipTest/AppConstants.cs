using System;

namespace GZipTest
{
    internal static class AppConstants
    {
        public const int ChunkSizeBytes = 1048576;

        public const string GZipArchiveExtension = ".gz";

        public static readonly int MaxThreadsCount = Environment.ProcessorCount;
    }
}
