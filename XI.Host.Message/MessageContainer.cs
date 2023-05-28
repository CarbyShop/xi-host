using System;
using ZeroMQ;

namespace XI.Host.Message
{
    public class MessageContainer : IDisposable
    {
        private bool disposedValue;

        public byte[] from;
        public byte type;
        public ZFrame extraIn;
        public ZFrame packet;
        public byte[] extraData;

        public MessageContainer(in ZMessage zMessage)
        {
            from = zMessage.Pop().Read();
            type = zMessage.PopAsByte();
            extraIn = zMessage.Pop();
            packet = zMessage.Pop();
            extraData = extraIn.Read();
        }

        public uint GetId()
        {
            return BitConverter.ToUInt32(extraData, 0);
        }

        public ushort GetZoneId()
        {
            return BitConverter.ToUInt16(extraData, 2);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                    extraIn.Dispose();
                    packet.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~MessageContainer()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
