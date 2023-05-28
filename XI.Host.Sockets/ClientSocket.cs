using XI.Host.Common;
using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace XI.Host.Sockets
{
    public class ClientSocket : IDisposable
    {
        public delegate void ClientSocketEventHandler(ClientSocket sender, SocketEventArgs args);

        public event ClientSocketEventHandler Authenticating;
        public event ClientSocketEventHandler Sent;
        public event ClientSocketEventHandler Received;
        public event ClientSocketEventHandler Disconnected;

        // Static-ness is important here so that only one client can authenticate at a time no matter how many instances exist.
        private static object authenticateSynchronizer = new object();

        private byte[] buffer;
        private AsyncCallback receiveCallback;

        public readonly string IpAddressString = string.Empty;

        #region Properties
        private Socket Client { get; set; }

        public bool MustAuthenticate { get; private set; }
        public bool IsAuthenticated { get; private set; }
        public uint ClientAddress { get; private set; }
        public byte[] HostAddressBytes { get; private set; }

        public uint AccountId { get; set; }
        public ulong ActiveKey { get; set; }
        #endregion

        //public IPAddress IpAddress
        //{
        //    get
        //    {
        //        IPAddress result = IPAddress.Any;

        //        try
        //        {
        //            // Client.RemoteEndPoint can be null when the View socket disconnects.
        //            if (Client != null && Client.RemoteEndPoint != null)
        //            {
        //                IPEndPoint ipEndPoint = Client.RemoteEndPoint as IPEndPoint;

        //                result = ipEndPoint.Address;
        //            }
        //            else
        //            {
        //                Logger.Warning("Client (or its RemoteEndPoint) property is null.", MethodBase.GetCurrentMethod());
        //            }
        //        }
        //        catch (ObjectDisposedException)
        //        {
        //            // Ignore; client was disposed.
        //        }
        //        catch (Exception ex)
        //        {
        //            Logger.Error(ex, MethodBase.GetCurrentMethod());
        //        }

        //        return result;
        //    }
        //}

        public ClientSocket(Socket client, ushort receiveBufferSize, bool mustAuthenticate)
        {
            buffer = new byte[receiveBufferSize];
            receiveCallback = new AsyncCallback(ReceiveHandler);
            MustAuthenticate = mustAuthenticate;
            Client = client;
            HostAddressBytes = client.GetHostAddressBytes();
            
            IPEndPoint ipEndPoint = client.RemoteEndPoint as IPEndPoint;

            IpAddressString = ipEndPoint.Address.ToString();
            ClientAddress = ipEndPoint.Address.ToCPP();
        }

        public bool ReceiveAsync()
        {
            bool result = true;

            try
            {
                Client.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, receiveCallback, Client);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, MethodBase.GetCurrentMethod());
                result = false;
            }

            return result;
        }

        private void ReceiveHandler(IAsyncResult result)
        {
            Socket socket = null;

            try
            {
                socket = result.AsyncState as Socket;

                if (socket != null)
                {
                    int byteCount = socket.EndReceive(result);

                    if (byteCount > 0) // Graceful disconnect.
                    {
                        byte[] data = new byte[byteCount];

                        Array.Copy(buffer, data, byteCount); // Array.Copy(buffer, 0, data, 0, byteCount);

                        try
                        {
                            SocketEventArgs args = new SocketEventArgs(socket, data);

                            if (MustAuthenticate && !IsAuthenticated)
                            {
                                // Critical section; one attempt at a time (all threads).
                                lock (authenticateSynchronizer)
                                {
                                    Authenticating(this, args);

                                    IsAuthenticated = !args.Cancel;
                                }
                            }
                            else
                            {
                                Received(this, args);
                            }

                            if (args.Response != null && args.Response.Length > 0)
                            {
                                // Could add sending event here.

                                if (socket.Send(args.Response) > 0)
                                {
                                    Sent(this, new SocketEventArgs(socket, args.Response));
                                }
                                else
                                {
                                    Logger.Warning("Could not respond with any bytes.", MethodBase.GetCurrentMethod());
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, MethodBase.GetCurrentMethod());
                        }
                        finally
                        {
                            // It is possible for the client to send data and immediately disconnect, so check before
                            // trying to get more data.
                            if (socket.Connected)
                            {
                                socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, receiveCallback, socket);
                            }
                        }
                    }
                    else
                    {
                        try
                        {
                            socket.Shutdown(SocketShutdown.Receive);
                            // Do not need to call disconnect, the fact that we got 0 bytes means the remote end disconnected.
                            //socket.Disconnect(false);

                            Disconnected?.Invoke(this, new SocketEventArgs(socket));
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, MethodBase.GetCurrentMethod());
                        }
                        finally
                        {
                            // Don't need to test for null because we are not explicitly setting that anywhere.
                            socket.Dispose();
                        }
                    }
                }
                else
                {
                    Logger.Warning("AsyncState is null.", MethodBase.GetCurrentMethod());
                }
            }
            catch (SocketException ex)
            {
                // Note: When the socket sends data, some clients don't respond, in which case TimedOut is the error,
                // which means the socket is no longer useful; must raise Disconnect event.
                // SocketException.ErrorCode and SocketException.SocketErrorCode are exactly the same value, just different types.
                if (ex.SocketErrorCode == SocketError.ConnectionReset || ex.SocketErrorCode == SocketError.ConnectionAborted || ex.SocketErrorCode == SocketError.TimedOut) // Ungraceful disconnect
                {
                    try
                    {
                        // Disconnected can be null, stack trace says it came from socket.Disconnect(true) above, change to false.
                        Disconnected?.Invoke(this, new SocketEventArgs(socket));
                    }
                    catch (Exception inner)
                    {
                        Logger.Error(inner, MethodBase.GetCurrentMethod());
                    }
                    finally
                    {
                        // Don't need to test for null because we are not explicitly setting that anywhere.
                        socket.Dispose();
                    }
                }
                else
                {
                    Logger.Error(ex, MethodBase.GetCurrentMethod());
                }
            }
            catch (Exception ex)
            {
                // Just because an exception was thrown doesn't mean the socket is bad, so don't raise Disconnected.
                Logger.Error(ex, MethodBase.GetCurrentMethod());
            }
        }

        public bool Send(byte[] buffer)
        {
            bool result = false;

            try
            {
                result = Client.Send(buffer) > 0;

                if (result)
                {
                    Sent(this, new SocketEventArgs(Client, buffer));
                }
                else
                {
                    Logger.Warning("Could not respond with any bytes.", MethodBase.GetCurrentMethod());
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, MethodBase.GetCurrentMethod());
            }

            return result;
        }

        public bool Disconnect()
        {
            return Disconnect(false);
        }

        public bool Disconnect(bool reuseSocket)
        {
            bool result = true;

            try
            {
                if (Client != null)
                {
                    Client.Disconnect(reuseSocket);
                    // Do not call dispose, that is handled automatically.
                }
                else
                {
                    Logger.Warning("Client property is null.", MethodBase.GetCurrentMethod());
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, MethodBase.GetCurrentMethod());
                result = false;
            }

            return result;
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
                    Client?.Dispose();
                }

                //-TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                //-TODO: set large fields to null.

                disposedValue = true;
            }
        }

        //-TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~ClientSocket()
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
