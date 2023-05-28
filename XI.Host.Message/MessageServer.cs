using XI.Host.Common;
using XI.Host.SQL;
using System;
using System.Collections.Generic;
using System.Data;
using System.Net;
using System.Reflection;
using ZeroMQ;

namespace XI.Host.Message
{
    public class MessageServer : IDisposable
    {
        public static class MessageRouterTypes
        {
            public const byte LOGIN = 0;
            public const byte CHAT_TELL = 1;
            public const byte CHAT_PARTY = 2;
            public const byte CHAT_LINKSHELL = 3;
            public const byte UNITY = 4;
            public const byte CHAT_YELL = 5;
            public const byte CHAT_SERVER = 6;
            public const byte PARTY_INVITE = 7;
            public const byte PARTY_INVITE_RESPONSE = 8;
            public const byte PARTY_RELOAD = 9;
            public const byte PARTY_DISBAND = 10;
            public const byte DIRECT = 11;
            public const byte LINKSHELL_RANK_CHANGE = 12;
            public const byte LINKSHELL_REMOVE = 13;
            public const byte LUA_FUNCTION = 14;
            public const byte CHARVAR_UPDATE = 15;
            public const byte SEND_TO_ZONE = 16;
            public const byte SEND_TO_ENTITY = 17;
            public const byte RPC_SEND = 18;
            public const byte RPC_RECV = 19;
        }

        private static readonly object receiverSynchronizer = new object();

        private readonly MySqlConnectionManager Database;
        private readonly ServerMessageRouter MessageRouter;
        private readonly Func<DataRow, Receiver> newReceiverOnServerFunc = (row) => { return new Receiver(row.ServerAddress(), row.ServerPort()); };
        private readonly Func<DataRow, Receiver> newReceiverOnServerWithIdFunc = (row) => { return new Receiver(row.ServerAddress(), row.ServerPort(), row.MinimumCharacterId()); };
        private readonly Func<DataRow, Receiver> newReceiverByZoneFunc = (row) => { return new Receiver(row.ZoneIpAddress(), row.ZonePort()); };

        private Func<MessageContainer, List<Receiver>> ToCharacter;
        private Func<MessageContainer, List<Receiver>> ToParty;
        private Func<MessageContainer, List<Receiver>> ToLinkshell;
        private Func<MessageContainer, List<Receiver>> ToYell;
        private Func<MessageContainer, List<Receiver>> ToServer;
        private Func<MessageContainer, List<Receiver>> ToDirect;
        private Func<MessageContainer, List<Receiver>> ToEntity;
        private Func<MessageContainer, List<Receiver>>[] RounterFuncs;

        private Func<MessageContainer, List<Receiver>> routerFunc;
        private List<Receiver> receivers;

        private void CreateFuncs()
        {
            ToCharacter = messageContainer =>
            {
                List<Receiver> receivers;

                string charname = Utilities.TryReadUntil(messageContainer.extraData, 4);

                using (DataTable accounts_sessions = Database.Select(Statements.Select.SERVER_ADDRESS_PORT_BY_CHARNAME, Columns.Select.SERVER_ADDRESS_PORT, new Couple<object>("charname", charname)))
                {
                    if (accounts_sessions != null && accounts_sessions.Rows.Count > 0)
                    {
                        DataRow row;

                        receivers = new List<Receiver>();

                        for (int i = 0; i < accounts_sessions.Rows.Count; i++)
                        {
                            row = accounts_sessions.Rows[i];
                            //newReceiverOnServerFunc.Invoke(row);
                            receivers.Add(new Receiver(row.ServerAddress(), row.ServerPort()));
                        }
                    }
                    else
                    {
                        uint charid = messageContainer.GetId();

                        receivers = Database.SelectInvokeEachRow<Receiver>(
                            newReceiverOnServerFunc,
                            Statements.Select.SERVER_ADDRESS_PORT_BY_CHARID,
                            Columns.Select.SERVER_ADDRESS_PORT,
                            new Couple<object>(Columns.CHARACTER_ID_COLUMN, charid)
                        );
                    }
                }

                return receivers;
            };

            ToParty = messageContainer =>
            {
                uint partyId = messageContainer.GetId();

                return Database.SelectInvokeEachRow<Receiver>(
                    newReceiverOnServerWithIdFunc,
                    Statements.Select.SERVER_ADDRESS_PORT_BY_PARTYID,
                    Columns.Select.SERVER_ADDRESS_PORT_BY_PARTYID,
                    new Couple<object>("partyid", partyId)
                );
            };

            ToLinkshell = messageContainer =>
            {
                uint lsId = messageContainer.GetId();

                return Database.SelectInvokeEachRow<Receiver>(
                    newReceiverOnServerFunc,
                    Statements.Select.SERVER_ADDRESS_PORT_BY_LINKSHELL,
                    Columns.Select.SERVER_ADDRESS_PORT,
                    new Couple<object>("lsid", lsId)
                );
            };

            // TODO yell is not global by default
            ToYell = messageContainer =>
            {
                return Database.SelectInvokeEachRow<Receiver>(
                    newReceiverByZoneFunc,
                    Statements.Select.ZONE_ADDRESS_PORT_BY_MISC,
                    Columns.Select.ZONE_ADDRESS_PORT,
                    null
                );
            };

            ToServer = messageContainer =>
            {
                return Database.SelectInvokeEachRow<Receiver>(
                    newReceiverByZoneFunc,
                    Statements.Select.ZONE_ADDRESS_PORT_BY_MISC,
                    Columns.Select.ZONE_ADDRESS_PORT,
                    null
                );
            };

            ToDirect = messageContainer =>
            {
                uint charId = messageContainer.GetId();

                return Database.SelectInvokeEachRow<Receiver>(
                    newReceiverOnServerFunc,
                    Statements.Select.SERVER_ADDRESS_PORT_BY_CHARID,
                    Columns.Select.SERVER_ADDRESS_PORT,
                    new Couple<object>(Columns.CHARACTER_ID_COLUMN, charId)
                );
            };

            ToEntity = messageContainer =>
            {
                ushort zoneId = messageContainer.GetZoneId();

                return Database.SelectInvokeEachRow<Receiver>(
                    newReceiverByZoneFunc,
                    Statements.Select.ZONE_ADDRESS_PORT_BY_ID,
                    Columns.Select.ZONE_ADDRESS_PORT,
                    new Couple<object>("zoneid", zoneId)
                );
            };

            RounterFuncs = new Func<MessageContainer, List<Receiver>>[]
            {
                null, // 0 LOGIN
                ToCharacter, // 1 CHAT_TELL
                ToParty, // 2 CHAT_PARTY
                ToLinkshell, // 3 CHAT_LINKSHELL
                null, // 4 TODO UNITY - TODO ToEntity
                ToYell, // 5 CHAT_YELL
                ToServer, // 6 CHAT_SERVER
                ToDirect, // 7 PARTY_INVITE
                ToDirect, // 8 PARTY_INVITE_RESPONSE
                ToParty, // 9 PARTY_DISBAND
                ToParty, // 10 PARTY_DISBAND
                ToDirect, // 11 DIRECT
                ToCharacter, // 12 LINKSHELL_RANK_CHANGE
                ToCharacter, // 13 LINKSHELL_REMOVE
                ToEntity, // 14 LUA_FUNCTION
                ToCharacter, // 15 CHARVAR_UPDATE
                ToDirect, // 16 SEND_TO_ZONE
                ToEntity, // 17 SEND_TO_ENTITY
                ToEntity, // 18 RPC_SEND
                ToEntity, // 19 RPC_RECV
            };
        }

