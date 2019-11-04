using System;

namespace GZipTest
{
    public static class AppConstants
    {
        public const int SliceSizeBytes = 1048576;

        public const string GZipArchiveExtension = ".gz";

        public static readonly int MaxThreadsCount = Environment.ProcessorCount;
    }
}
