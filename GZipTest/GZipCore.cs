using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace GZipTest
{
    /// <summary>
    /// Базовый класс для сжатия / распаковки данных.
    /// </summary>
    public abstract class GZipCore
    {
        protected readonly Queue<FileChunk> _readChunks;
        protected readonly Queue<FileChunk> _processedChunks;

        protected readonly object _lock = new object();
        protected readonly ManualResetEvent[] _endingEvents;
        protected Semaphore _sm;
        protected volatile int _runningThreadsNumber = default(int);

        protected volatile int _isReadingDone = default(int);
        protected volatile int _isProcessingDone = default(int);

        protected GZipCore()
        {
            _readChunks = new Queue<FileChunk>();
            _processedChunks = new Queue<FileChunk>();

            _sm = new Semaphore(AppConstants.MaxThreadsCount, AppConstants.MaxThreadsCount);
            _endingEvents = new ManualResetEvent[]
            {
                new ManualResetEvent(false),
                new ManualResetEvent(false)
            };
        }

        protected OperationResult Handle(string inputFilePath, string outputFilePath)
        {
            try
            {
                ValidateArguments(inputFilePath, outputFilePath);

                var reading = new Thread(ReadData);
                reading.Start(inputFilePath);

                var processing = new Thread(ProcessData);
                processing.Start();

                var writing = new Thread(WriteData);
                writing.Start(outputFilePath);

                WaitHandle.WaitAll(_endingEvents);

                return new OperationResult
                {
                    Result = OperationResultEnum.Success
                };
            }
            catch (Exception ex)
            {
                return new OperationResult()
                {
                    ThrownException = ex,
                    Result = OperationResultEnum.Failure
                };
            }
        }

        protected abstract void ReadData(object obj);

        protected virtual void ProcessData(object obj)
        {
            try
            {
                do
                {
                    lock (_lock)
                    {
                        if (_readChunks.Count == 0 && _isReadingDone == 1)
                        {
                            Interlocked.Exchange(ref _isProcessingDone, 1);
                            Monitor.PulseAll(_lock);
                            break;
                        }

                        if (_runningThreadsNumber >= AppConstants.MaxThreadsCount)
                        {
                            Monitor.PulseAll(_lock);
                            Monitor.Wait(_lock, 50);
                            continue;
                        }
                    }
                    
                    new Thread(ProcessChunk).Start();
                }
                while (_isProcessingDone != 1);

                lock (_lock)
                    _endingEvents[0].Set();
            }
            catch (Exception ex)
            {
                LogAndExit("Ошибка в ходе обработки данных", ex);
            }            
        }

        protected abstract void ProcessChunk(object obj);

        protected virtual void WriteData(object obj)
        {
            string outputFilePath = (string)obj;

            try
            {
                lock (_lock)
                {
                    using (var compressedFile = new FileStream(outputFilePath, FileMode.CreateNew))
                    {
                        while (_processedChunks.Count > 0 || _isProcessingDone == 0)
                        {
                            if (_processedChunks.Count == 0 && _isProcessingDone == 0)
                            {
                                Monitor.PulseAll(_lock);
                                Monitor.Wait(_lock, 200);
                                continue;
                            }

                            var chunk = _processedChunks.Dequeue();
                            compressedFile.Write(chunk.Bytes, 0, chunk.Bytes.Length);
                        }
                    }
                }

                lock (_lock)
                    _endingEvents[1].Set();
            }
            catch (Exception ex)
            {
                LogAndExit("Ошибка при записи файла", ex);
            }
        }

        protected void LogAndExit(string message, Exception exception)
        {
            Console.WriteLine(message);
            Console.WriteLine($"Текст ошибки: {exception.Message}");
            Console.WriteLine((int)OperationResultEnum.Failure);
            Environment.Exit(0);
        }

        protected abstract void ValidateArguments(string inputFilePath, string outputFilePath);

        public void Dispose()
        {
            _sm.Close();
            _endingEvents[0].Close();
            _endingEvents[1].Close();
        }
    }
}
