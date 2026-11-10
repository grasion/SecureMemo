using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using NAudio.Wave;

namespace SecureMemo.Services
{
    /// <summary>
    /// 암호화된 오디오 파일 형식 (.sma)
    ///   헤더 16바이트: "SMA1" | sampleRate(4) | channels(2) | bitsPerSample(2) | reserved(4)
    ///   이후 청크 반복: nonce(12) | tag(16) | ciphertext(최대 ChunkSize)
    /// 각 청크는 (헤더 + 청크 번호)를 AAD로 묶어 순서 변경/교체를 막는다.
    /// 녹음 중에도 청크 단위로 바로 암호화하므로 평문 음성이 디스크에 남지 않는다.
    /// </summary>
    internal static class EncryptedAudioFormat
    {
        public const int HeaderSize = 16;
        public const int ChunkSize = 64 * 1024;
        public const int RecordOverhead = CryptoService.NonceSize + CryptoService.TagSize;
        public static readonly byte[] Magic = "SMA1"u8.ToArray();

        public static byte[] BuildHeader(WaveFormat format)
        {
            var header = new byte[HeaderSize];
            Magic.CopyTo(header, 0);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), format.SampleRate);
            BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(8), (short)format.Channels);
            BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(10), (short)format.BitsPerSample);
            return header;
        }

        public static byte[] Aad(byte[] header, long chunkIndex)
        {
            var aad = new byte[HeaderSize + 8];
            header.CopyTo(aad, 0);
            BinaryPrimitives.WriteInt64LittleEndian(aad.AsSpan(HeaderSize), chunkIndex);
            return aad;
        }
    }

    /// <summary>PCM 데이터를 받아 청크 단위로 암호화해 파일에 기록</summary>
    public sealed class EncryptedAudioWriter : IDisposable
    {
        private readonly FileStream _file;
        private readonly byte[] _key;
        private readonly byte[] _header;
        private readonly byte[] _buffer = new byte[EncryptedAudioFormat.ChunkSize];
        private int _buffered;
        private long _chunkIndex;
        private bool _disposed;

        public EncryptedAudioWriter(string path, byte[] key, WaveFormat format)
        {
            _key = key;
            _header = EncryptedAudioFormat.BuildHeader(format);
            _file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            _file.Write(_header);
        }

        public void Write(byte[] data, int offset, int count)
        {
            if (_disposed) return;
            while (count > 0)
            {
                var n = Math.Min(count, _buffer.Length - _buffered);
                Buffer.BlockCopy(data, offset, _buffer, _buffered, n);
                _buffered += n;
                offset += n;
                count -= n;
                if (_buffered == _buffer.Length) FlushChunk();
            }
        }

        private void FlushChunk()
        {
            if (_buffered == 0) return;
            var record = CryptoService.Seal(_key, _buffer.AsSpan(0, _buffered),
                EncryptedAudioFormat.Aad(_header, _chunkIndex));
            _file.Write(record);
            _file.Flush();
            Array.Clear(_buffer, 0, _buffered);
            _buffered = 0;
            _chunkIndex++;
        }

        public void Dispose()
        {
            if (_disposed) return;
            FlushChunk();
            _disposed = true;
            _file.Dispose();
        }
    }

    /// <summary>NAudio 재생용 래퍼 (재생 스레드와 탐색이 동시에 일어나도 안전하도록 잠금)</summary>
    public sealed class EncryptedWaveStream : WaveStream
    {
        private readonly EncryptedAudioStream _source;
        private readonly object _sync = new();

        public EncryptedWaveStream(EncryptedAudioStream source) => _source = source;

        public override WaveFormat WaveFormat => _source.Format;

        public override long Length => _source.Length - _source.Length % WaveFormat.BlockAlign;

        public override long Position
        {
            get { lock (_sync) return _source.Position; }
            set { lock (_sync) _source.Position = value - value % WaveFormat.BlockAlign; }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            lock (_sync) return _source.Read(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _source.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>암호화된 오디오 파일을 필요한 청크만 복호화하며 읽는 탐색 가능한 스트림</summary>
    public sealed class EncryptedAudioStream : Stream
    {
        private readonly FileStream _file;
        private readonly byte[] _key;
        private readonly byte[] _header;
        private readonly long _length;
        private long _position;
        private long _cachedChunk = -1;
        private byte[] _cache = Array.Empty<byte>();

        public WaveFormat Format { get; }

        public EncryptedAudioStream(string path, byte[] key)
        {
            _key = key;
            _file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                _header = new byte[EncryptedAudioFormat.HeaderSize];
                _file.ReadExactly(_header);
                if (!_header.AsSpan(0, 4).SequenceEqual(EncryptedAudioFormat.Magic))
                    throw new InvalidDataException("지원하지 않는 오디오 형식입니다");

                var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(_header.AsSpan(4));
                var channels = BinaryPrimitives.ReadInt16LittleEndian(_header.AsSpan(8));
                var bits = BinaryPrimitives.ReadInt16LittleEndian(_header.AsSpan(10));
                Format = new WaveFormat(sampleRate, bits, channels);

                const int fullRecord = EncryptedAudioFormat.ChunkSize + EncryptedAudioFormat.RecordOverhead;
                var body = _file.Length - EncryptedAudioFormat.HeaderSize;
                var fullChunks = body / fullRecord;
                var remainder = body % fullRecord;
                _length = fullChunks * EncryptedAudioFormat.ChunkSize;

                if (remainder > EncryptedAudioFormat.RecordOverhead)
                {
                    // 마지막 청크가 온전한지 확인 (녹음 중 비정상 종료 대비)
                    try
                    {
                        _cache = ReadChunk(fullChunks);
                        _cachedChunk = fullChunks;
                        _length += _cache.Length;
                    }
                    catch (CryptographicException) { }
                }
            }
            catch
            {
                _file.Dispose();
                throw;
            }
        }

        private byte[] ReadChunk(long index)
        {
            const int fullRecord = EncryptedAudioFormat.ChunkSize + EncryptedAudioFormat.RecordOverhead;
            var start = EncryptedAudioFormat.HeaderSize + index * fullRecord;
            var size = (int)Math.Min(fullRecord, _file.Length - start);
            var record = new byte[size];
            _file.Position = start;
            _file.ReadExactly(record);
            return CryptoService.Open(_key, record, EncryptedAudioFormat.Aad(_header, index));
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var total = 0;
            while (count > 0 && _position < _length)
            {
                var chunk = _position / EncryptedAudioFormat.ChunkSize;
                if (chunk != _cachedChunk)
                {
                    _cache = ReadChunk(chunk);
                    _cachedChunk = chunk;
                }

                var inChunk = (int)(_position % EncryptedAudioFormat.ChunkSize);
                var n = Math.Min(count, _cache.Length - inChunk);
                if (n <= 0) break;
                Buffer.BlockCopy(_cache, inChunk, buffer, offset, n);
                _position += n;
                offset += n;
                count -= n;
                total += n;
            }
            return total;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                _ => _length + offset
            };
            return _position;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position
        {
            get => _position;
            set => _position = Math.Clamp(value, 0, _length);
        }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _file.Dispose();
            base.Dispose(disposing);
        }
    }
}
