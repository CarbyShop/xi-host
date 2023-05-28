using System;
using System.Net;
using ZeroMQ;

namespace XI.Host.Message
{
    public class Receiver : IDisposable
    {
        public static readonly int ADDRESS_LENGTH = sizeof(ulong);

        public ZFrame Address;
        public uint? ID;

        public Receiver(in uint ipAddress, in ushort port) : this(ipAddress, port, null) { }

        public Receiver(in uint ipAddress, in ushort port, in uint? id) : this(new IPAddress(BitConverter.GetBytes(ipAddress)), port, id) { }

        public Receiver(string ipAddress, in ushort port) : this(ipAddress, port, null) { }

        public Receiver(string ipAddress, in ushort port, in uint? id) : this(IPAddress.Parse(ipAddress), port, id) { }

        public Receiver(in IPAddress ipAddress, in ushort port) : this(ipAddress, port, null) { }

        public Receiver(in IPAddress ipAddress, in ushort port, in uint? id)
        {
            byte[] address = new byte[ADDRESS_LENGTH];
            byte[] ipAddressBytes = ipAddress.GetAddressBytes();
            byte[] portBytes = BitConverter.GetBytes(port);

            Array.Copy(ipAddressBytes, address, ipAddressBytes.Length);
            Array.Copy(portBytes, 0, address, ipAddressBytes.Length, portBytes.Length);

            Address = new ZFrame(address);
            ID = id;
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    //-TODO: dispose managed state (managed objects).
                    Address.Dispose();
                }

                //-TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                //-TODO: set large fields to null.

                disposedValue = true;
            }
        }

        //-TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~Receiver()
        // {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            //-TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}
