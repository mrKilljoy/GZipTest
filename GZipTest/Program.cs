using System;
using GZipTest.Compression;
using GZipTest.Decompression;

namespace GZipTest
{
    class Program
    {
        static void Main(string[] args)
        {
            //  set handlers
            var appDomain = AppDomain.CurrentDomain;
            appDomain.UnhandledException += ProcessUnhandledException;
            Console.CancelKeyPress += CancelProcess;

            ValidateInputArguments(args);

            OperationResult result;

            switch (args[0])
            {
                case "compress":
                    {
                        string input = args[1];
                        string output = args.Length == 3 ? args[2] : null;

                        ICompressor tool = new GZipCompressor();

                        if (string.IsNullOrEmpty(output))
                            result = tool.CompressFile(input);
                        else
                            result = tool.CompressFile(input, output);

                        tool.Dispose();

                        break;
                    }

                case "decompress":
                    {
                        string input = args[1];
                        string output = args.Length == 3 ? args[2] : null;

                        IDecompressor tool = new GZipDecompressor();

                        if (string.IsNullOrEmpty(output))
                            result = tool.DecompressFile(input);
                        else
                            result = tool.DecompressFile(input, output);

                        tool.Dispose();

                        break;
                    }

                default:
                    {
                        Console.WriteLine("Нет такой команды");
                        return;
                    }
            }

            HandleOperationResult(result);
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

        /// <summary>
        /// Выполнить начальную проверку переданных параметров.
        /// </summary>
        /// <param name="args">Список параметров.</param>
        private static void ValidateInputArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Console.WriteLine("Нет входных аргументов");
                Environment.Exit(0);
            }
            
            if (args.Length < 2)
            {
                Console.WriteLine("Не указаны необходимые входные параметры: команда, входной файл");
                Environment.Exit(0);
            }
        }
    }
}
