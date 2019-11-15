namespace GZipTest.Decompression
{
    public interface IDecompressor
    {
        OperationResult DecompressFile(string inputFilePath);

        OperationResult DecompressFile(string inputFilePath, string outputFilePath);
    }
}
