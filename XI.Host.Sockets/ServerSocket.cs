using XI.Host.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace XI.Host.Sockets
{
    public class ServerSocket : Socket
    {
        [Flags]
        public enum AutomaticBehaviors
        {
            /// <summary>
            /// No automatic behaviors.
            /// </summary>
            None = 0x00,
            /// <summary>
            /// Automatically bind to the supplied address and port.
            /// </summary>
            Bind = 0x01,
            /// <summary>
            /// Automatically start listening using DEFAULT_BACKLOG.
            /// </summary>
            Listen = 0x02,
            /// <summary>
            /// Automatically begin accepting connections.
            /// </summary>
            Accept = 0x04,
            /// <summary>
            /// Automatically bind, listen, and begin accepting connections.
            /// </summary>
            All = 0x07
        }

        public delegate void ServerSocketEventHandler(ServerSocket sender, ClientSocket client, SocketEventArgs args);

        public event ServerSocketEventHandler Connecting;
        public new event ServerSocketEventHandler Connected;
        public event ServerSocketEventHandler Authenticating;
        public event ServerSocketEventHandler Sent;
        public event ServerSocketEventHandler Received;
        public event ServerSocketEventHandler Disconnected;

        public static readonly int DEFAULT_BACKLOG = 511; // Same backlog Redis uses.

        // Static-ness should NOT be here because each server is distinct and should not block one another.
        private readonly object acceptSynchronizer = new object();

        private bool disposedValue = false;
        private readonly AsyncCallback acceptCallback;

        public bool IsListening { get; private set; }
        public bool IsAccepting { get; private set; }
        public bool SupportsAuthentication { get; private set; }
        public ushort Port { get; private set; }
        public ushort ClientReceiveBufferSize { get; set; }
        public int Backlog { get; set; }
        public ConcurrentDictionary<uint, ClientSocket> Clients { get; private set; }
        
        public ServerSocket(in ushort port) : this(IPAddress.Any, port, ushort.MaxValue, DEFAULT_BACKLOG, false, AutomaticBehaviors.All) { }

        public ServerSocket(in ushort port, in bool supportsAuthentication) : this(IPAddress.Any, port, ushort.MaxValue, DEFAULT_BACKLOG, supportsAuthentication, AutomaticBehaviors.All) { }

        public ServerSocket(in ushort port, in ushort clientReceiveBufferSize) : this(IPAddress.Any, port, clientReceiveBufferSize, DEFAULT_BACKLOG, false, AutomaticBehaviors.All) { }

        public ServerSocket(in ushort port, in ushort clientReceiveBufferSize, int backlog) : this(IPAddress.Any, port, clientReceiveBufferSize, backlog, false, AutomaticBehaviors.All) { }

        public ServerSocket(in ushort port, in ushort clientReceiveBufferSize, int backlog, bool supportsAuthentication) : this(IPAddress.Any, port, clientReceiveBufferSize, backlog, supportsAuthentication, AutomaticBehaviors.All) { }

        public ServerSocket(in IPAddress address, in ushort port) : this(address, port, ushort.MaxValue, DEFAULT_BACKLOG, false, AutomaticBehaviors.All) { }

        public ServerSocket(in IPAddress address, in ushort port, in ushort clientReceiveBufferSize) : this(address, port, clientReceiveBufferSize, DEFAULT_BACKLOG, false, AutomaticBehaviors.All) { }

        public ServerSocket(in IPAddress address, in ushort port, in ushort clientReceiveBufferSize, in int backlog) : this(address, port, clientReceiveBufferSize, backlog, false, AutomaticBehaviors.All) { }

        public ServerSocket(in IPAddress address, in ushort port, in ushort clientReceiveBufferSize, in int backlog, in bool supportsAuthentication) : this(address, port, clientReceiveBufferSize, backlog, supportsAuthentication, AutomaticBehaviors.All) { }

        public ServerSocket(in IPAddress address, in ushort port, in ushort clientReceiveBufferSize, in int backlog, in bool supportsAuthentication, in AutomaticBehaviors behavior) : base(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            try
            {
                acceptCallback = new AsyncCallback(AcceptCallback);

                SupportsAuthentication = supportsAuthentication;
                Port = port;
                ClientReceiveBufferSize = clientReceiveBufferSize;
                Backlog = backlog;
                Clients = new ConcurrentDictionary<uint, ClientSocket>();

                if ((behavior & AutomaticBehaviors.Bind) == AutomaticBehaviors.Bind)
                {
                    if (Bind(address) && (behavior & AutomaticBehaviors.Listen) == AutomaticBehaviors.Listen)
                    {
                        if (Listen() && (behavior & AutomaticBehaviors.Accept) == AutomaticBehaviors.Accept)
                        {
                            if (AcceptAsync())
                            {
                                // TODO?
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, MethodBase.GetCurrentMethod());
            }
        }

        public bool Bind()
        {
            return Bind(IPAddress.Any);
        }

        public bool Bind(IPAddress address)
        {
            bool result = true;

            try
            {
                if (!IsListening)
                {
                    // Warning: Unplugging a hard wire can result in the inability to bind (this case, to 0.0.0.0).
                    // Exception: An attempt was made to access a socket in a way forbidden by its access permissions.
                    // Fix (Host Network Service) by running: net stop hns && net start hns
                    // Source: https://stackoverflow.com/questions/10461257/an-attempt-was-made-to-access-a-socket-in-a-way-forbidden-by-its-access-permissi
                    Bind(new IPEndPoint(address, Port));
                }
                else
                {
                    Logger.Warning("Already listening.", MethodBase.GetCurrentMethod());
                    result = false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, MethodBase.GetCurrentMethod());
                result = false;
            }

            return result;
        }

        public bool Listen()
        {
            return Listen(Backlog);
        }

        public new bool Listen(int backlog)
        {
            bool result = true;

            try
            {
                if (!IsListening)
                {
                    base.Listen(backlog);

                    IsListening = true;
                }
                else
                {
                    Logger.Warning("Already listening.", MethodBase.GetCurrentMethod());
                    result = false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, MethodBase.GetCurrentMethod());
                result = false;
            }

            return result;
        }

        public bool AcceptAsync()
        {
            return AcceptAsync(false);
        }

        private bool AcceptAsync(bool reaccept)
        {
            bool result = true;

            try
            {
                if (IsListening)
                {
                    // Critical section; one attempt at a time (all threads).
                    lock (acceptSynchronizer)
                    {
                        if (reaccept || !IsAccepting)
                        {
                            BeginAccept(acceptCallback, this);

                            IsAccepting = true;
                        }
                        else
                        {
                            // This is a bit ambiguous whether it should return true or false.  True is the better option since
                            // it more accurately describes what the user wanted.
                            Logger.Warning("Accept called when already accepting and not a re-accept.", MethodBase.GetCurrentMethod());
                            result = true;
                        }
                    }
                }
                else
                {
                    Logger.Warning("Not listening.", MethodBase.GetCurrentMethod());
                    result = false;
                }
            }
            catch (ObjectDisposedException ex)
            {
                Logger.Warning(ex, MethodBase.GetCurrentMethod());
                result = false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, MethodBase.GetCurrentMethod());
                result = false;
            }

            return result;
        }

        private void AcceptCallback(IAsyncResult result)
        {
            Socket socket;

            try
            {
                socket = result.AsyncState as Socket;

                if (socket != null)
                {
                    // Dispose will cause EndAccept to throw an exception, but that is actually what we want, so don't
                    // prevent it.  The underlying socket depends on exceptions to be thrown to do things gracefully.
                    Socket client = socket.EndAccept(result);

                    if (client != null)
                    {
                        SocketEventArgs args = new SocketEventArgs(client);

                        try
                        {
                            Connecting(this, null, args);
                        }
                        catch (Exception ex)
                        {
                            // User handler threw an exception.
                            Logger.Error(ex, MethodBase.GetCurrentMethod());
                            args.Cancel = true;
                        }

                        if (!args.Cancel)
                        {
                            ClientSocket clientSocket = new ClientSocket(client, ClientReceiveBufferSize, SupportsAuthentication);

                            clientSocket.Authenticating += ClientSocket_Authenticating;
                            clientSocket.Sent += ClientSocket_Sent;
                            clientSocket.Received += ClientSocket_Received;
                            clientSocket.Disconnected += ClientSocket_Disconnected;

                            if (clientSocket.ReceiveAsync())
                            {
                                try
                                {
                                    Connected(this, clientSocket, args);

                                    if (args.Cancel)
                                    {
                                        client.Shutdown(SocketShutdown.Receive);
                                        client.Disconnect(false);

                                        // probably should not call dispose here, since the disconnection logic handles that?
                                        //client.Dispose();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    // User handler threw an exception.
                                    Logger.Error(ex, MethodBase.GetCurrentMethod());
                                    args.Cancel = true;
                                }
                            }
                        }
                        else
                        {
                            client.Shutdown(SocketShutdown.Receive);
                            client.Disconnect(false);

                            // probably should not call dispose here, since the disconnection logic handles that?
                            //client.Dispose();
                        }
                    }
                    else
                    {
                        Logger.Warning("EndAccept returned a null socket.", MethodBase.GetCurrentMethod());
                    }
                }
                else
                {
                    Logger.Warning("AsyncState is null.", MethodBase.GetCurrentMethod());
                }
            }
            catch (ObjectDisposedException ex)
            {
                Logger.Warning(ex, MethodBase.GetCurrentMethod());
            }
            catch (Exception ex)
            {
                Logger.Error(ex, MethodBase.GetCurrentMethod());
            }
            finally
            {
                if (!disposedValue)
                {
                    AcceptAsync(true);
                }
            }
        }

        public bool Broadcast(byte[] buffer)
        {
            bool result = true;
            IEnumerator<KeyValuePair<uint, ClientSocket>> enumerator = null;

            try
            {
                enumerator = Clients.GetEnumerator();

                ClientSocket maintenanceClient;

                while (enumerator.MoveNext())
                {
                    maintenanceClient = enumerator.Current.Value;

                    maintenanceClient.Send(buffer);
                }
            }
            catch (Exception ex)
            {
                result = false;
                Logger.Error(ex, MethodBase.GetCurrentMethod());
            }
            finally
            {
                enumerator?.Dispose();
            }

            return result;
        }

        public void DisconnectClients()
        {
            DisposeClients(true);
        }

        private void DisposeClients(bool disconnect)
        {
            if (Clients != null && Clients.Count > 0)
            {
                using (IEnumerator<KeyValuePair<uint, ClientSocket>> enumerator = Clients.GetEnumerator())
                {
                    while (enumerator.MoveNext())
                    {
                        if (enumerator.Current.Value != null)
                        {
                            if (disconnect)
                            {
                                try
                                {
                                    enumerator.Current.Value.Disconnect();
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error(ex, MethodBase.GetCurrentMethod());
                                }
                            }

                            enumerator.Current.Value.Dispose();
                        }
                    }
                }

                Clients.Clear();
            }
        }

        private void ClientSocket_Authenticating(ClientSocket sender, SocketEventArgs args)
        {
            try
            {
                Authenticating(this, sender, args);

                if (!args.Cancel)
                {
                    if (!Clients.TryAdd(sender.AccountId, sender))
                    {
                        args.Cancel = true;
                        Logger.Warning($"Connection from {args.IpAddressString} could not authenticate, but the associated account ID {sender.AccountId} was already in the server's client list.", MethodBase.GetCurrentMethod());
                    }
                }
                else
                {
                    // TODO In what scenario do we actually not want to respond?  Banned?  Let them timeout to delay their attempts?
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, MethodBase.GetCurrentMethod());
            }
            finally
            {

            }
        }

        private void ClientSocket_Sent(ClientSocket sender, SocketEventArgs args)
        {
            try
            {
                Sent(this, sender, args);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, MethodBase.GetCurrentMethod());
            }
            finally
            {

            }
        }

        private void ClientSocket_Received(ClientSocket sender, SocketEventArgs args)
        {
            try
            {
                Received(this, sender, args);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, MethodBase.GetCurrentMethod());
            }
            finally
            {

            }
        }

        private void ClientSocket_Disconnected(ClientSocket sender, SocketEventArgs args)
        {
            try
            {
                Clients.TryRemove(sender.AccountId, out ClientSocket client);

                // Proper order should be remove from collection and then raise disconnected event?
                // Yes, the user should not expect the ServerSocket to keep track of a disconnected socket AND the
                // socket itself is available in the event parameters, thus no need to look it up.

                // Whether collection removal succeded or not, unhook events to prevent memory leak.
                sender.Authenticating -= ClientSocket_Authenticating;
                sender.Sent -= ClientSocket_Sent;
                sender.Received -= ClientSocket_Received;
                sender.Disconnected -= ClientSocket_Disconnected;
                // No need to call dispose, it will dispose itself.

                // Don't raise in finally; user handler.
                Disconnected(this, sender, args);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, MethodBase.GetCurrentMethod());
            }
            finally
            {

            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                disposedValue = true;

                try
                {
                    DisposeClients(false);

                    // No receive pending, only accept, so don't shutdown.
                    // Shutdown(SocketShutdown.Receive);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, MethodBase.GetCurrentMethod());
                }
                finally
                {
                    base.Dispose(disposing);
                }
            }
        }
    }
}
