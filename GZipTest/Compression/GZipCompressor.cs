using System;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace GZipTest.Compression
{
    public class GZipCompressor : GZipCore, ICompressor
    {
        public GZipCompressor() : base() { }

        protected override void ReadData(object obj)
        {
            try
            {
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
                                var trailerBucket = new byte[bytesRead];
                                Array.Copy(bucket, 0, trailerBucket, 0, bytesRead);
                                _readChunks.Enqueue(new FileChunk { Id = chunkId, Bytes = trailerBucket });
                            }

                            chunkId++;
                        }

                        bucket = new byte[bucket.Length];
                    }
                }
            }
            catch (Exception ex)
            {
                LogAndExit("Ошибка в ходе чтения файла", ex);
            }

            Interlocked.Increment(ref _isReadingDone);
        }

        protected override void ProcessChunk(object obj)
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

                                _processedChunks.Enqueue(new FileChunk { Id = chunk.Id, Bytes = compressedStream.ToArray() });
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

        public OperationResult CompressFile(string inputFilePath)
        {
            return Handle(inputFilePath, string.Concat(inputFilePath, AppConstants.GZipArchiveExtension));
        }

        public OperationResult CompressFile(string inputFilePath, string outputFilePath)
        {
            return Handle(inputFilePath, outputFilePath);
        }

        protected override void ValidateArguments(string inputFilePath, string outputFilePath)
        {
            if (string.IsNullOrEmpty(inputFilePath))
                throw new ArgumentException("Имя входного файла не задано");

            if (!File.Exists(inputFilePath))
                throw new FileNotFoundException("Файл с таким именем не найден");

            if (File.Exists(outputFilePath))
                throw new ArgumentException("Архив с таким именем уже существует");

            if (!outputFilePath.EndsWith(".gz"))
                throw new ArgumentException("Неверное расширение для архива. Архив должен иметь расширение *.gz");
        }
    }
}
