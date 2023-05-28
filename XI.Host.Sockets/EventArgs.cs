using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using XI.Host.Common;

namespace XI.Host.Sockets
{
    public class SocketEventArgs : EventArgs
    {
        private Socket instance;

        public bool Cancel { get; set; }
        public byte[] Data { get; private set; }
        public byte[] Response { get; set; }

        public string IpAddressString = string.Empty;
        public uint ClientAddr = 0;

        public IPAddress IpAddress
        {
            get
            {
                IPAddress result = IPAddress.Any;

                try
                {
                    IPEndPoint ipEndPoint = instance.RemoteEndPoint as IPEndPoint;

                    result = ipEndPoint.Address;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, MethodBase.GetCurrentMethod());
                }

                return result;
            }
        }

        public SocketEventArgs(in Socket socket) : this(socket, null) { }

        public SocketEventArgs(in Socket socket, in byte[] data) : base()
        {
            if (socket != null && socket.RemoteEndPoint != null)
            {
                IPEndPoint ipEndPoint = socket.RemoteEndPoint as IPEndPoint;

                IpAddressString = ipEndPoint.Address.ToString();
                ClientAddr = ipEndPoint.Address.ToCPP();
            }
            else
            {
                Logger.Warning("socket (or its RemoteEndPoint) property is null.", MethodBase.GetCurrentMethod());
            }

            instance = socket;

            Cancel = false;
            Data = data;
        }
    }
}
