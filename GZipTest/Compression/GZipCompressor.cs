using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace GZipTest.Compression
{
    public class GZipCompressor : ICompressor
    {
        private Queue<FileChunk> _readChunks;
        private Queue<FileChunk> _compressedChunks;

        private int _runningThreadsNumber = default(int);
        private readonly object _lock = new object();
        private Semaphore _sm;
        private ManualResetEvent[] _endingEvents;

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

        private void CompressBase(string inputFilePath, string outputFilePath)
        {
            if (string.IsNullOrEmpty(inputFilePath))
                throw new ArgumentException("Имя входного файла не задано");

            //  todo
            if (!File.Exists(inputFilePath))
                throw new FileNotFoundException("Файл с таким именем не найден");

            //  todo
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

            ManualResetEvent.WaitAll(_endingEvents);
        }

        private void ReadData(object obj)
        {
            _sm.WaitOne();
            Interlocked.Increment(ref _runningThreadsNumber);

            int chunkId = default(int);
            string inputFilePath = (string)obj;

            using (var originalFile = new FileStream(inputFilePath, FileMode.Open))
            {
                byte[] bucket = new byte[AppConstants.SliceSizeBytes];
                int bytesRead;

                while ((bytesRead = originalFile.Read(bucket, 0, bucket.Length)) != 0)
                {
                    lock (_lock)
                    {
                        if (bytesRead == AppConstants.SliceSizeBytes)
                            _readChunks.Enqueue(new FileChunk { Id = chunkId, Bytes = bucket });
                        else
                        {
                            var trailerBucker = new byte[bytesRead];
                            Array.Copy(bucket, 0, trailerBucker, 0, bytesRead);
                            _readChunks.Enqueue(new FileChunk { Id = chunkId, Bytes = trailerBucker });
                        }

                        chunkId++;
                    }

                    bucket = new byte[bucket.Length];
                }
            }

            _sm.Release();
            Interlocked.Decrement(ref _runningThreadsNumber);
            Interlocked.Increment(ref _isReadingDone);
        }

        private void ProcessData(object obj)
        {
            _sm.WaitOne();
            Interlocked.Increment(ref _runningThreadsNumber);

            do
            {
                if (_runningThreadsNumber >= AppConstants.MaxThreadsCount - 1)
                {
                    Thread.Sleep(10);
                    continue;
                }

                Interlocked.Increment(ref _runningThreadsNumber);
                new Thread(ProcessChunk).Start();
            }
            while (_isProcessingDone != 1);
            
            _sm.Release();

            lock (_lock)
                _endingEvents[0].Set();

            Interlocked.Decrement(ref _runningThreadsNumber);
        }

        private void ProcessChunk(object obj)
        {            
            FileChunk chunk;
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

                        _compressedChunks.Enqueue(new FileChunk { Id = chunk.Id, Bytes = memoryStream.ToArray() });
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

        private void WriteData(object obj)
        {
            string outputFilePath = (string)obj;

            _sm.WaitOne();
            Interlocked.Increment(ref _runningThreadsNumber);

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

            _sm.Release();
            Interlocked.Decrement(ref _runningThreadsNumber);

            lock (_lock)
                _endingEvents[1].Set();
        }

        public void CompressFile(string inputFilePath)
        {
            //CompressBase(inputFilePath, string.Concat(inputFilePath, AppConstants.GZipArchiveExtension));
            CompressBase(inputFilePath, string.Concat(inputFilePath, AppConstants.GZipArchiveExtension));
        }

        public void CompressFile(string inputFilePath, string outputFilePath)
        {
            CompressBase(inputFilePath, outputFilePath);
        }

        public void Dispose()
        {
            _sm.Close();
            _sm = null;
        }
    }
}
