using XI.Host.Common;
using System;
using System.Net;
using System.Reflection;
using System.Threading;
using ZeroMQ;

namespace XI.Host.Message
{
    public class ServerMessageRouter : ZSocket
    {
        private delegate void ReceiveMessageDelegate();
        public delegate void ServerMessageRouterEventHandler(ServerMessageRouter sender, MessageEventArgs args);

        public event ServerMessageRouterEventHandler Received;

        private bool disposedValue = false;
        private Thread receiveMessageThread;

        // If type initialization error, make sure all necessary C++ redist are installed.
        // https://stackoverflow.com/questions/38714669/libzmq-dll-filenotfoundexception
        public ServerMessageRouter(in IPAddress ipAddress, in ushort port) : base(ZSocketType.ROUTER)
        {
            // I don't think this should be set.  When the object is disposed, the listener thread will abort.
            //ReceiveTimeout = new TimeSpan(0, 0, 0, 60);

            try
            {
                Bind($"tcp://{ipAddress}:{port}");

                receiveMessageThread = new Thread(ReceiveMessageWorker);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, MethodBase.GetCurrentMethod());
            }
        }

        public bool BeginReceiveMessages()
        {
            bool result = true;

            try
            {
                receiveMessageThread.Start();
            }
            catch (Exception ex)
            {
                result = false;
                Logger.Error(ex, MethodBase.GetCurrentMethod());
            }

            return result;
        }

        private void ReceiveMessageWorker()
        {
            try
            {
                while (!disposedValue)
                {
                    ZMessage message = null;

                    // Protect from zmq and the receiver's code.
                    try
                    {
                        // Both are blocking calls.
                        message = ReceiveMessage();

                        Received(this, new MessageEventArgs(message));
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, MethodBase.GetCurrentMethod());
                    }
                    finally
                    {
                        message?.Dispose();
                    }
                }
            }
            catch (Exception)
            {
                // Thrown during dispose; ignore.
            }
        }

        public new void Dispose()
        {
            if (disposedValue) return;

            disposedValue = true;

            try
            {
                // Not supported.
                //receiveMessageThread?.Abort();

                receiveMessageThread?.Join(TimeSpan.FromSeconds(1));
            }
            catch (Exception) { }            

            base.Dispose();
        }
    }
}
