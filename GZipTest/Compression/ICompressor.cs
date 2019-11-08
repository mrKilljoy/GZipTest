using System;

namespace GZipTest.Compression
{
    public interface ICompressor : IDisposable
    {
        void CompressFile(string inputFilePath);

        void CompressFile(string inputFilePath, string outputFilePath);
    }
}
