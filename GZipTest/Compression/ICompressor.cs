using System;

namespace GZipTest.Compression
{
    public interface ICompressor : IDisposable
    {
        OperationResult CompressFile(string inputFilePath);

        OperationResult CompressFile(string inputFilePath, string outputFilePath);
    }
}
