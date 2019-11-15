using System;

namespace GZipTest.Decompression
{
    public interface IDecompressor : IDisposable
    {
        OperationResult DecompressFile(string inputFilePath);

        OperationResult DecompressFile(string inputFilePath, string outputFilePath);
    }
}
