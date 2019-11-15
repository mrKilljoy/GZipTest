using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace GZipTest
{
    public abstract class GZipBase
    {
        protected readonly Queue<FileChunk> _readChunks;
        protected readonly Queue<FileChunk> _processedChunks;

        protected readonly object _lock = new object();
        protected readonly ManualResetEvent[] _endingEvents;
        protected Semaphore _sm;
        protected int _runningThreadsNumber = default(int);

        protected int _isReadingDone = default(int);
        protected int _isProcessingDone = default(int);

        protected GZipBase()
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

        protected OperationResult HandleBase(string inputFilePath, string outputFilePath)
        {
            try
            {
                ValidateArguments(inputFilePath, outputFilePath);

                //  READING
                var readThread = new Thread(ReadData);
                readThread.Start(inputFilePath);

                //  PROCESSING
                var processingThread = new Thread(ProcessData);
                processingThread.Start();

                //  WRITING
                var writeThread = new Thread(WriteData);
                writeThread.Start(outputFilePath);

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

        protected virtual void ReadData(object obj)
        {
            Interlocked.Increment(ref _runningThreadsNumber);
            _sm.WaitOne();

            int chunkId = default(int);
            string inputFilePath = (string)obj;

            try
            {
                using (var originalFile = new FileStream(inputFilePath, FileMode.Open))
                {
                    byte[] bucket = new byte[AppConstants.ChunkSizeBytes];
                    int bytesRead;

                    while ((bytesRead = originalFile.Read(bucket, 0, bucket.Length)) != 0)
                    {
                        lock (_lock)
                        {
                            if (bytesRead == AppConstants.ChunkSizeBytes)
                                _readChunks.Enqueue(new FileChunk { Id = chunkId, Bytes = bucket });
                            else
                            {
                                var trailerBucket = new byte[bytesRead];
                                Array.Copy(bucket, 0, trailerBucket, 0, bytesRead);
                                _readChunks.Enqueue(new FileChunk { Id = chunkId, Bytes = trailerBucket });
                            }

                            chunkId++;
                        }

                        bucket = new byte[bucket.Length];
                    }
                }

                _sm.Release();
                Interlocked.Decrement(ref _runningThreadsNumber);
            }
            catch (Exception ex)
            {
                LogAndExit("Ошибка в ходе чтения файла", ex);
            }
            
            Interlocked.Increment(ref _isReadingDone);
        }

        protected void ProcessData(object obj)
        {
            try
            {
                //Interlocked.Increment(ref _runningThreadsNumber);
                //_sm.WaitOne();

                do
                {
                    if (_runningThreadsNumber >= AppConstants.MaxThreadsCount)
                    {
                        Thread.Sleep(10);
                        continue;
                    }
                    
                    new Thread(ProcessChunk).Start();
                }
                while (_isProcessingDone != 1);

                //_sm.Release();
                //Interlocked.Decrement(ref _runningThreadsNumber);

                lock (_lock)
                    _endingEvents[0].Set();
            }
            catch (Exception ex)
            {
                LogAndExit("Ошибка в ходе сжатия данных", ex);
            }            
        }

        protected virtual void ProcessChunk(object obj)
        {
            try
            {
                FileChunk chunk;
                Interlocked.Increment(ref _runningThreadsNumber);
                _sm.WaitOne();

                lock (_lock)
                {
                    if (_readChunks.Count != 0)
                    {
                        chunk = _readChunks.Dequeue();

                        using (var memoryStream = new MemoryStream())
                        {
                            using (var zipper = new GZipStream(memoryStream, CompressionMode.Compress, true))
                                zipper.Write(chunk.Bytes, 0, chunk.Bytes.Length);

                            _processedChunks.Enqueue(new FileChunk { Id = chunk.Id, Bytes = memoryStream.ToArray() });
                        }
                    }
                    else
                    {
                        if (_isReadingDone == 1 && _isProcessingDone == 0)
                        {
                            Interlocked.Increment(ref _isProcessingDone);
                            Monitor.PulseAll(_lock);
                        }
                    }
                }

                Interlocked.Decrement(ref _runningThreadsNumber);
                _sm.Release();
            }
            catch (Exception ex)
            {
                LogAndExit("Ошибка при сжатии фрагмента файла", ex);
            }
        }

        protected void WriteData(object obj)
        {
            string outputFilePath = (string)obj;

            try
            {
                //Interlocked.Increment(ref _runningThreadsNumber);
                //_sm.WaitOne();

                lock (_lock)
                {
                    using (var compressedFile = new FileStream(outputFilePath, FileMode.CreateNew))
                    {
                        while (_processedChunks.Count > 0 || _isProcessingDone == 0)
                        {
                            if (_processedChunks.Count == 0 && _isProcessingDone == 0)
                            {
                                Monitor.Wait(_lock, 500);
                                continue;
                            }

                            var chunk = _processedChunks.Dequeue();

                            compressedFile.Write(chunk.Bytes, 0, chunk.Bytes.Length);
                        }
                    }
                }

                //_sm.Release();
                //Interlocked.Decrement(ref _runningThreadsNumber);

                lock (_lock)
                    _endingEvents[1].Set();
            }
            catch (Exception ex)
            {
                LogAndExit("Ошибка при записи архива", ex);
            }
        }

        public abstract OperationResult HandleFile(string inputFilePath);

        public abstract OperationResult HandleFile(string inputFilePath, string outputFilePath);

        protected void LogAndExit(string message, Exception exception)
        {
            Console.WriteLine(message);
            Console.WriteLine($"Текст ошибки: {exception.Message}");
            Console.WriteLine((int)OperationResultEnum.Failure);
            Environment.Exit(1);
        }

        //  todo: drop later
        //protected void ShowChunkData(FileChunk chunk)
        //{
        //    var header = string.Join(".", chunk.Bytes.Take(4));
        //    var trailer = string.Join(".", chunk.Bytes.Reverse().Take(14).Reverse());
        //    var trailerSize = BitConverter.ToInt32(chunk.Bytes.Reverse().Take(4).Reverse().ToArray(), 0);
        //    Console.WriteLine($"c.id = {chunk.Id}   |   c.header = {header}    |    c.size = {trailerSize} | c.tail = {trailer}");
        //}

        protected abstract void ValidateArguments(string inputFilePath, string outputFilePath);

        public void Dispose()
        {
            _sm.Close();
            _endingEvents[0].Close();
            _endingEvents[1].Close();
        }
    }
}
