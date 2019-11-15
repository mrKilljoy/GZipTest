using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace GZipTest.Decompression
{
    public sealed class GZipDecompressor : GZipCore, IDecompressor
    {
        public GZipDecompressor() : base() { }

        protected override void ValidateArguments(string inputFilePath, string outputFilePath)
        {
            if (!File.Exists(inputFilePath))
                throw new FileNotFoundException("Архив с таким именем не найден");

            if (File.Exists(outputFilePath))
                throw new ArgumentException("Файл с таким именем уже существует");
        }

        protected override void ReadData(object obj)
        {
            int chunkId = default(int);
            string inputFilePath = (string)obj;

            try
            {
                using (var archivedFile = new FileStream(inputFilePath, FileMode.Open))
                {
                    if (archivedFile.ReadByte() != 0x1f || archivedFile.ReadByte() != 0x8b || archivedFile.ReadByte() != 0x08)
                        throw new FormatException("Некорректный формат архива");

                    archivedFile.Seek(0, SeekOrigin.Begin);

                    byte[] bucket;

                    var positions = GetFlagsPositions(archivedFile);
                    if (positions.Count > 0)
                        positions.Dequeue();

                    if (positions.Count > 1)
                    {
                        positions.Dequeue();    // drop the first one
                        bucket = new byte[positions.Dequeue() - archivedFile.Position];
                    }
                    else
                        bucket = new byte[AppConstants.ChunkSizeBytes];
                    
                    int bytesRead;

                    while ((bytesRead = archivedFile.Read(bucket, 0, bucket.Length)) != 0)
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

                        if (positions.Count > 0)
                            bucket = new byte[positions.Dequeue() - archivedFile.Position];
                        else
                            bucket = new byte[archivedFile.Length - archivedFile.Position];
                    }
                }
            }
            catch (Exception ex)
            {
                LogAndExit("Ошибка в ходе чтения архива", ex);
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
                    if (_readChunks.Count > 0)
                    {
                        chunk = _readChunks.Dequeue();

                        using (var decompressedMemoryStream = new MemoryStream())
                        {
                            using (var compressedMemoryStream = new MemoryStream(chunk.Bytes))
                            {
                                using (var unzipper = new GZipStream(compressedMemoryStream, CompressionMode.Decompress, true))
                                    unzipper.CopyTo(decompressedMemoryStream);
                            }

                            _processedChunks.Enqueue(new FileChunk { Id = chunk.Id, Bytes = decompressedMemoryStream.ToArray() });
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
                LogAndExit("Ошибка при распаковке фрагмента файла", ex);
            }
        }

        /// <summary>
        /// Получить список индексов в потоке, с которых начинается заголовок gzip-фрагмента.
        /// </summary>
        /// <param name="stream">Читаемый поток данных.</param>
        /// <returns>Список индексов.</returns>
        private Queue<long> GetFlagsPositions(Stream stream)
        {
            var places = new Queue<long>();
            int lastByte = default(int);
            bool id1 = false;
            bool id2 = false;
            bool id3 = false;
            bool extraByte = false;

            while (lastByte != -1)
            {
                lastByte = stream.ReadByte();

                if (lastByte == 0x1f)
                    id1 = true;
                else if (lastByte == 0x8b && id1)
                    id2 = true;
                else if (lastByte == 0x08 && id2 && id1)
                {
                    id3 = true;
                }
                else if (lastByte == 0x00 && id3 && id2 && id1)
                {
                    extraByte = true;
                    places.Enqueue(stream.Position - 4);

                    id1 = false;
                    id2 = false;
                    id3 = false;
                    extraByte = false;
                }
                else
                {
                    id1 = false;
                    id2 = false;
                    id3 = false;
                    extraByte = false;
                }
            }

            stream.Seek(0, SeekOrigin.Begin);
            return places;
        }

        public OperationResult DecompressFile(string inputFilePath)
        {
            return Handle(inputFilePath, inputFilePath?.Replace(AppConstants.GZipArchiveExtension, string.Empty));
        }

        public OperationResult DecompressFile(string inputFilePath, string outputFilePath)
        {
            return Handle(inputFilePath, outputFilePath);
        }
    }
}
