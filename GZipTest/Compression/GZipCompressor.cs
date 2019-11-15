﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace GZipTest.Compression
{
    public class GZipCompressor : ICompressor
    {
        private readonly Queue<FileChunk> _readChunks;
        private readonly Queue<FileChunk> _compressedChunks;
        
        private readonly object _lock = new object();
        private readonly ManualResetEvent[] _endingEvents;
        private Semaphore _sm;
        private int _runningThreadsNumber = default(int);

        private int _isReadingDone = default(int);
        private int _isProcessingDone = default(int);

        public GZipCompressor()
        {
            _readChunks = new Queue<FileChunk>();
            _compressedChunks = new Queue<FileChunk>();

            _sm = new Semaphore(AppConstants.MaxThreadsCount, AppConstants.MaxThreadsCount);
            _endingEvents = new ManualResetEvent[]
            {
                new ManualResetEvent(false),
                new ManualResetEvent(false)
            };
        }

        private OperationResult CompressBase(string inputFilePath, string outputFilePath)
        {
            try
            {
                if (string.IsNullOrEmpty(inputFilePath))
                    throw new ArgumentException("Имя входного файла не задано");

                if (!File.Exists(inputFilePath))
                    throw new FileNotFoundException("Файл с таким именем не найден");

                if (File.Exists(outputFilePath))
                    throw new ArgumentException("Архив с таким именем уже существует");

                if (!outputFilePath.EndsWith(".gz"))
                    throw new ArgumentException("Неверное расширение для архива. Архив должен иметь расширение *.gz");

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
                return new OperationResult
                {
                    ThrownException = ex,
                    Result = OperationResultEnum.Failure
                };
            }
        }

        private void ReadData(object obj)
        {
            try
            {
                //_sm.WaitOne();
                //Interlocked.Increment(ref _runningThreadsNumber);

                int chunkId = default(int);
                string inputFilePath = (string)obj;

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
                                var trailerBucker = new byte[bytesRead];
                                Array.Copy(bucket, 0, trailerBucker, 0, bytesRead);
                                _readChunks.Enqueue(new FileChunk { Id = chunkId, Bytes = trailerBucker });
                            }

                            chunkId++;
                            Console.WriteLine(chunkId);
                        }

                        bucket = new byte[bucket.Length];
                    }
                }

                //_sm.Release();
                //Interlocked.Decrement(ref _runningThreadsNumber);

                Interlocked.Increment(ref _isReadingDone);
            }
            catch (Exception ex)
            {
                LogAndExit("Ошибка в ходе чтения файла", ex);
            }
        }

        private void ProcessData(object obj)
        {
            try
            {
                //_sm.WaitOne();
                //Interlocked.Increment(ref _runningThreadsNumber);

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

        private void ProcessChunk(object obj)
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

                        using (var sourceStream = new MemoryStream(chunk.Bytes))
                        {
                            using (var compressedStream = new MemoryStream())
                            {
                                using (var zipper = new GZipStream(compressedStream, CompressionMode.Compress, true))
                                    sourceStream.CopyTo(zipper);

                                _compressedChunks.Enqueue(new FileChunk { Id = chunk.Id, Bytes = compressedStream.ToArray() });
                            }
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

        private void WriteData(object obj)
        {
            string outputFilePath = (string)obj;

            try
            {
                //_sm.WaitOne();
                //Interlocked.Increment(ref _runningThreadsNumber);

                lock (_lock)
                {
                    using (var compressedFile = new FileStream(outputFilePath, FileMode.CreateNew))
                    {
                        while (_compressedChunks.Count > 0 || _isProcessingDone == 0)
                        {
                            if (_compressedChunks.Count == 0 && _isProcessingDone == 0)
                            {
                                Monitor.Wait(_lock, 500);
                                continue;
                            }
                            
                            var chunk = _compressedChunks.Dequeue();
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

        public OperationResult CompressFile(string inputFilePath)
        {
            return CompressBase(inputFilePath, string.Concat(inputFilePath, AppConstants.GZipArchiveExtension));
        }

        public OperationResult CompressFile(string inputFilePath, string outputFilePath)
        {
            return CompressBase(inputFilePath, outputFilePath);
        }

        //  todo: turn it on
        private void LogAndExit(string message, Exception exception)
        {
            Console.WriteLine(message);
            Console.WriteLine($"Текст ошибки: {exception.Message}");
            Console.WriteLine((int)OperationResultEnum.Failure);
            Environment.Exit(1);
        }

        //  todo: drop later
        //private void ShowChunkData(FileChunk chunk)
        //{
        //    var header = string.Join(".", chunk.Bytes.Take(4));
        //    var trailer = string.Join(".", chunk.Bytes.Reverse().Take(14).Reverse());
        //    var trailerSize = BitConverter.ToInt32(chunk.Bytes.Reverse().Take(4).Reverse().ToArray(), 0);
        //    Console.WriteLine($"c.id = {chunk.Id} | c.header = {header} | c.size = {trailerSize} | c.tail = {trailer}");
        //}

        public void Dispose()
        {
            _sm.Close();
            _endingEvents[0].Close();
            _endingEvents[1].Close();
        }
    }
}