        public MessageServer()
        {
            Logger.Information("Starting...", MethodBase.GetCurrentMethod());

            // Instantiation order should be from last disposed to first to ensure everything is setup for the first incoming connection.
            Database = new MySqlConnectionManager();

            MessageRouter = new ServerMessageRouter(IPAddress.Any, Global.GetConfigAsUShort("ZMQ_PORT"));
            MessageRouter.Received += MessageServer_Received;
            MessageRouter.BeginReceiveMessages();

            CreateFuncs();

            Logger.Information("Started.", MethodBase.GetCurrentMethod());
        }

        private void TrySend(in Receiver receiver, in MessageContainer messageContainer)
        {
            try
            {
                using (ZMessage message = new ZMessage())
                {
                    ZFrame extraOut = messageContainer.extraIn.Duplicate();

                    if (receiver.ID.HasValue)
                    {
                        extraOut.Write(receiver.ID.Value);
                    }

                    message.Add(receiver.Address);
                    message.Add(new ZFrame(messageContainer.type));
                    message.Add(extraOut);
                    message.Add(messageContainer.packet.Duplicate());

                    MessageRouter.Send(message);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, MethodBase.GetCurrentMethod());
            }
            finally
            {
                receiver.Dispose();
            }
        }

        #region "Message Events"
        private void MessageServer_Received(ServerMessageRouter sender, MessageEventArgs args)
        {
            // Do not need to explicitly dispose ZMessage here, the caller will do it, so we have to use/copy the data
            // before that happens.
            lock (receiverSynchronizer)
            {
                using (var messageContainer = new MessageContainer(args.Message))
                {
                    if (messageContainer.type < RounterFuncs.Length) // Prevent index out-of-range.
                    {
                        // Because this code block is synchronized, reuse the pointer.
                        routerFunc = RounterFuncs[messageContainer.type];

                        if (routerFunc != null) // Some indices might intentionally be null.
                        {
                            // Because this code block is synchronized, reuse the pointer.
                            receivers = routerFunc.Invoke(messageContainer);

                            if (receivers != null && receivers.Count >= 1) // Still possible to return no receivers.
                            {
                                Logger.Information($"Type '{messageContainer.type}' to {receivers.Count} receiver(s).", MethodBase.GetCurrentMethod());

                                // zmq does not support threaded-safe sending.  An internal exception is thrown.
                                // ParallelLoopResult result = 
                                // Parallel.For(0, receivers.Count, (i) =>
                                // {
                                //    TrySend(receivers, messageContainer, i);
                                // });

                                // It is cheaper both in memory and cycles to skip looping if there is only one
                                // recipiant.  However, this makes it slower sending to multiple recipients.
                                //if (receivers.Count == 1)
                                //{
                                //    TrySend(receivers[0], messageContainer);
                                //}
                                //else
                                //{
                                for (int i = 0; i < receivers.Count; i++)
                                {
                                    TrySend(receivers[i], messageContainer);
                                }
                                //}

                                receivers.Clear();
                            }
                            else
                            {
                                Logger.Information($"Message has no receiver(s).", MethodBase.GetCurrentMethod());
                            }
                        }
                        else
                        {
                            Logger.Warning($"Message type {messageContainer.type} has no handler.", MethodBase.GetCurrentMethod());
                        }
                    }
                    else
                    {
                        Logger.Warning($"Message type {messageContainer.type} is not defined.", MethodBase.GetCurrentMethod());
                    }
                }
            }
        }
        #endregion

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects).
                    Logger.Information("Stopping...", MethodBase.GetCurrentMethod());

                    if (MessageRouter != null)
                    {
                        MessageRouter.Received -= MessageServer_Received;
                        MessageRouter.Dispose();
                    }

                    Database?.Dispose();

                    Logger.Information("Stopped.", MethodBase.GetCurrentMethod());
                    Logger.Close();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~MessageServer()
        // {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}
