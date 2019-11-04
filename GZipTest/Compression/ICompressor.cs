namespace GZipTest.Compression
{
    public interface ICompressor
    {
        void CompressFile(string inputFilePath);

        void CompressFile(string inputFilePath, string outputFilePath);
    }
}
