using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace GZipTest.Compression
{
    public class GZipCompressor : ICompressor
    {
        private Queue<FileChunk> _readSlices = new Queue<FileChunk>();
        private Queue<FileChunk> _compressedSlices = new Queue<FileChunk>();
        private int _runningThreads = default(int);
        private static readonly object _locker = new object();

        //private EventWaitHandle[] _handles = new ManualResetEvent[AppConstants.MaxThreadsCount - 1];
        private EventWaitHandle[] _handles = new ManualResetEvent[]{
            new ManualResetEvent(true),
            new ManualResetEvent(true),
            new ManualResetEvent(true),
            new ManualResetEvent(true),
            new ManualResetEvent(true),
            new ManualResetEvent(true),
            new ManualResetEvent(true),
            new ManualResetEvent(true),
        };

        private int _isReadingDone = default(int);
        private int _isProcessingDone = default(int);

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

            using (var originalFile = new FileStream(inputFilePath, FileMode.Open))
            {
                //  todo: make a way to set size of slice
                long chunksToProcess = (originalFile.Length % (decimal)AppConstants.SliceSizeBytes == 0 ?
                    originalFile.Length / AppConstants.SliceSizeBytes :
                    originalFile.Length / AppConstants.SliceSizeBytes + 1);

                //  todo: deal with output file format
                using (var compressedOne = new FileStream(outputFilePath, FileMode.CreateNew))
                {
                    //  todo: handle size slice properly (should be some restrictions about it)
                    using (var gs = new GZipStream(compressedOne, CompressionMode.Compress, true))
                    {
                        byte[] bucket = new byte[AppConstants.SliceSizeBytes];
                        int bytesRead;

                        while ((bytesRead = originalFile.Read(bucket, 0, bucket.Length)) != 0)
                        {
                            gs.Write(bucket, 0, bytesRead);
                            Console.Out.WriteLine($"left: {chunksToProcess}");
                            chunksToProcess--;
                        }
                    }
                }
            }
        }

        private void CompressParallel(string inputFilePath, string outputFilePath)
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
            var readThread = new Thread(ReadData) { Name = "reading_thread" };
            readThread.Start(inputFilePath);

            //  CONTINUE COMPRESSING
            int last = default(int);
            do
            {
                if (_runningThreads >= AppConstants.MaxThreadsCount)
                {
                    Thread.Sleep(10);
                    continue;
                }

                Interlocked.Exchange(ref last, WaitHandle.WaitAny(_handles));
                Interlocked.Increment(ref _runningThreads);
                new Thread(ProcessSlice) { Name = $"t-{last}" }.Start(last);
            }
            while (_isProcessingDone != 1);

            WaitHandle.WaitAll(_handles);

            Console.Out.WriteLine("compression has finished");

            //  WRITE CHUNKS
            var writeThread = new Thread(WriteData) { Name = "writing_thread" };
            writeThread.Start(outputFilePath);

            WaitHandle.WaitAll(_handles);
        }

        private void ReadData(object obj)
        {
            Interlocked.Increment(ref _runningThreads);

            _handles[0] = new ManualResetEvent(false);

            int chunkId = default(int);
            string inputFilePath = obj as string;

            using (var originalFile = new FileStream(inputFilePath, FileMode.Open))
            {
                byte[] bucket = new byte[AppConstants.SliceSizeBytes];
                int bytesRead;

                while ((bytesRead = originalFile.Read(bucket, 0, bucket.Length)) != 0)
                {
                    lock (_locker)
                    {
                        if (bytesRead != AppConstants.SliceSizeBytes)
                        {
                            var trailerBucker = new byte[bytesRead];
                            Array.Copy(bucket, 0, trailerBucker, 0, bytesRead);
                            _readSlices.Enqueue(new FileChunk { Id = chunkId, Bytes = trailerBucker });
                        }
                        else
                            _readSlices.Enqueue(new FileChunk { Id = chunkId, Bytes = bucket });

                        chunkId++;
                        //Console.Out.WriteLine($"chunk_read: {chunkId}");
                    }

                    bucket = new byte[bucket.Length];
                }

                Console.Out.WriteLine("file has been read");
            }

            Interlocked.Decrement(ref _runningThreads);
            Interlocked.Increment(ref _isReadingDone);
            _handles[0].Set();
        }

        private void ProcessSlice(object obj)
        {
            int triggerId = (int)obj;
            
            FileChunk data;
            lock (_locker)
            {
                if (_handles[triggerId] == null)
                    _handles[triggerId] = new ManualResetEvent(false);
                else
                    _handles[triggerId].Reset();

                if (_readSlices.Count != 0)
                {
                    data = _readSlices.Dequeue();

                    using (var memoryStream = new MemoryStream())
                    {
                        using (var zipper = new GZipStream(memoryStream, CompressionMode.Compress, true))
                        {
                            zipper.Write(data.Bytes, 0, data.Bytes.Length);
                            //Console.Out.WriteLine($"package#{data.Id} has been compressed");
                        }

                        _compressedSlices.Enqueue(new FileChunk { Id = data.Id, Bytes = memoryStream.ToArray() });
                    }
                }
                else
                {
                    if (_isReadingDone == 1 && _isProcessingDone == 0)
                        Interlocked.Increment(ref _isProcessingDone);
                }

                _handles[triggerId].Set();
            }

            Interlocked.Decrement(ref _runningThreads);
            
            //Console.Out.WriteLine($"thread#{triggerId} is out");
        }

        private void WriteData(object obj)
        {
            string outputFilePath = obj as string;

            Interlocked.Increment(ref _runningThreads);

            //  todo: get any free slot, not the first one
            _handles[0] = new ManualResetEvent(false);

            lock (_locker)
            {
                using (var archivedFile = new FileStream(outputFilePath, FileMode.CreateNew))
                {
                    while (_compressedSlices.Count > 0)
                    {
                        var chunk = _compressedSlices.Dequeue();

                        archivedFile.Write(chunk.Bytes, 0, chunk.Bytes.Length);
                        //Console.Out.WriteLine($"chunk_id#: {chunk.Id}");
                    }

                    archivedFile.Flush();
                }
            }

            Console.Out.WriteLine("file has been written");

            Interlocked.Decrement(ref _runningThreads);
            _handles[0].Set();
        }

        public void CompressFile(string inputFilePath)
        {
            //CompressBase(inputFilePath, string.Concat(inputFilePath, AppConstants.GZipArchiveExtension));
            CompressParallel(inputFilePath, string.Concat(inputFilePath, AppConstants.GZipArchiveExtension));
        }

        public void CompressFile(string inputFilePath, string outputFilePath)
        {
            CompressBase(inputFilePath, outputFilePath);
        }
    }
}
