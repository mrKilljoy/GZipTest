namespace GZipTest.Decompression
{
    public interface IDecompressor
    {
        void DecompressFile(string inputFilePath);

        void DecompressFile(string inputFilePath, string outputFilePath);
    }
}
