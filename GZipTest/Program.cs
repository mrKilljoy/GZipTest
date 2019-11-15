using System;
using GZipTest.Compression;
using GZipTest.Decompression;

namespace GZipTest
{
    class Program
    {
        static void Main(string[] args)
        {
            string input = @"d:\temp\compression\exec_sample.exe";
            string output = @"d:\temp\compression\exec_sample_0.exe.gz";
            var appDomain = AppDomain.CurrentDomain;
            appDomain.UnhandledException += ProcessUnhandledException;
            Console.CancelKeyPress += CancelProcess;

            //ICompressor cmp = new GZipCompressor();
            //var result = cmp.CompressFile(input, output);
            //cmp.Dispose();

            GZipDecompressor dcmp = new GZipDecompressor();
            var result = dcmp.DecompressFile(output);
            dcmp.Dispose();

            //new CompressorTwo().Decompress(input + ".gz");

            //HandleOperationResult(result);
        }

        /// <summary>
        /// Обработать полученный ответ.
        /// </summary>
        /// <param name="result">Данные о результате операции.</param>
        private static void HandleOperationResult(OperationResult result)
        {
            if (result.Result == OperationResultEnum.Success)
                Console.WriteLine((int)OperationResultEnum.Success);
            else
            {
                Console.WriteLine($"Ошибка: {result.ThrownException.Message}");
                Console.WriteLine((int)OperationResultEnum.Failure);
            }
        }

        /// <summary>
        /// Сообщить о непредвиденной ошибке.
        /// </summary>
        private static void ProcessUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Console.WriteLine("Непредвиденная ошибка");
            Console.Write(((Exception)e.ExceptionObject).Message);
            Console.Write((int)OperationResultEnum.Failure);
        }

        /// <summary>
        /// Сообщить об отмене операции.
        /// </summary>
        private static void CancelProcess(object sender, ConsoleCancelEventArgs e)
        {
            Console.WriteLine("Операция отменена");
            Console.Write((int)OperationResultEnum.Failure);
            Environment.Exit(1);
        }
    }
}
