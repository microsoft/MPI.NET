using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MPI
{
    public class ActionStream : Stream
    {
        Func<byte[],int> readAction;
        Action<byte[]> writeAction;
        Action flushAction;
        readonly int maxWriteSize;
        byte[] writeBuffer;
        int writePosition;

        public ActionStream(Func<byte[],int> readAction, Action<byte[]> writeAction, Action flushAction = null)
            : this(readAction, writeAction, flushAction, int.MaxValue)
        {
        }

        /// <summary>
        /// Buffers writes into blocks of maxWriteSize bytes; a partial block is emitted only on Flush.
        /// </summary>
        public ActionStream(Func<byte[],int> readAction, Action<byte[]> writeAction, Action flushAction,
            int maxWriteSize)
        {
            if (maxWriteSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxWriteSize));
            this.readAction = readAction;
            this.writeAction = writeAction;
            this.flushAction = flushAction;
            this.maxWriteSize = maxWriteSize;
        }

        public override bool CanRead
        {
            get
            {
                return (readAction != null);
            }
        }

        public override bool CanSeek
        {
            get
            {
                return false;
            }
        }

        public override bool CanWrite
        {
            get
            {
                return (writeAction != null);
            }
        }

        public override long Length
        {
            get
            {
                throw new NotImplementedException("get Length");
            }
        }

        public override long Position
        {
            get
            {
                throw new NotImplementedException("get Position");
            }

            set
            {
                throw new NotImplementedException("set Position");
            }
        }

        public override void Flush()
        {
            if (writePosition > 0)
            {
                var bytes = new byte[writePosition];
                Array.Copy(writeBuffer, bytes, writePosition);
                writeBuffer = null;
                writePosition = 0;
                writeAction(bytes);
            }
            flushAction?.Invoke();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || count > buffer.Length - offset)
                throw new ArgumentOutOfRangeException();
            if (count == 0)
                return 0;
            if (offset == 0 && count == buffer.Length)
                return readAction(buffer);
            var bytes = new byte[count];
            int read = readAction(bytes);
            Array.Copy(bytes, 0, buffer, offset, read);
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException("Seek");
        }

        public override void SetLength(long value)
        {
        }

        public override void Write(byte[] array, int offset, int count)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));
            if (offset < 0 || count < 0 || count > array.Length - offset)
                throw new ArgumentOutOfRangeException();
            if (count == 0)
                return;
            if (maxWriteSize == int.MaxValue)
            {
                if (offset == 0 && count == array.Length)
                    writeAction(array);
                else
                {
                    var bytes = new byte[count];
                    Array.Copy(array, offset, bytes, 0, count);
                    writeAction(bytes);
                }
                return;
            }
            while (count > 0)
            {
                if (writeBuffer == null)
                    writeBuffer = new byte[maxWriteSize];
                int length = Math.Min(count, maxWriteSize - writePosition);
                Array.Copy(array, offset, writeBuffer, writePosition, length);
                offset += length;
                count -= length;
                writePosition += length;
                if (writePosition == maxWriteSize)
                {
                    var bytes = writeBuffer;
                    writeBuffer = null;
                    writePosition = 0;
                    writeAction(bytes);
                }
            }
        }

        public override void Close()
        {
            Flush();
        }
    }
}
