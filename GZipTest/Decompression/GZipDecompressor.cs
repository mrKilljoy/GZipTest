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

                    var positions = GetHeadersOffsets(archivedFile);
                    if (positions.Count > 0)
                        positions.Dequeue();    // drop the first one

                    if (positions.Count > 1)
                        bucket = new byte[positions.Dequeue() - archivedFile.Position];
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
            
            Interlocked.Exchange(ref _isReadingDone, 1);
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
        private Queue<long> GetHeadersOffsets(Stream stream)
        {
            var indices = new Queue<long>();
            int lastByte = default(int);
            int offset = default(int);

            //  some gzip header flags
            bool id1 = false;
            bool id2 = false;
            bool compressionFlag = false;
            bool flag = false;
            bool extraCompressionFlag = false;
            bool osByte = false;

            while (lastByte != -1)
            {
                lastByte = stream.ReadByte();

                if (lastByte == 0x1f)
                {
                    id1 = true;
                    offset++;
                }
                else if (offset == 1 && lastByte == 0x8b && id1)
                {
                    id2 = true;
                    offset++;
                }
                else if (offset == 2 && lastByte == 0x08 && id2 && id1)
                {
                    compressionFlag = true;
                    offset++;
                }
                else if (offset == 3 && (lastByte == 0x00) && compressionFlag && id2 && id1)
                {
                    flag = true;
                    offset++;
                }
                else if (offset > 3 && offset < 8 && flag && compressionFlag && id2 && id1)
                {
                    offset++;
                    continue;
                }
                else if (offset == 8 && flag && compressionFlag && id2 && id1)
                {
                    if (lastByte == 0x04)
                    {
                        extraCompressionFlag = true;
                        offset++;
                    }
                    else
                    {
                        id1 = false;
                        id2 = false;
                        compressionFlag = false;
                        flag = false;
                        extraCompressionFlag = false;
                        osByte = false;
                        offset = default(int);
                    }
                }
                else if (offset == 9 && extraCompressionFlag && flag && compressionFlag && id2 && id1)
                {
                    if (lastByte == 0x00)
                    {
                        osByte = true;
                        indices.Enqueue(stream.Position - 10);
                    }

                    id1 = false;
                    id2 = false;
                    compressionFlag = false;
                    flag = false;
                    extraCompressionFlag = false;
                    osByte = false;
                    offset = default(int);
                }
                else
                {
                    id1 = false;
                    id2 = false;
                    compressionFlag = false;
                    flag = false;
                    extraCompressionFlag = false;
                    osByte = false;
                    offset = default(int);
                }
            }

            stream.Seek(0, SeekOrigin.Begin);
            return indices;
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
