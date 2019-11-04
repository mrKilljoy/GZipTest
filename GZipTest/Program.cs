using GZipTest.Compression;
using GZipTest.Decompression;

namespace GZipTest
{
    class Program
    {
        static void Main(string[] args)
        {
            ICompressor cmp = new GZipCompressor();
            IDecompressor dcmp = new GZipDecompressor();

            cmp.CompressFile(@"d:\temp\large_doc_sample.pdf");
            //dcmp.DecompressFile(@"d:\temp\large_doc_sample.pdf.gz", @"d:\temp\large_doc_sample2.pdf");
        }
    }
}
