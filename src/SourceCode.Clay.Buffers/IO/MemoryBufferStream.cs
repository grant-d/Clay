#region License

// Copyright (c) K2 Workflow (SourceCode Technology Holdings Inc.). All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

#endregion

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SourceCode.Clay.IO
{
    public sealed class MemoryBufferStream : Stream
    {
        #region Fields

        private readonly ReadOnlyMemory<byte> _memory;

        private readonly object _lock;

        private int _position;

        #endregion

        #region Classes

        private sealed class CompletedWaitHandle : WaitHandle
        {
            #region Fields

            public static readonly CompletedWaitHandle Instance = new CompletedWaitHandle();

            #endregion

            #region Constructors

            public CompletedWaitHandle()
            {
                GC.SuppressFinalize(this);
            }

            #endregion

            #region Methods

            public override bool WaitOne() => true;

            public override bool WaitOne(int millisecondsTimeout) => true;

            public override bool WaitOne(int millisecondsTimeout, bool exitContext) => true;

            public override bool WaitOne(TimeSpan timeout) => true;

            public override bool WaitOne(TimeSpan timeout, bool exitContext) => true;

            public override void Close()
            {
            }

            #endregion
        }

        private sealed class SyncAsyncResult : IAsyncResult
        {
            #region Properties

            public object AsyncState { get; }

            public WaitHandle AsyncWaitHandle => CompletedWaitHandle.Instance;

            public bool CompletedSynchronously => true;

            public bool IsCompleted => true;

            public int BytesCopied { get; }

            public AsyncCallback AsyncCallback { get; }

            #endregion

            #region Constructors

            public SyncAsyncResult(object asyncState, int bytesCopied, AsyncCallback asyncCallback)
            {
                AsyncState = asyncState;
                BytesCopied = bytesCopied;
                AsyncCallback = asyncCallback;
            }

            #endregion

            #region Methods

            public void ThreadPoolWorkItem(object state)
            {
                using (AsyncWaitHandle)
                {
                    AsyncCallback?.Invoke(this);
                }
            }

            #endregion
        }

        #endregion

        #region Properties

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanTimeout => false;

        public override bool CanWrite => false;

        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0 || value > _memory.Length) throw new ArgumentOutOfRangeException(nameof(value));
                _position = (int)value;
            }
        }

        public override long Length => _memory.Length;

        #endregion

        #region Constructors

        public MemoryBufferStream(ReadOnlyMemory<byte> memory)
        {
            _memory = memory;
            _lock = new object();
        }

        #endregion

        #region Methods

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public override long Seek(long offset, SeekOrigin origin)
        {
            lock (_lock)
            {
                switch (origin)
                {
                    case SeekOrigin.Begin:

                        if (offset < 0) return _position = 0;
                        return _position = (int)Math.Min(offset, _memory.Length);

                    case SeekOrigin.Current:

                        return _position = (int)Math.Max(
                            Math.Min(_position + offset, _memory.Length),
                            0
                        );

                    case SeekOrigin.End:

                        if (offset > 0) return _position = _memory.Length;
                        return _position = (int)Math.Max(_memory.Length + offset, 0);

                    default: throw new ArgumentOutOfRangeException(nameof(origin));
                }
            }
        }

        public override void Close()
        {
        }

        #endregion

        #region Read

        public int Read(Span<byte> buffer)
        {
            lock (_lock)
            {
                if (buffer.IsEmpty) return 0;

                var remaining = _memory.Length - _position;
                if (remaining == 0) return 0;

                var toCopy = Math.Min(remaining, buffer.Length);
                _memory.Span.Slice(_position, toCopy).CopyTo(buffer);
                _position += toCopy;
                return toCopy;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            return Read(new Span<byte>(buffer, offset, count));
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Task.FromResult(Read(buffer, offset, count));

        public Task<int> ReadAsync(Memory<byte> buffer)
            => ReadAsync(buffer, default);

        public Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
            => Task.FromResult(Read(buffer.Span));

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            return BeginRead(new Memory<byte>(buffer, offset, count), callback, state);
        }

        public IAsyncResult BeginRead(Memory<byte> buffer, AsyncCallback callback, object state)
        {
            var read = Read(buffer.Span);
            var async = new SyncAsyncResult(state, read, callback);
            ThreadPool.QueueUserWorkItem(async.ThreadPoolWorkItem);
            return async;
        }

        public override int EndRead(IAsyncResult asyncResult)
        {
            if (asyncResult is SyncAsyncResult sync)
                return sync.BytesCopied;
            throw new ArgumentOutOfRangeException(nameof(asyncResult), "asyncResult was not created by this stream.");
        }

        public override int ReadByte()
        {
            lock (_lock)
            {
                if (_position == _memory.Length) return -1;
                var result = _memory.Span[_position++];
                return result;
            }
        }

        public override void CopyTo(Stream destination, int bufferSize)
        {
            lock (_lock)
            {
                if (_position == _memory.Length) return;

                var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
                try
                {
                    var source = _memory.Span.Slice(_position);
                    for (var i = _position; i < _memory.Length; i += buffer.Length)
                    {
                        var toCopy = Math.Min(source.Length, buffer.Length);
                        source.Slice(0, toCopy).CopyTo(buffer);
                        source = source.Slice(toCopy);

                        destination.Write(buffer, 0, toCopy);
                        _position = i;
                    }

                    _position = _memory.Length;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }

        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            var position = _position;
            if (position == _memory.Length) return;

            var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                var source = _memory.Slice(position);
                for (var i = position; i < _memory.Length; i += buffer.Length)
                {
                    var toCopy = Math.Min(source.Length, buffer.Length);
                    source.Slice(0, toCopy).Span.CopyTo(buffer);
                    source = source.Slice(toCopy);

                    await destination.WriteAsync(buffer, 0, toCopy, cancellationToken);
                    _position = i;
                }

                _position = _memory.Length;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        #endregion

        #region Not Supported

        private static Exception CreateNotSupportedException()
            => new NotSupportedException("The stream is not writeable.");

        public override void SetLength(long value)
            => throw CreateNotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw CreateNotSupportedException();

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => throw CreateNotSupportedException();

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
            => throw CreateNotSupportedException();

        public override void EndWrite(IAsyncResult asyncResult)
            => throw CreateNotSupportedException();

        public override void WriteByte(byte value)
            => throw CreateNotSupportedException();

        #endregion
    }
}
