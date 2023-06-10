using XI.Host.Common;
using XI.Host.Sockets;
using XI.Host.SQL;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace XI.Host.Login
{
    public class LoginServer : IDisposable
    {
        private static class AccountStatus
        {
            public const byte NORMAL = 1;
            public const byte BANNED = 2;
            public const byte SUSPENDED = 3;
            public const byte INACTIVE = 4;
        }

        private static class AuthenticationRequest
        {
            public const byte LOGIN = 0x10;
            public const byte CREATE = 0x20;
            public const byte UPDATE = 0x30;
        }

        private static class ViewRequest
        {
            public const byte ID = 0x07;
            public const byte DELETE = 0x14;
            public const byte CONTINUE = 0x1F;
            public const byte SAVE = 0x21;
            public const byte VALIDATE = 0x22;
            public const byte SERVER = 0x24;
            public const byte VERSION = 0x26;
        }

        private static class DataRequest
        {
            public const byte CHARACTERS = 0xA1;
            public const byte SELECT = 0xA2;
        }

        private static class Nation
        {
            public const byte San_dOria = 0x00;
            public const byte Bastok = 0x01;
            public const byte Windurst = 0x02;
        }

        private enum VersionLockTypes : byte
        {
            Disabled = 0,
            Exact = 1,
            GTE = 2,
        }

        #region "Constants"
        // This number is used in many places and defining it as a constant makes all instances easy to find.
        // Incidentally, a time test reveals in-lining the value is faster.
        private const int SIXTEEN = 16;
        private const int MAXIMUM_USERNAME_LENGTH = SIXTEEN;
        private const int MAXIMUM_PASSWORD_LENGTH = SIXTEEN;
        private const int MAXIMUM_CHARACTER_NAME_LENGTH = SIXTEEN;
        private const int MAXIMUM_SERVER_NAME_LENGTH = SIXTEEN;
        private const byte MAXIMUM_CONTENT_ID_COUNT = SIXTEEN;
        private const int SESSION_KEY_SHIFT_OFFSET = SIXTEEN;
        private const uint MAXIMUM_CHARACTER_ID = 0x00FFFFFF;
        #endregion

        private static readonly bool ACCOUNT_CREATION_ENABLED = Global.GetConfigAsBoolean("ACCOUNT_CREATION");
        private static readonly bool CHARACTER_DELETION_ENABLED = Global.GetConfigAsBoolean("CHARACTER_DELETION");
        private static readonly bool NEW_CHARACTER_CUTSCENE = Global.GetConfigAsBoolean("NEW_CHARACTER_CUTSCENE");
        private static readonly bool LOG_USER_IP = Global.GetConfigAsBoolean("LOG_USER_IP");
        private static readonly int IP_LOGIN_LIMIT = Global.GetConfigAsInt32("LOGIN_LIMIT");
        private static readonly byte WORLD_ID = 0; // TODO make configurable

        // Static-ness is important here so that only one operation can happen at a time no matter how many clients are
        // requesting.  I.e. Pound sand until your turn.
        private static readonly object createSynchronizer = new object();
        private static readonly object saveSynchronizer = new object();

        private static readonly Regex regexCharacterName = new Regex("^[A-Z]{1}[a-z]{2,14}$", RegexOptions.Compiled);

        private readonly Random random = new Random((DateTime.UtcNow.Year << 16) & ((DateTime.UtcNow.Minute * 4) << 8) & (DateTime.UtcNow.Second * 4));
        private readonly Action<ClientSocket, SocketEventArgs, AuthenticationResponse, CredentialContainer>[] AuthenticationRequests = new Action<ClientSocket, SocketEventArgs, AuthenticationResponse, CredentialContainer>[(AuthenticationRequest.UPDATE >> 4) + 1];
        private readonly Action<ServerSocket, ClientSocket, SocketEventArgs>[] ViewRequests = new Action<ServerSocket, ClientSocket, SocketEventArgs>[ViewRequest.VERSION + 1];
        private readonly Action<ClientSocket, SocketEventArgs>[] DataRequests = new Action<ClientSocket, SocketEventArgs>[DataRequest.SELECT + 1];
        private readonly Func<DataRow, byte> funcGetContentIdCount = (row) => { return row.ContentIdCount(); };

        private readonly VersionLockTypes versionLockType = VersionLockTypes.Disabled;

        private readonly ConcurrentDictionary<uint, Selection> PendingSessions;
        private readonly ConcurrentDictionary<ulong, Selection> ActiveSessions;
        private readonly ConcurrentDictionary<uint, List<uint>> ViewSessionIds;
        private readonly ConcurrentQueue<Selection> ClosedSessions;
        private readonly ConcurrentDictionary<uint, DateTime> CreateLockout;
        private readonly MySqlConnectionManager Database;
        private readonly ServerSocket ViewServer;
        private readonly ServerSocket DataServer;
        private readonly ServerSocket AuthenticationServer;

        // There are two types in .NET, so don't remove the System.Threading qualification.
        private readonly System.Threading.TimerCallback CleanUpSessionsCallback;
        private readonly System.Threading.Timer CleanUpSessionsTimer;

        private int cleanUpSessionsRunning = 0;

        #region "Properties"
        public bool MaintenanceMode { get; private set; }
        public bool ClearSessionsOnDispose { get; set; }
        public uint ServerID { get; private set; }
        public byte[] ServerName { get; private set; }
        public uint ExpectedVersion { get; private set; }
        public byte[] SearchPortBytes { get; private set; }
        public ushort ServerPopulationLimit { get; private set; }
        public int CleanUpSessionsFrequency { get; private set; }
        public int DelayCharacterCreatedResponse { get; set; }
        #endregion

        public LoginServer()
        {
            Logger.Information("Starting...", MethodBase.GetCurrentMethod());

            AuthenticationRequests[AuthenticationRequest.LOGIN >> 4] = new Action<ClientSocket, SocketEventArgs, AuthenticationResponse, CredentialContainer>(AuthenticateCredentials);
            AuthenticationRequests[AuthenticationRequest.CREATE >> 4] = new Action<ClientSocket, SocketEventArgs, AuthenticationResponse, CredentialContainer>(AuthenticateCredentialsAndCreate);
            AuthenticationRequests[AuthenticationRequest.UPDATE >> 4] = new Action<ClientSocket, SocketEventArgs, AuthenticationResponse, CredentialContainer>(AuthenticateCredentialsAndUpdate);

            // Presented order is the same as the flow of data between client and server.
            ViewRequests[ViewRequest.VERSION] = new Action<ServerSocket, ClientSocket, SocketEventArgs>(ViewServer_Received_Version);
            ViewRequests[ViewRequest.CONTINUE] = new Action<ServerSocket, ClientSocket, SocketEventArgs>(ViewServer_Received_Continue);
            ViewRequests[ViewRequest.SERVER] = new Action<ServerSocket, ClientSocket, SocketEventArgs>(ViewServer_Received_Server);
            ViewRequests[ViewRequest.ID] = new Action<ServerSocket, ClientSocket, SocketEventArgs>(ViewServer_Received_Id);
            ViewRequests[ViewRequest.SAVE] = new Action<ServerSocket, ClientSocket, SocketEventArgs>(ViewServer_Received_Save);
            ViewRequests[ViewRequest.VALIDATE] = new Action<ServerSocket, ClientSocket, SocketEventArgs>(ViewServer_Received_Validate);
            ViewRequests[ViewRequest.DELETE] = new Action<ServerSocket, ClientSocket, SocketEventArgs>(ViewServer_Received_Delete);

            DataRequests[DataRequest.CHARACTERS] = new Action<ClientSocket, SocketEventArgs>(DataServer_Received_Characters);
            DataRequests[DataRequest.SELECT] = new Action<ClientSocket, SocketEventArgs>(DataServer_Received_Select);

            ServerID = 0x64;
            ServerName = Encoding.UTF8.GetBytes(Global.Config["SERVER_NAME"]);
            MaintenanceMode = !Global.Config["MAINT_MODE"].Equals("0");
            ExpectedVersion = uint.Parse(Global.Config["CLIENT_VER"].Replace('_', '0'));
            SearchPortBytes = BitConverter.GetBytes(Global.GetConfigAsUInt32("SEARCH_PORT"));
            ServerPopulationLimit = Global.GetConfigAsUShort("ServerPopulationLimit");
            DelayCharacterCreatedResponse = Global.GetConfigAsInt32("DelayCharacterCreatedResponse");

            versionLockType = (VersionLockTypes)byte.Parse(Global.Config["VER_LOCK"]);
            PendingSessions = new ConcurrentDictionary<uint, Selection>();
            ActiveSessions = new ConcurrentDictionary<ulong, Selection>();
            ViewSessionIds = new ConcurrentDictionary<uint, List<uint>>();
            ClosedSessions = new ConcurrentQueue<Selection>();
            CreateLockout = new ConcurrentDictionary<uint, DateTime>();

            // Instantiation order should be from last disposed to first to ensure everything is setup for the first incoming connection.
            Database = new MySqlConnectionManager();

            ushort clientReceiveBufferSize = Global.GetConfigAsUShort("ClientReceiveBufferSize");
            int tcpServerBacklog = Global.GetConfigAsInt32("TcpServerBacklog");

            DataServer = new ServerSocket(Global.GetConfigAsUShort("LOGIN_DATA_PORT"), clientReceiveBufferSize, tcpServerBacklog);
            DataServer.Connecting += DataServer_Connecting;
            DataServer.Connected += DataServer_Connected;
            DataServer.Sent += DataServer_Sent;
            DataServer.Received += DataServer_Received;
            DataServer.Disconnected += DataServer_Disconnected;

            ViewServer = new ServerSocket(Global.GetConfigAsUShort("LOGIN_VIEW_PORT"), clientReceiveBufferSize, tcpServerBacklog);
            ViewServer.Connecting += ViewServer_Connecting;
            ViewServer.Connected += ViewServer_Connected;
            ViewServer.Sent += ViewServer_Sent;
            ViewServer.Received += ViewServer_Received;
            ViewServer.Disconnected += ViewServer_Disconnected;

            AuthenticationServer = new ServerSocket(Global.GetConfigAsUShort("LOGIN_AUTH_PORT"), clientReceiveBufferSize, tcpServerBacklog, true);
            AuthenticationServer.Connecting += AuthenticationServer_Connecting;
            AuthenticationServer.Connected += AuthenticationServer_Connected;
            AuthenticationServer.Authenticating += AuthenticationServer_Authenticating;
            AuthenticationServer.Sent += AuthenticationServer_Sent;
            AuthenticationServer.Received += AuthenticationServer_Received;
            AuthenticationServer.Disconnected += AuthenticationServer_Disconnected;

            // Always last, because it starts immediately.
            CleanUpSessionsFrequency = Global.GetConfigAsInt32("CleanUpSessionsFrequency");
            CleanUpSessionsCallback = new System.Threading.TimerCallback(TryCleanUpSessions);
            CleanUpSessionsTimer = new System.Threading.Timer(CleanUpSessionsCallback, null, 0, CleanUpSessionsFrequency * 1000);

            Logger.Information("Started.", MethodBase.GetCurrentMethod());
        }

        private void TryCleanUpSessions(object state)
        {
            // Seems like the timer doesn't support pausing.  Use an atomic operation to determine overlapping
            // invocations.  Perhaps this is no longer needed, as heartbeating has been separated.
            if (Interlocked.Increment(ref cleanUpSessionsRunning) > 1)
            {
                Logger.Warning("Taking too long to run; skipping the current requested clean-up.", MethodBase.GetCurrentMethod());
            }
            else
            {
                // Failure here will crash the program; must catch.
                try
                {
                    if (PendingSessions != null)
                    {
                        using (IEnumerator<KeyValuePair<uint, Selection>> enumerator = PendingSessions.GetEnumerator())
                        {
                            DateTime now = DateTime.UtcNow.ToLocalTime();

                            while (enumerator.MoveNext())
                            {
                                Selection selection = enumerator.Current.Value;

                                if (selection != null)
                                {
                                    // Wait a few seconds for the user to accept the agreement, else give'em the boot.
                                    if (now >= selection.Authenticated.AddSeconds(CleanUpSessionsFrequency))
                                    {
                                        if (selection.Data != null)
                                        {
                                            if (PendingSessions.TryRemove(selection.Data.ClientAddress, out _))
                                            {
                                                continue; // TODO disconnect?
                                            }
                                        }

                                        if (selection.View != null)
                                        {
                                            if (PendingSessions.TryRemove(selection.View.ClientAddress, out _))
                                            {
                                                continue; // TODO disconnect?
                                            }
                                        }
                                        //PendingSessions.TryRemove(selection.AccountId, out selection);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, MethodBase.GetCurrentMethod());
                }

                try
                {
                    DateTime now = DateTime.UtcNow.ToLocalTime();

                    if (ClosedSessions != null && ClosedSessions.TryPeek(out Selection selection))
                    {
                        if (now >= selection.Closed.AddSeconds(CleanUpSessionsFrequency))
                        {
                            if (ClosedSessions.TryDequeue(out Selection _))
                            {
                                // Failure most likely means it wasn't in the table.
                                //SqlManager.Delete(Statements.Delete.ACCOUNTS_SESSIONS, new Couple<object>(Columns.ACCOUNT_ID_COLUMN, selection.AccountId));
                            }
                            else
                            {
                                Logger.Warning("Failed to dequeue a clean up session.", MethodBase.GetCurrentMethod());
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, MethodBase.GetCurrentMethod());
                }
            }

            Interlocked.Decrement(ref cleanUpSessionsRunning);
        }

        private void AuthenticationErrorResponse(in IResponse response, in byte errorCode)
        {
            response.Append(errorCode);
            response.Pad(20);
        }

        private byte[] ViewErrorResponse(in uint errorCode)
        {
            byte[] result;

            using (var viewResponse = new ViewResponse(ViewResponse.Codes.ERROR))
            {
                viewResponse.Pad(4); // Sub-error?
                viewResponse.Append(errorCode); // Primary error.

                result = viewResponse.GetBytes();
            }

            return result;
        }

        private string BuildSelectionMessage(in ulong activeKey)
        {
            string result = string.Empty;

            if (ActiveSessions.TryGetValue(activeKey, out Selection selection))
            {
                result = $" > Acct [{selection.AccountId}] > Char [{selection.CharacterId}] > Name [{selection.CharacterName}]";
            }

            return result;
        }

        private string BuildArgsDataMessage(in byte[] data, in byte type)
        {
            string result = string.Empty;

            if (data != null)
            {
                string argsDataLength = data.Length.ToString();

                if (data.Length > 0)
                {
                    string typeString = type.ToString("X");
                    result = $" > args.Data.Length [{argsDataLength}] > type [0x{typeString}]";
                }
                else
                {
                    result = $" > args.Data.Length [{argsDataLength}]";
                }
            }

            return result;
        }

        private string BuildLogMessage(in ClientSocket client, in SocketEventArgs args, in byte type = 0)
        {
            string result;

            if (client != null)
            {
                result = string.Concat($"ip = {client.IpAddressString}", BuildSelectionMessage(client.ActiveKey), BuildArgsDataMessage(args.Data, type));
            }
            else
            {
                result = $"ip = {args.IpAddressString}";
            }

            return result;
        }

        private bool HasGM(in uint accid)
        {
            bool result = false;

            using (DataTable chars_gm = Database.Select(Statements.Select.CHARS_GM, Columns.Select.CHARID, new Couple<object>(Columns.ACCOUNT_ID_COLUMN, accid)))
            {
                if (chars_gm != null)
                {
                    result = chars_gm.Rows.Count > 0;
                }
            }

            return result;
        }

        private bool IpAllowed(in uint client_addr)
        {
            bool result = true;

            if (IP_LOGIN_LIMIT > 0)
            {
                using (DataTable accounts_sessions_ip_count = Database.Select(Statements.Select.ACCOUNTS_SESSIONS_IP, Columns.Select.ACCOUNTS_SESSIONS_IP, new Couple<object>(Columns.CLIENT_ADDRESS_COLUMN, client_addr)))
                {
                    if (accounts_sessions_ip_count != null)
                    {
                        result = accounts_sessions_ip_count.Rows.Count < IP_LOGIN_LIMIT;
                    }
                }
            }

            return result;
        }

        private bool IsAllowed(in uint accid, in uint client_addr)
        {
            if (MaintenanceMode)
            {
                return HasGM(accid);
            }

            return IpAllowed(client_addr);
        }

        private CredentialContainer GetCredentials(in IResponse response, in SocketEventArgs args, in bool getMac)
        {
            CredentialContainer result = null;
            string username = Utilities.TryReadUntil(args.Data, 0, MAXIMUM_USERNAME_LENGTH);

            if (!string.IsNullOrEmpty(username) && username.Length >= 3 && username.Length <= MAXIMUM_USERNAME_LENGTH)
            {
                string password = Utilities.TryReadUntil(args.Data, MAXIMUM_USERNAME_LENGTH, MAXIMUM_PASSWORD_LENGTH);

                if (!string.IsNullOrEmpty(password) && password.Length >= 6 && password.Length <= MAXIMUM_PASSWORD_LENGTH)
                {
                    result = new CredentialContainer(username, password);
                }
                else
                {
                    // Password empty or too short or too long.
                    AuthenticationErrorResponse(response, AuthenticationResponse.Codes.FAIL);
                }
            }
            else
            {
                // Username empty or too short or too long.
                AuthenticationErrorResponse(response, AuthenticationResponse.Codes.FAIL);
            }

            return result;
        }

        // Aggressive inlining isn't really necessary, as the compiler does it automatically for
        // methods having only one caller.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddSession(in ClientSocket client, in SocketEventArgs args, in AuthenticationResponse response, in CredentialContainer credentials)
        {
            Couple<object> accidCouple = new Couple<object>(Columns.ACCOUNT_ID_COLUMN, client.AccountId);

            using (DataTable accounts_sessions = Database.Select(Statements.Select.ACCOUNTS_SESSIONS, Columns.Select.ACCOUNTS_SESSIONS, accidCouple))
            {
                // C++ map_cleanup can only work if the IP and port stored in
                // memory match a record in the accounts_sessions table.  In
                // this case, the user has successfully logged back in, so it
                // is safe to delete their persisted session(s).
                // Session objects in map_sessions_list will get cleaned up
                // when the last_update test exceeds the 5 second threshold.
                if (accounts_sessions != null && accounts_sessions.Rows.Count > 0)
                {
                    // Allow players to kick themselves out (even from different IP addresses).
                    Database.Delete(Statements.Delete.ACCOUNTS_SESSIONS, accidCouple);
                }

                if (IsAllowed(client.AccountId, args.ClientAddr))
                {
                    //if (SqlManager.Update(Statements.Update.ACCOUNT_ATTEMPTS, accidCouple))
                    //{
                    if (PendingSessions.TryAdd(client.ClientAddress, new Selection(client.AccountId, DateTime.UtcNow.ToLocalTime())))
                    {
                        response.Append(AuthenticationResponse.Codes.SUCCEED);
                        response.Append(client.AccountId);
                        //response.Append(byteToken.Bytes);
                        //response.Pad(235); // Confirmed, do not need.
                    }
                    else
                    {
                        // Unable to create session.
                        AuthenticationErrorResponse(response, AuthenticationResponse.Codes.FAIL);
                    }
                    //}
                    //else
                    //{
                    // Unable to reset attempts.
                    //AuthenticationErrorResponse(response, AuthenticationResponse.Codes.FAIL);
                    //}
                }
                else
                {
                    // Too many connections.
                    AuthenticationErrorResponse(response, AuthenticationResponse.Codes.TOO_MANY);
                }
            }
        }

        private bool IsNormal(DataRow row)
        {
            return row.Status() == AccountStatus.NORMAL;
        }

        private ulong GetActiveKey(uint accountId, uint clientAddress)
        {
            return (Convert.ToUInt64(accountId) << 32) | Convert.ToUInt64(clientAddress);
        }

        private bool AuthenticateWorker(in ClientSocket client, in SocketEventArgs args, in AuthenticationResponse response, in CredentialContainer credentials)
        {
            // https://stackoverflow.com/questions/732561/why-is-using-a-mysql-prepared-statement-more-secure-than-using-the-common-escape
            // Prepared SQL statements do not need escaped.
            //login = MySqlHelper.EscapeString(login);
            //password = MySqlHelper.EscapeString(password);
            //mac = MySqlHelper.EscapeString(mac);

            Couple<object>[] parameters = new Couple<object>[]
            {
                new Couple<object>(Columns.LOGIN_COLUMN, credentials.Username),
                new Couple<object>(Columns.PASSWORD_COLUMN, credentials.Password),
            };

            using (DataTable accounts = Database.Select(Statements.Select.AUTHENTICATE, Columns.Select.AUTHENTICATE, parameters))
            {
                if (accounts != null && accounts.Rows.Count == 1)
                {
                    DataRow row = accounts.Rows[0];

                    if (IsNormal(row))
                    {
                        uint accountId = row.Id();
                        ulong activeKey = GetActiveKey(accountId, client.ClientAddress);

                        if (!ActiveSessions.TryGetValue(activeKey, out Selection _))
                        {
                            client.AccountId = accountId;
                            client.ActiveKey = activeKey;

                            Couple<object>[] modifyCouple = new Couple<object>[]
                            {
                                new Couple<object>(Columns.TIME_LAST_MODIFY_COLUMN, DateTime.UtcNow.ToLocalTime().ToMySQL()),
                                new Couple<object>(Columns.ACCOUNT_ID_COLUMN, accountId),
                            };

                            Database.Update(Statements.Update.ACCOUNT_MODIFY, modifyCouple);

                            return true; // Always return true.
                        }
                        else
                        {
                            // Session still active.  Clean-up will eventually kick the session out if player were disconnected.
                            AuthenticationErrorResponse(response, AuthenticationResponse.Codes.WAIT);
                        }
                    }
                    else
                    {
                        Logger.Warning($"Disallowed login attempt from {args.IpAddressString} with username '{credentials.Username}' having account status {row.Status()}.", MethodBase.GetCurrentMethod());
                        // Don't say anything, just deny.
                        args.Cancel = true;
                    }
                }
                else
                {
                    // no matching login, or wrong password, or more than one match
                    AuthenticationErrorResponse(response, AuthenticationResponse.Codes.INVALID);
                }
            }

            return false;
        }

        private void AuthenticateCredentials(ClientSocket client, SocketEventArgs args, AuthenticationResponse response, CredentialContainer credentials)
        {
            if (AuthenticateWorker(client, args, response, credentials))
            {
                AddSession(client, args, response, credentials);
            }
        }

        private void AuthenticateCredentialsAndCreate(ClientSocket client, SocketEventArgs args, AuthenticationResponse response, CredentialContainer credentials)
        {
            if (!ACCOUNT_CREATION_ENABLED)
            {
                AuthenticationErrorResponse(response, AuthenticationResponse.Codes.CREATE_DISABLED);
                return;
            }

            lock (createSynchronizer)
            {
                DateTime now = DateTime.UtcNow.ToLocalTime();

                // Prevents rapid/brute force account creation.
                if (!CreateLockout.TryGetValue(client.ClientAddress, out DateTime until) || now > until)
                {
                    Couple<object> loginCouple = new Couple<object>(Columns.LOGIN_COLUMN, credentials.Username);

                    using (DataTable accounts_login = Database.Select(Statements.Select.ACCOUNTS_LOGIN, Columns.Select.ACCOUNTS_LOGIN, loginCouple))
                    {
                        if (accounts_login == null || accounts_login.Rows.Count == 0)
                        {
                            // TODO check for expired entries, if so delete
                            // TODO check IP collection entries < 6, if not fail

                            uint newAccountId = 0;

                            using (DataTable accounts_max_id = Database.Select(Statements.Select.ACCOUNTS_MAX_ID, Columns.Select.ACCOUNT_MAX_ID))
                            {
                                if (accounts_max_id != null && accounts_max_id.Rows.Count > 0)
                                {
                                    DataRow row = accounts_max_id.Rows[0];
                                    newAccountId = row.Max();

                                    if (newAccountId == uint.MaxValue)
                                    {
                                        // Maximum accid reached
                                        AuthenticationErrorResponse(response, AuthenticationResponse.Codes.CREATE_FAIL);
                                        return;
                                    }

                                    newAccountId++;

                                    if (newAccountId < 1000)
                                    {
                                        newAccountId = 1000;
                                    }
                                }
                                else
                                {
                                    // No rows means no accounts
                                    newAccountId = 1000; // TODO make configurable
                                }
                            }
                            // Format needed 0000-00-00 00:00:00
                            string timeCreate = now.ToLocalTime().ToMySQL();

                            Couple<object>[] createCouple = new Couple<object>[]
                            {
                                new Couple<object>(Columns.ID_COLUMN, newAccountId),
                                new Couple<object>(Columns.LOGIN_COLUMN, credentials.Username),
                                new Couple<object>(Columns.PASSWORD_COLUMN, credentials.Password),
                                new Couple<object>("timecreate", timeCreate),
                                new Couple<object>(Columns.TIME_LAST_MODIFY_COLUMN, timeCreate),
                                //new Couple<object>("status", 1), // ACCOUNT_STATUS_CODE::NORMAL
                                //new Couple<object>("priv", 1), // ACCOUNT_PRIVILIGE_CODE::USER
                            };

                            if (Database.Insert(Statements.Insert.ACCOUNTS_CREATE, createCouple))
                            {
                                // TODO replace with AddOrUpdate
                                CreateLockout.TryRemove(client.ClientAddress, out DateTime _);
                                CreateLockout.TryAdd<uint, DateTime>(client.ClientAddress, now + TimeSpan.FromMinutes(5)); // TODO make configurable

                                if (AuthenticateWorker(client, args, response, credentials))
                                {
                                    response.Append(AuthenticationResponse.Codes.CREATE_SUCCEED);
                                }
                            }
                            else
                            {
                                // Other problem
                                AuthenticationErrorResponse(response, AuthenticationResponse.Codes.CREATE_FAIL);
                            }
                        }
                        else
                        {
                            AuthenticationErrorResponse(response, AuthenticationResponse.Codes.CREATE_FAIL_TAKEN);
                        }
                    }
                }
                else
                {
                    // Lock-out period in effect.
                    AuthenticationErrorResponse(response, AuthenticationResponse.Codes.CREATE_WARNING_LOCKOUT);
                }
            }
        }

        private void AuthenticateCredentialsAndUpdate(ClientSocket client, SocketEventArgs args, AuthenticationResponse response, CredentialContainer credentials)
        {
            if (!ACCOUNT_CREATION_ENABLED)
            {
                response.Append(AuthenticationResponse.Codes.CREATE_DISABLED);
                return;
            }

            if (AuthenticateWorker(client, args, response, credentials))
            {
                response.Append(AuthenticationResponse.Codes.CHANGE_PASSWORD);
            }
        }

        #region "Authentication Events"
        // Event handlers should not try/catch/finally.  The main reason is to force callers to protect themselves
        // from being hooked up to code they don't own (and event handlers are usually declared void).
        private void AuthenticationServer_Connecting(ServerSocket sender, ClientSocket client, SocketEventArgs args)
        {
            Logger.Information(BuildLogMessage(client, args), MethodBase.GetCurrentMethod());
        }

        private void AuthenticationServer_Connected(ServerSocket sender, ClientSocket client, SocketEventArgs args)
        {
            //Logger.Information(BuildLogMessage(client, args), MethodBase.GetCurrentMethod());
        }

        private void AuthenticationServer_Authenticating(ServerSocket sender, ClientSocket client, SocketEventArgs args)
        {
            bool getMac = args.Data.Length == 50;

            args.Cancel = true; // Only one way to authenticate (set if all is good).

            // Check for exact sizes to limit denial of service points.
            if (args.Data.Length == 33 || getMac)
            {
                byte authenticationRequest = args.Data[0x20];

                Logger.Information(BuildLogMessage(client, args, authenticationRequest), MethodBase.GetCurrentMethod());

                using (var authenticationResponse = new AuthenticationResponse())
                {
                    CredentialContainer credentials = GetCredentials(authenticationResponse, args, getMac);

                    if (credentials != null)
                    {
                        int index = authenticationRequest >> 4;

                        if (index < AuthenticationRequests.Length)
                        {
                            var action = AuthenticationRequests[index];

                            if (action != null)
                            {
                                action.Invoke(client, args, authenticationResponse, credentials);
                            }
                            else
                            {
                                // Unhandled flag
                                AuthenticationErrorResponse(authenticationResponse, AuthenticationResponse.Codes.FAIL);
                            }
                        }
                        else
                        {
                            // Unhandled flag
                            AuthenticationErrorResponse(authenticationResponse, AuthenticationResponse.Codes.FAIL);
                        }
                    }
                    else
                    {
                        // AuthenticationErrorResponse already set by GetLoginInfo method.
                    }

                    args.Response = authenticationResponse.GetBytes();
                    args.Cancel = false;
                }
            }
        }

        private void AuthenticationServer_Sent(ServerSocket sender, ClientSocket client, SocketEventArgs args)
        {
            Logger.Information(BuildLogMessage(client, args), MethodBase.GetCurrentMethod());
        }

        private void AuthenticationServer_Received(ServerSocket sender, ClientSocket client, SocketEventArgs args)
        {
            args.Cancel = true; // Only one way to authenticate (set if all is good).

            Logger.Information(BuildLogMessage(client, args, Convert.ToByte(args.Data.Length)), MethodBase.GetCurrentMethod());

            if (args.Data.Length == MAXIMUM_PASSWORD_LENGTH)
            {
                using (var authenticationResponse = new AuthenticationResponse())
                {
                    if (client.IsAuthenticated)
                    {
                        string password = Utilities.TryReadUntil(args.Data, 0, MAXIMUM_PASSWORD_LENGTH);

                        if (Database.Update(Statements.Update.ACCOUNT_PASSWORD, new Couple<object>(Columns.PASSWORD_COLUMN, password), new Couple<object>(Columns.ACCOUNT_ID_COLUMN, client.AccountId)))
                        {
                            authenticationResponse.Append(AuthenticationResponse.Codes.CHANGE_PASSWORD_SUCCEED);
                        }
                        else
                        {
                            authenticationResponse.Append(AuthenticationResponse.Codes.CHANGE_PASSWORD_FAIL);
                        }
                    }
                    else
                    {
                        authenticationResponse.Append(AuthenticationResponse.Codes.CHANGE_PASSWORD_FAIL);
                    }

                    args.Response = authenticationResponse.GetBytes();
                    args.Cancel = false;
                }
            }
        }

        private void AuthenticationServer_Disconnected(ServerSocket sender, ClientSocket client, SocketEventArgs args)
        {
            Logger.Information(BuildLogMessage(client, args), MethodBase.GetCurrentMethod());
        }
        #endregion

        private bool HasValidClientVersion(SocketEventArgs args)
        {
            bool result = true;

            switch (versionLockType)
            {
                case VersionLockTypes.Exact:
                    result = (GetClientVersion(args) == ExpectedVersion);
                    break;
                case VersionLockTypes.GTE:
                    result = (GetClientVersion(args) >= ExpectedVersion);
                    break;
                default:
                    // Accept any version.
                    break;
            }

            return result;
        }

        private bool TryGetFreeCharacterId(out uint free)
        {
            bool result = true;

            // Use to create a character and test the rollover ID.
            //free = 0x00010000;
            //return true;

            // Use to create a character and test the maximum ID.
            //free = MAXIMUM_CHARACTER_ID;
            //return true;

            free = 1;

            try
            {
                // Get an open ID.  MySQL doesn't do this really well.  MSSQL supports OVER function.
                using (DataTable free_charid = Database.Select(Statements.Select.FREE_CHARID, Columns.Select.CHARID))
                {
                    if (free_charid != null && free_charid.Rows.Count > 0)
                    {
                        uint taken;

                        for (int i = 0; i < free_charid.Rows.Count; i++)
                        {
                            taken = Convert.ToUInt32(free_charid.Rows[i][Columns.CHARACTER_ID_COLUMN]);

                            if (taken > free)
                            {
                                break;
                            }

                            // can cause an overflow exception
                            free++;
                        }
                    }
                }

                if (free > MAXIMUM_CHARACTER_ID)
                {
                    result = false;
                    free = 0;
                }
            }
            catch (Exception)
            {
                result = false;
                free = 0;
            }

            return result;
        }

        private ushort GetStartingZone(in byte nation)
        {
            ushort result = 0;

            // TODO Using Action here is probably overkill, but do it anyway to be consistent.
            switch (nation)
            {
                case Nation.San_dOria:
                    result = random.NextInclusive(0xE6, 0xE8);
                    break;
                case Nation.Bastok:
                    result = random.NextInclusive(0xEA, 0xEC);
                    break;
                case Nation.Windurst:
                    do
                    {
                        result = random.NextInclusive(0xEE, 0xF1);
                    } while (result == 0xEF); // Omit Windurst Walls
                    break;
                default:
                    break;
            }

            return result;
        }

        private uint GetClientVersion(in SocketEventArgs args)
        {
            byte[] version = new byte[10];

            Array.Copy(args.Data, ViewResponse.Constants.VERSION_OFFSET, version, 0, version.Length);
            version[8] = 0x30; // overwrite _ with 0

            return uint.Parse(Encoding.UTF8.GetString(version));
        }

        // KEEP
        //static uint errorCode = 341;

        private void ViewServer_Received_Version(ServerSocket sender, ClientSocket client, SocketEventArgs args)
        {
            // KEEP Use to get/test error message details.
            //args.Response = GetErrorResponse(errorCode);
            //errorCode++;
            //return;

            // account ID is not included, by default, in this packet
            // see g_SessionHash implementation
            ulong activeKey = client.ActiveKey;
            List<uint> accountIds = null;

            // First, check pending sessions.  Reconnections won't have a pending session entry.
            if (PendingSessions.TryGetValue(client.ClientAddress, out Selection selection) && selection != null)
            {
                activeKey = GetActiveKey(selection.AccountId, client.ClientAddress);

                // Client address key is necessary for reconnection of the view socket, since it does not tell us which account it is associated.
                if (ActiveSessions.TryAdd(activeKey, selection))
                {
                    if (!PendingSessions.TryRemove(client.ClientAddress, out _))
                    {
                        Logger.Warning($"Unable to remove pending session {client.IpAddressString}", MethodBase.GetCurrentMethod());
                    }

                    if (ViewSessionIds.TryGetValue(client.ClientAddress, out accountIds))
                    {
                        if (!accountIds.Contains(selection.AccountId))
                        {
                            accountIds.Add(selection.AccountId);
                        }
                    }
                    else
                    {
                        accountIds = new List<uint>() { selection.AccountId };

                        if (!ViewSessionIds.TryAdd(client.ClientAddress, accountIds))
                        {
                            Logger.Warning($"Unable to add view session ID {selection.AccountId} for client {client.ClientAddress}", MethodBase.GetCurrentMethod());
                        }
                    }
                }
                else
                {
                    // Session still active, authenticated too quickly.
                }
            }
            else
            {
                // must be active or did not authenticate
            }

            uint associatedAccountId = BitConverter.ToUInt32(args.Data, 148); // 0x94

            if (associatedAccountId == 0) // original client data
            {
                // try to check for logged out and reconnecting
                if (accountIds == null && ViewSessionIds.TryGetValue(client.ClientAddress, out accountIds) && accountIds.Count == 1)
                {
                    activeKey = GetActiveKey(accountIds[0], client.ClientAddress);
                }
            }
            else // updated client data
            {
                activeKey = GetActiveKey(associatedAccountId, client.ClientAddress);
            }

            // Next, check active sessions.  TODO this condition could be simplified; see above
            if (ActiveSessions.TryGetValue(activeKey, out selection))
            {
                if (HasValidClientVersion(args))
                {
                    if (sender.Clients.TryAdd(selection.AccountId, client))
                    {
                        client.AccountId = selection.AccountId;
                        client.ActiveKey = activeKey;
                        selection.View = client;

                        // Clear the character data, otherwise the logging can show the previous values.
                        selection.CharacterId = 0;
                        selection.CharacterName = string.Empty;

                        using (DataTable accounts = Database.Select(Statements.Select.EXPANSIONS_FEATURES, Columns.Select.EXPANSIONS_FEATURES, new Couple<object>(Columns.ACCOUNT_ID_COLUMN, client.AccountId)))
                        {
                            if (accounts != null && accounts.Rows.Count > 0)
                            {
                                DataRow row = accounts.Rows[0];

                                using (var viewResponse = new ViewResponse(ViewResponse.Codes.VERSION))
                                {
                                    viewResponse.Append(ViewResponse.Constants.VERSION_HEADER);
                                    viewResponse.Append(row.Expansions());
                                    viewResponse.Append(row.Features());

                                    args.Response = viewResponse.GetBytes();
                                }
                            }
                            else
                            {
                                // This should never happen, but... saw it in test, why?
                                // Because the View connected from a 'logout' and from an IP that had more than one connection.
                                args.Response = ViewErrorResponse(ViewResponse.Errors.INTERNAL_ERROR_1);
                            }
                        }
                    }
                    else
                    {
                        // This also should never happen, but... saw it in test, why?
                        args.Response = ViewErrorResponse(ViewResponse.Errors.UNABLE_TO_CONNECT_WORLD_SERVER);
                    }
                }
                else
                {
                    // Invalid client version.
                    args.Response = ViewErrorResponse(ViewResponse.Errors.GAME_DATA_UPDATED_DOWNLOAD_LATEST);
                }
            }
            else
            {
                // If the login server is closed, data nor view sockets will try to reconnect; player must close the client.
                // Too many connections from the same IP to tell which client should get the data, or no active session.
                // TODO add account ID sending to the view socket in xiloader, then logout/in will work for multiple
                // connections from the same IP.
                args.Response = ViewErrorResponse(ViewResponse.Errors.UNABLE_TO_CONNECT_WORLD_SERVER);
            }
        }

        private void ViewServer_Received_Continue(ServerSocket sender, ClientSocket client, SocketEventArgs args)
        {
            if (ActiveSessions.TryGetValue(client.ActiveKey, out Selection selection) && selection.Data != null)
            {
                using (var dataResponse = new DataResponse(DataResponse.Codes.READY))
                {
                    selection.Data.Send(dataResponse.GetBytes(5));
                }
            }
            else
            {
                // no session or no data socket
                args.Response = ViewErrorResponse(ViewResponse.Errors.INTERNAL_ERROR_2);
            }
        }

        private void ViewServer_Received_Server(ServerSocket sender, ClientSocket client, SocketEventArgs args)
        {
            if (ServerPopulationLimit == 0 || ActiveSessions.Count <= ServerPopulationLimit)
            {
                using (var viewResponse = new ViewResponse(ViewResponse.Codes.SERVERS))
                {
                    // TODO how are multiple servers handled?
                    viewResponse.Append(ViewResponse.Constants.SERVERS_HEADER); // TODO Payload ID or Size?
                    viewResponse.Append(ServerID);
                    viewResponse.Append(ServerName, MAXIMUM_SERVER_NAME_LENGTH);
                    viewResponse.Pad(12);

                    args.Response = viewResponse.GetBytes();
                }
            }
            else
            {
                args.Response = ViewErrorResponse(ViewResponse.Errors.SERVER_POPULATION_LIMIT_REACHED);
            }
        }

        private void ViewServer_Received_Id(ServerSocket sender, ClientSocket client, SocketEventArgs args)
        {
            if (ActiveSessions.TryGetValue(client.ActiveKey, out Selection selection) && selection.Data != null)
            {
                selection.CharacterId = args.Data.GetCharacterId();
                selection.CharacterName = Utilities.TryReadUntil(args.Data, 36, MAXIMUM_CHARACTER_NAME_LENGTH);

                using (var dataResponse = new DataResponse(DataResponse.Codes.SET))
                {
                    selection.Data.Send(dataResponse.GetBytes(5));
                }
            }
            else
            {
                // no session or no data socket
                args.Response = ViewErrorResponse(ViewResponse.Errors.CHARACTER_RESERVATION_CANCEL_FAIL);
            }
        }

        private void ViewServer_Received_Save(ServerSocket sender, ClientSocket client, SocketEventArgs args)
        {
            // Critical section; one attempt at a time (all threads).
            lock (saveSynchronizer)
            {
                if (ActiveSessions.TryGetValue(client.ActiveKey, out Selection selection))
                {
                    if (TryGetFreeCharacterId(out uint characterId))
                    {
                        Character character = new Character(args.Data[48], args.Data[50], args.Data[54], args.Data[57], args.Data[60]);

                        character.Zone = GetStartingZone(character.Nation);

                        if (character.IsValid)
                        {
                            using (var databaseTransaction = new MySqlConnectionManager())
                            {
                                if (databaseTransaction.TryTransactionBegin())
                                {
                                    bool commit = false;

                                    // Things that make you go... hmm...?  Still feels hacky.
                                    do
                                    {
                                        Couple<object> charidCouple = new Couple<object>(Columns.CHARACTER_ID_COLUMN, characterId);

                                        Couple<object>[] parameters = new Couple<object>[]
                                        {
                                            charidCouple,
                                            new Couple<object>(Columns.ACCOUNT_ID_COLUMN, client.AccountId),
                                            new Couple<object>(Columns.CHARACTER_NAME_COLUMN, selection.CharacterName),
                                            new Couple<object>("pos_zone", character.Zone),
                                            new Couple<object>("nation", character.Nation)
                                        };

                                        commit = databaseTransaction.Insert(Statements.Insert.CHARS, parameters);
                                        if (!commit) break;

                                        parameters = new Couple<object>[]
                                        {
                                            charidCouple,
                                            new Couple<object>("face", character.Face),
                                            new Couple<object>("race", character.Race),
                                            new Couple<object>("size", character.Size)
                                        };

                                        commit = databaseTransaction.Insert(Statements.Insert.CHAR_LOOK, parameters);
                                        if (!commit) break;

                                        Couple<object> mjobCouple = new Couple<object>("mjob", character.MainJob);

                                        commit = databaseTransaction.Insert(Statements.Insert.CHAR_STATS, charidCouple, mjobCouple);
                                        if (!commit) break;

                                        //Couple<object> jobidCouple = new Couple<object>("jobid", character.mjob);

                                        //commit = SqlManagerSave.Insert(Statements.Insert.CHAR_EQUIP, charidCouple, jobidCouple);
                                        //if (!commit) break;
                                        commit = databaseTransaction.Insert(Statements.Insert.CHAR_EXP, charidCouple);
                                        if (!commit) break;
                                        commit = databaseTransaction.Insert(Statements.Insert.CHAR_JOBS, charidCouple);
                                        if (!commit) break;
                                        commit = databaseTransaction.Insert(Statements.Insert.CHAR_PET, charidCouple);
                                        if (!commit) break;
                                        commit = databaseTransaction.Insert(Statements.Insert.CHAR_POINTS, charidCouple);
                                        if (!commit) break;
                                        commit = databaseTransaction.Insert(Statements.Insert.CHAR_UNLOCKS, charidCouple);
                                        if (!commit) break;
                                        commit = databaseTransaction.Insert(Statements.Insert.CHAR_PROFILE, charidCouple);
                                        if (!commit) break;
                                        commit = databaseTransaction.Insert(Statements.Insert.CHAR_STORAGE, charidCouple);
                                        if (!commit) break;

                                        // TODO find a good way to get a positive response from 0 rows deleted.  Change return type to integer?
                                        databaseTransaction.Delete(Statements.Delete.CHAR_INVENTORY, charidCouple);

                                        commit = databaseTransaction.Insert(Statements.Insert.CHAR_INVENTORY, charidCouple);
                                        if (!commit) break;

                                        if (NEW_CHARACTER_CUTSCENE)
                                        {
                                            parameters = new Couple<object>[]
                                            {
                                                charidCouple,
                                                new Couple<object>("varname", "HQuest[newCharacterCS]notSeen"),
                                                new Couple<object>("value", 1)
                                            };

                                            commit = databaseTransaction.Insert(Statements.Insert.CHAR_CUTSCENE, parameters);
                                        }
                                    } while (false);

                                    if (commit && databaseTransaction.TryTransactionCommit())
                                    {
                                        using (var viewResponse = new ViewResponse())
                                        {
                                            viewResponse.Pad(4); // Sub-success?
                                            args.Response = viewResponse.GetBytes();
                                        }

                                        // Delay the response to let the database finalize the transaction; this may help
                                        // stuck on ...downloading data... issues when creating characters.
                                        Thread.Sleep(DelayCharacterCreatedResponse);
                                    }
                                    else
                                    {
                                        databaseTransaction.TryTransactionRollback();

                                        // save failed
                                        args.Response = ViewErrorResponse(ViewResponse.Errors.INTERNAL_ERROR_4_TRY_CHAR_CREATE_AGAIN);
                                    }
                                }
                                else
                                {
                                    // could not begin transaction
                                    args.Response = ViewErrorResponse(ViewResponse.Errors.REGISTRATION_3_FAILED);
                                }
                            }
                        }
                        else
                        {
                            Logger.Warning("Invalid character data provided during save.", MethodBase.GetCurrentMethod());

                            args.Response = ViewErrorResponse(ViewResponse.Errors.CHARACTER_PARAMS_INCORRECT);
                        }
                    }
                    else
                    {
                        Logger.Error(new ApplicationException($"No more character IDs left.  Maximum is {MAXIMUM_CHARACTER_ID}."), MethodBase.GetCurrentMethod());

                        args.Response = ViewErrorResponse(ViewResponse.Errors.ERROR_NUMBER_ONLY);
                    }
                }
                else
                {
                    // no session
                    args.Response = ViewErrorResponse(ViewResponse.Errors.INTERNAL_ERROR_3);
                }
            }
        }

        private void ViewServer_Received_Validate(ServerSocket sender, ClientSocket client, SocketEventArgs args)
        {
            if (ActiveSessions.TryGetValue(client.ActiveKey, out Selection selection))
            {
                string charname = Utilities.TryReadUntil(args.Data, 32, 15);

                // Even though the client forces a certain format, the data could be changed in-transit,
                // so enforce server-side as well.
                if (regexCharacterName.IsMatch(charname))
                {
                    // Make sure the name is not taken. TODO unless it is also deleted?
                    using (DataTable chars = Database.Select(Statements.Select.CHARNAME, Columns.Select.CHARNAME, new Couple<object>(Columns.CHARACTER_NAME_COLUMN, charname)))
                    {
                        if (chars == null || chars.Rows.Count == 0)
                        {
                            selection.CharacterName = charname;

                            using (var viewResponse = new ViewResponse())
                            {
                                viewResponse.Pad(4); // Sub-success?
                                args.Response = viewResponse.GetBytes();
                            }
                        }
                        else
                        {
                            // 313 English translation:
                            // The name you entered can't be registered because it is already in use. Please try a different name.
                            args.Response = ViewErrorResponse(ViewResponse.Errors.NAME_TAKEN);
                        }
                    }
                }
                else
                {
                    // invalid name
                    args.Response = ViewErrorResponse(ViewResponse.Errors.NAME_SERVER_REGISTRATION_FAILED);
                }
            }
            else
            {
                // no session
                args.Response = ViewErrorResponse(ViewResponse.Errors.INTERNAL_ERROR_3);
            }
        }

        private void ViewServer_Received_Delete(ServerSocket sender, ClientSocket client, SocketEventArgs args)
        {
            if (!CHARACTER_DELETION_ENABLED)
            {
                args.Response = ViewErrorResponse(ViewResponse.Errors.DELETE_FAILED_1);
                return;
            }

            uint charid = args.Data.GetCharacterId();

            // Validated view connections could intercept and try to delete chararacters that are not
            // associated with their account.
            if (charid >= 1 && charid <= ushort.MaxValue) // Valid range on Host.
            {
                if (ActiveSessions.TryGetValue(client.ActiveKey, out Selection _))
                {
                    Couple<object>[] parameters = new Couple<object>[]
                    {
                        new Couple<object>("deleted", DateTime.UtcNow.ToLocalTime().ToMySQL()),
                        new Couple<object>(Columns.ACCOUNT_ID_COLUMN, client.AccountId),
                        new Couple<object>(Columns.CHARACTER_ID_COLUMN, charid)
                    };

                    // TODO make hard delete configurable, for safety, soft delete

                    if (Database.Update(Statements.Update.CHARS_SOFT_DELETE, parameters))
                    {
                        using (var viewResponse = new ViewResponse())
                        {
                            viewResponse.Pad(4); // Sub-success?
                            args.Response = viewResponse.GetBytes();
                        }
                    }
                    else
                    {
                        // soft delete failed
                        args.Response = ViewErrorResponse(ViewResponse.Errors.DELETE_FAILED_2);
                    }
                }
                else
                {
                    // don't try again
                    args.Response = ViewErrorResponse(ViewResponse.Errors.DELETE_FAILED_1);
                }
            }
            else
            {
                // don't try again
                args.Response = ViewErrorResponse(ViewResponse.Errors.DELETE_FAILED_1);
            }
        }

        #region "View Events"
        private void ViewServer_Connecting(ServerSocket sender, ClientSocket client, SocketEventArgs args)
        {
            Logger.Information(BuildLogMessage(client, args), MethodBase.GetCurrentMethod());

            //if (MaintenanceMode)
            //{
            //    args.Cancel = !MaintenanceServer.Clients.TryGetValue(args.IpAddressString, out ClientSocket _);
            //}
        }

        private void ViewServer_Connected(ServerSocket sender, ClientSocket client, SocketEventArgs args)
        {
            Logger.Information(BuildLogMessage(client, args), MethodBase.GetCurrentMethod());
        }

        private void ViewServer_Sent(ServerSocket sender, ClientSocket client, SocketEventArgs args)
        {
            Logger.Information(BuildLogMessage(client, args), MethodBase.GetCurrentMethod());
        }

        private void ViewServer_Received(ServerSocket sender, ClientSocket client, SocketEventArgs args)
        {
            // Could test length, but the client buffer is small.
            byte viewRequest = args.Data[0x08];

            Logger.Information(BuildLogMessage(client, args, viewRequest), MethodBase.GetCurrentMethod());

            // Despite the caller being able to catch IndexOutOfRangeException and NullReferenceException,
            // it is less expensive to validate in the event that an attacker uses known thrown exceptions
            // to degrade performance or perform denial of service.
            if (viewRequest < ViewRequests.Length)
            {
                var action = ViewRequests[viewRequest];

                if (action != null)
                {
                    action.Invoke(sender, client, args);
                }
                else
                {
                    // Request index not set; say nothing.
                    Logger.Warning("View request index not set.", MethodBase.GetCurrentMethod());
                    args.Cancel = true;
                }
            }
            else
            {
                // Unsupported request; say nothing.
                Logger.Warning("Unsupported view request index.", MethodBase.GetCurrentMethod());
                args.Cancel = true;
            }
        }

        private void ViewServer_Disconnected(ServerSocket sender, ClientSocket client, SocketEventArgs args)
        {
            Logger.Information(BuildLogMessage(client, args), MethodBase.GetCurrentMethod());

            if (ActiveSessions.TryGetValue(client.ActiveKey, out Selection selection))
            {
                selection.View = null;

                if (selection.Data == null)
                {
                    if (ActiveSessions.TryRemove(client.ActiveKey, out selection))
                    {
                        selection.Closed = DateTime.UtcNow.ToLocalTime();

                        ClosedSessions.Enqueue(selection);
                    }
                    else
                    {
                        Logger.Error(new ApplicationException($"View socket for accid {client.AccountId} disconnected, but the active session could not be removed."), MethodBase.GetCurrentMethod());
                    }
                }
                else
                {
                    Logger.Warning($"View socket for accid {client.AccountId} disconnected.  Data socket is connected.", MethodBase.GetCurrentMethod());
                }
            }
            else
            {
                if (client.AccountId == 0)
                {
                    Logger.Warning($"View socket for accid {client.AccountId} disconnected.  The connection was interrupted before an accid could be assigned.", MethodBase.GetCurrentMethod());
                }
                else
                {
                    Logger.Warning($"View socket for accid {client.AccountId} disconnected, but the accid has already been removed.", MethodBase.GetCurrentMethod());
                }
            }
        }
        #endregion

        private byte GetAllocatedContentIdCount(in uint accid)
        {
            byte contentIdCount = 0;

            QueryParameterContainer queryParameterContainer = new QueryParameterContainer()
            {
                Statement = Statements.Select.CONTENT_IDS,
                Columns = Columns.Select.CONTENT_IDS,
                Parameters = new Couple<object>[] { new Couple<object>(Columns.ACCOUNT_ID_COLUMN, accid) }
            };

            contentIdCount = Database.SelectInvoke<byte>(funcGetContentIdCount, queryParameterContainer);

            if (contentIdCount > MAXIMUM_CONTENT_ID_COUNT)
            {
                contentIdCount = MAXIMUM_CONTENT_ID_COUNT;
            }

            return contentIdCount;
        }

        private uint GetUnallocatedContentIdCount(in DataTable charsDataTable, in uint allocatedContentIdCount)
        {
            uint result;

            // Don't know how retail handles owning less content ids than number of
            // characters, but this code handles it by showing all characters rather than
            // limiting by content ids.  The client will not display characters beyond the
            // reported content ID count.
            if (charsDataTable != null)
            {
                if (charsDataTable.Rows.Count >= allocatedContentIdCount)
                {
                    result = (uint)charsDataTable.Rows.Count;
                }
                else
                {
                    // Because of the value checking, don't have to worry about negative/overflow.
                    result = Convert.ToUInt32(allocatedContentIdCount - charsDataTable.Rows.Count);
                }
            }
            else
            {
                result = allocatedContentIdCount;
            }

            return result;
        }

        private void ExpandCharacterId(in uint characterId, out ushort characterIdLSBs, out byte characterIdMSBs)
        {
            characterIdLSBs = Convert.ToUInt16(characterId & 0x0000FFFF);
            characterIdMSBs = Convert.ToByte(characterId >> 16);
        }

        private void SendDataResponse(in Selection selection, in DataTable charsDataTable, in byte allocatedContentIdCount, in uint unallocatedContentIdCount)
        {
            using (var dataResponse = new DataResponse(DataResponse.Codes.LIST, allocatedContentIdCount))
            {
                int rowCount = 0;
                uint contentId;
                uint characterId = 0;
                ushort characterIdLSBs;
                byte characterIdMSBs;

                dataResponse.Pad(6);

                if (charsDataTable != null && charsDataTable.Rows.Count > 0)
                {
                    rowCount = charsDataTable.Rows.Count;

                    for (int i = 0; i < charsDataTable.Rows.Count; i++)
                    {
                        characterId = charsDataTable.Rows[i].CharacterId();
                        contentId = characterId;

                        ExpandCharacterId(characterId, out characterIdLSBs, out characterIdMSBs);

                        dataResponse.Pad(8);
                        dataResponse.Append(contentId);
                        dataResponse.Append(characterIdLSBs);
                        dataResponse.Append(WORLD_ID);
                        dataResponse.Append(characterIdMSBs);
                    }
                }

                // Fill with fake IDs, server will generate new ones.
                if (rowCount < MAXIMUM_CONTENT_ID_COUNT)
                {
                    // Make sure to reset to zero to prevent overflow exceptions.
                    if (characterId >= MAXIMUM_CHARACTER_ID)
                    {
                        characterId = 0;
                    }

                    characterId++;

                    for (uint i = characterId; i <= unallocatedContentIdCount + characterId; i++)
                    {
                        ExpandCharacterId(i, out characterIdLSBs, out characterIdMSBs);

                        dataResponse.Pad(8);
                        dataResponse.Append(i);
                        dataResponse.Append(characterIdLSBs);
                        dataResponse.Append(WORLD_ID);
                        dataResponse.Append(characterIdMSBs);
                    }
                }

                if (selection.Data != null)
                {
                    selection.Data.Send(dataResponse.GetBytes());
                }
                else
                {
                    // no session or no data socket; ignore
                }
            }
        }

        private void SendViewResponse(in Selection selection, in DataTable charsDataTable, in uint allocatedContentIdCount, in uint unallocatedContentIdCount)
        {
            uint characterId = 0;

            using (var viewResponse = new ViewResponse(ViewResponse.Codes.CHARACTERS))
            {
                int rowCount = 0;
                ushort characterIdLSBs;
                byte characterIdMSBs;

                viewResponse.Append(allocatedContentIdCount);

                if (charsDataTable != null && charsDataTable.Rows.Count > 0)
                {
                    ushort mjob;
                    uint contentid;
                    DataRow row;

                    rowCount = charsDataTable.Rows.Count;

                    for (int i = 0; i < rowCount; i++)
                    {
                        row = charsDataTable.Rows[i];

                        mjob = row.MainJob();

                        characterId = row.CharacterId();
                        contentid = characterId;

                        ExpandCharacterId(characterId, out characterIdLSBs, out characterIdMSBs);

                        // both ids required, otherwise POL-0001 or protocol timeout.
                        viewResponse.Append(contentid);
                        viewResponse.Append(characterIdLSBs);
                        viewResponse.Append(WORLD_ID);
                        viewResponse.Append(byte.MinValue);
                        viewResponse.Append(ViewResponse.Constants.ACTIVE); // INACTIVE will prevent character creation
                        viewResponse.Append(byte.MinValue);
                        viewResponse.Append(ViewResponse.Constants.KEEP_NAME); // prompt change?
                        viewResponse.Append(characterIdMSBs);
                        viewResponse.Append(row.CharacterName(), MAXIMUM_CHARACTER_NAME_LENGTH);
                        viewResponse.Append(ServerName, MAXIMUM_SERVER_NAME_LENGTH);
                        viewResponse.Append(row.Race());
                        viewResponse.Append(mjob);
                        viewResponse.Append(ViewResponse.Constants.UNKNOWN_DATA_7);
                        viewResponse.Append(row.Face());
                        viewResponse.Append(row.Size());
                        viewResponse.Append(row.Head());
                        viewResponse.Append(row.Body());
                        viewResponse.Append(row.Hands());
                        viewResponse.Append(row.Legs());
                        viewResponse.Append(row.Feet());
                        viewResponse.Append(row.MainHand());
                        viewResponse.Append(row.OffHand());
                        viewResponse.Append(row.ZoneAsByte());
                        viewResponse.Append(Convert.ToByte(row[mjob])); // mjob level is offset (1 = war = column 1)
                        viewResponse.Append(ViewResponse.Constants.UNKNOWN_DATA_4);
                        viewResponse.Append(row.ZoneAsUShort()); // Retail captures do not show this here.
                        viewResponse.Append(ViewResponse.Constants.UNKNOWN_DATA_6);
                        viewResponse.Append(ServerID);
                        viewResponse.Append(ViewResponse.Constants.PROBABLE_POL_DATA);
                    }
                }

                // Fill with fake IDs, server will generate new ones.
                if (rowCount < MAXIMUM_CONTENT_ID_COUNT)
                {
                    // Make sure to reset to zero to prevent overflow exceptions.
                    if (characterId >= MAXIMUM_CHARACTER_ID)
                    {
                        characterId = 0;
                    }

                    characterId++;

                    for (uint i = characterId; i < unallocatedContentIdCount + characterId; i++)
                    {
                        ExpandCharacterId(i, out characterIdLSBs, out characterIdMSBs);

                        viewResponse.Append(i);
                        viewResponse.Append(characterIdLSBs);
                        viewResponse.Append(WORLD_ID);
                        viewResponse.Append(byte.MinValue);
                        viewResponse.Append(ViewResponse.Constants.ACTIVE); // INACTIVE will prevent character creation
                        viewResponse.Append(byte.MinValue);
                        viewResponse.Append(ViewResponse.Constants.KEEP_NAME); // prompt change?
                        viewResponse.Append(characterIdMSBs);
                        viewResponse.Append(ViewResponse.Constants.EMPTY_NAME); // 0x20 is one of many things that hide it from the UI
                        viewResponse.Append(ViewResponse.Constants.EMPTY_NAME); // 0x20 is one of many things that hide it from the UI
                        viewResponse.Pad(4);
                        viewResponse.Append(ViewResponse.Constants.UNKNOWN_DATA_7);
                        viewResponse.Pad(2);
                        viewResponse.Append(ViewResponse.Constants.GEAR_OFFSET_0x10);
                        viewResponse.Append(ViewResponse.Constants.GEAR_OFFSET_0x20);
                        viewResponse.Append(ViewResponse.Constants.GEAR_OFFSET_0x30);
                        viewResponse.Append(ViewResponse.Constants.GEAR_OFFSET_0x40);
                        viewResponse.Append(ViewResponse.Constants.GEAR_OFFSET_0x50);
                        viewResponse.Append(ViewResponse.Constants.GEAR_OFFSET_0x60);
                        viewResponse.Append(ViewResponse.Constants.GEAR_OFFSET_0x70);
                        viewResponse.Append(Convert.ToByte(0));
                        viewResponse.Append(Convert.ToByte(1)); // mjob level is offset (1 = war = column 1)
                        viewResponse.Append(ViewResponse.Constants.UNKNOWN_DATA_12);
                        //viewSend.Append(new byte[] { 0x01, 0x00, 0x02, 0x00, 0x00, 0x00 });
                        //viewSend.Append(Convert.ToUInt16(row["pos_zone"])); // Retail captures do not show this here; added to above constant.
                        //viewSend.Append(new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 });
                        viewResponse.Append(ServerID);
                        viewResponse.Append(ViewResponse.Constants.PROBABLE_POL_DATA);
                    }
                }

                // It is possible for the session to have been removed, so query for selection again.
                if (selection.View != null)
                {
                    selection.View.Send(viewResponse.GetBytes());
                }
                else
                {
                    // no session or no view socket; ignore
                }
            }
        }

        private bool SelectionAllowed(in DataRow row)
        {
            if (MaintenanceMode)
            {
                return row.GameMasterLevel() > 0; // TODO make configurable
            }

            return true;
        }

        private StringBuilder GetSessionKey(byte[] sessionKeyBytes)
        {
            StringBuilder session_key = new StringBuilder();

            for (int i = 0; i < sessionKeyBytes.Length; i++)
            {
                session_key.AppendFormat("{0:x2}{1}", sessionKeyBytes[i], string.Empty);
            }

            return session_key;
        }

        private int GetSessionCount(ClientSocket client)
        {
            int result = 0;
            // TODO stopwatch
            using (DataTable dataTable = Database.Select(Statements.Select.SESSION_COUNT, Columns.Select.SESSION_COUNT, new Couple<object>(Columns.CLIENT_ADDRESS_COLUMN, client.ClientAddress)))
            {
                if (dataTable != null && dataTable.Rows.Count > 0)
                {
                    DataRow row = dataTable.Rows[0];

                    result = Convert.ToInt32(row["count"]);
                }
            }

            return result;
        }

        private ulong GetExceptionTime(ClientSocket client)
        {
            ulong result = 0;
            // TODO stopwatch
            using (DataTable dataTable = Database.Select(Statements.Select.EXCEPTION_TIME, Columns.Select.EXCEPTION_TIME, new Couple<object>(Columns.ACCOUNT_ID_COLUMN, client.AccountId)))
            {
                if (dataTable != null && dataTable.Rows.Count > 0)
                {
                    DataRow row = dataTable.Rows[0];

                    result = Convert.ToUInt64(row["exception"]);
                }
            }

            return result;
        }

        private bool LoginLimitOkay(ClientSocket client)
        {
            int sessionCount = GetSessionCount(client);
            ulong exceptionTime = GetExceptionTime(client);

            ulong timeStamp = Convert.ToUInt64((DateTime.UtcNow.ToLocalTime() - DateTime.UnixEpoch.ToLocalTime()).TotalSeconds);
            bool excepted = exceptionTime > timeStamp;
            //bool isGM = gmlevel > 0; // Previously checked.

            return IP_LOGIN_LIMIT == 0 || sessionCount < IP_LOGIN_LIMIT || excepted;
        }

        private void InsertAccountIpRecord(ClientSocket client, Selection selection)
        {
            var parameters = new Couple<object>[] {
                new Couple<object>("login_time", DateTime.UtcNow.ToLocalTime().ToMySQL()),
                new Couple<object>(Columns.ACCOUNT_ID_COLUMN, client.AccountId),
                new Couple<object>(Columns.CHARACTER_ID_COLUMN, selection.CharacterId),
                new Couple<object>(Columns.CLIENT_IP_COLUMN, client.ClientAddress)
            };

            Database.Insert(Statements.Insert.ACCOUNT_IP_RECORD, parameters);
        }

        private void DataServer_Received_Characters(ClientSocket client, SocketEventArgs args)
        {
            Selection selection;

            // Do not test the ID given first, instead test whether it has previously been set.
            // Once the data client's associated account ID is set, it cannot be changed.  Hi-jack prevention.
            if (client.AccountId != uint.MinValue)
            {
                if (ActiveSessions.TryGetValue(client.ActiveKey, out selection))
                {
                    using (DataTable charsDataTable = Database.Select(Statements.Select.CHARS, Columns.Select.CHARS, new Couple<object>(Columns.ACCOUNT_ID_COLUMN, client.AccountId)))
                    {
                        byte allocatedContentIdCount = GetAllocatedContentIdCount(client.AccountId);
                        uint unallocatedContentIdCount = GetUnallocatedContentIdCount(charsDataTable, allocatedContentIdCount);

                        SendDataResponse(selection, charsDataTable, allocatedContentIdCount, unallocatedContentIdCount);

                        SendViewResponse(selection, charsDataTable, allocatedContentIdCount, unallocatedContentIdCount);
                    }
                }
                else
                {
                    // no session or no data socket; ignore
                }
            }
            else
            {
                if (PendingSessions.TryGetValue(client.ClientAddress, out selection))
                {
                    // The first request is only needed to associate the data client to the account ID.  A response
                    // is not expected.
                    client.AccountId = args.Data.GetAccountId();
                    client.ActiveKey = GetActiveKey(client.AccountId, client.ClientAddress);
                    selection.Data = client;
                }
                else
                {
                    // no one is there; ignore
                }
            }
        }

        private void DataServer_Received_Select(ClientSocket client, SocketEventArgs args)
        {
            bool allowed = false;
            Selection selection = null;

            // Check the account is still allowed, because the account could be logged in, and then become
            // disallowed.
            using (DataTable account_status = Database.Select(Statements.Select.ACCOUNT_STATUS, Columns.Select.ACCOUNT_STATUS, new Couple<object>(Columns.ID_COLUMN, client.AccountId)))
            {
                if (account_status != null && account_status.Rows.Count > 0)
                {
                    DataRow row = account_status.Rows[0];

                    allowed = IsNormal(row);
                }
            }

            if (allowed && ActiveSessions.TryGetValue(client.ActiveKey, out selection))
            {
                using (DataTable zone_settings_chars = Database.Select(Statements.Select.ZONE_SETTINGS_CHARS, Columns.Select.CHAR_ZONE_SETTINGS, new Couple<object>(Columns.CHARACTER_ID_COLUMN, selection.CharacterId)))
                {
                    if (zone_settings_chars != null && zone_settings_chars.Rows.Count > 0)
                    {
                        DataRow row = zone_settings_chars.Rows[0];

                        if (SelectionAllowed(row))
                        {
                            // Can be replaced with actual session token?  No.  Results in:
                            // [Warning] Client cannot receive packet or key is invalid: <player name>
                            byte[] sessionKeyBytes = new byte[20];

                            if (row.PreviousZone() == 0)
                            {
                                ushort zoneid = row.ZoneId();

                                if (!Database.Update(Statements.Update.CHARS_POS_PREVZONE, new Couple<object>(Columns.ZONE_ID, zoneid), new Couple<object>(Columns.CHARACTER_ID_COLUMN, selection.CharacterId)))
                                {
                                    Logger.Warning($"\"{Statements.Update.CHARS_POS_PREVZONE}\" failed.", MethodBase.GetCurrentMethod());
                                }

                                Array.Copy(args.Data, 1, sessionKeyBytes, 0, sessionKeyBytes.Length);
                                sessionKeyBytes[SESSION_KEY_SHIFT_OFFSET] += 4; // Why is the key being shifted?
                            }
                            else
                            {
                                Array.Copy(args.Data, 1, sessionKeyBytes, 0, sessionKeyBytes.Length);
                                sessionKeyBytes[SESSION_KEY_SHIFT_OFFSET] -= 2; // Why is the key being shifted?
                            }

                            using (DataTable accounts_sessions = Database.Select(Statements.Select.ACCOUNTS_SESSIONS, Columns.Select.ACCOUNTS_SESSIONS, new Couple<object>(Columns.ACCOUNT_ID_COLUMN, client.AccountId)))
                            {
                                // Previous sessions will have been purged at this point; confirm it.
                                if (accounts_sessions == null || accounts_sessions.Rows.Count == 0)
                                {
                                    // Ensure sockets are good before 'committing' session entry.  Possible fix for
                                    // stuck sessions.
                                    if (selection.View != null)
                                    {
                                        if (selection.Data != null)
                                        {
                                            if (LoginLimitOkay(client))
                                            {
                                                uint client_addr = client.ClientAddress;

                                                Couple<object> accidCouple = new Couple<object>(Columns.ACCOUNT_ID_COLUMN, client.AccountId);
                                                Couple<object> charidCouple = new Couple<object>(Columns.CHARACTER_ID_COLUMN, selection.CharacterId);

                                                Couple<object>[] parameters = new Couple<object>[]
                                                {
                                                    accidCouple,
                                                    charidCouple,
                                                    new Couple<object>("session_key", GetSessionKey(sessionKeyBytes).ToString()), // session_key.ToString()
                                                    new Couple<object>("server_addr", row.ZoneIpAsUInt32()), // BitConverter.ToUInt32(zoneAddressBytes, 0)
                                                    new Couple<object>("server_port", row.ZonePort()),
                                                    new Couple<object>(Columns.CLIENT_ADDRESS_COLUMN, client_addr),
                                                };

                                                if (Database.Insert(Statements.Insert.ACCOUNTS_SESSIONS, parameters))
                                                {
                                                    Couple<object>[] auditParameters = new Couple<object>[]
                                                    {
                                                        accidCouple,
                                                        charidCouple,
                                                        new Couple<object>(Columns.CLIENT_IP_COLUMN, client.IpAddressString),
                                                    };

                                                    if (Database.Insert(Statements.Update.CHAR_STATS_ZONING, charidCouple))
                                                    {
                                                        using (var viewResponse = new ViewResponse(ViewResponse.Codes.SELECTION))
                                                        {
                                                            viewResponse.Append(ViewResponse.Constants.SELECTION_HEADER);
                                                            viewResponse.Append(selection.CharacterName, MAXIMUM_CHARACTER_NAME_LENGTH);
                                                            viewResponse.Append(ViewResponse.Constants.IP_PORT_COMBO_COUNT); // IP + port combo count?

                                                            // If connecting with loopback, and zone settings are an external IP, zone-in will
                                                            // work, but zoning will not.  The map server tells the client to use the external
                                                            // address, which causes the traffic to mismatch the sessions table.

                                                            viewResponse.Append(client.HostAddressBytes);
                                                            viewResponse.Append(row.ZonePortAsUInt32());
                                                            viewResponse.Append(client.HostAddressBytes); // real search server IP; TODO make configurable
                                                            viewResponse.Append(SearchPortBytes);

                                                            selection.View.Send(viewResponse.GetBytes());
                                                        }

                                                        selection.View.Disconnect();
                                                    }
                                                    else
                                                    {
                                                        //if (!Database.Delete(Statements.Delete.ACCOUNTS_SESSIONS, accidCouple))
                                                        //{
                                                        //    Logger.Error(new ApplicationException($"Unable to undo accounts_sessions creation for accid {selection.AccountId}, charid {selection.CharacterId}, name {selection.CharacterName}"), MethodBase.GetCurrentMethod());
                                                        //}
                                                        selection.View.Send(ViewErrorResponse(ViewResponse.Errors.REGISTRATION_2_FAILED));
                                                    }
                                                }
                                                else
                                                {
                                                    // could not insert accounts_sessions
                                                    selection.View.Send(ViewErrorResponse(ViewResponse.Errors.REGISTRATION_2_FAILED));
                                                }
                                            }
                                            else
                                            {
                                                // login limit failed
                                                selection.View.Send(ViewErrorResponse(ViewResponse.Errors.CHARACTER_PARAMS_INCORRECT));
                                            }
                                        }
                                        else
                                        {
                                            // no data socket (automatic clean-up)
                                            Logger.Error(new ApplicationException($"Data socket was null before session commit for accid {selection.AccountId}, charid {selection.CharacterId}, name {selection.CharacterName}"), MethodBase.GetCurrentMethod());
                                        }
                                    }
                                    else
                                    {
                                        // no view socket (automatic clean-up)
                                        Logger.Error(new ApplicationException($"View socket was null before session commit for accid {selection.AccountId}, charid {selection.CharacterId}, name {selection.CharacterName}"), MethodBase.GetCurrentMethod());
                                    }
                                }
                                else
                                {
                                    // session already exists
                                    selection.View.Send(ViewErrorResponse(ViewResponse.Errors.CHARACTER_ALREADY_LOGGED_IN));
                                }
                            }
                        }
                        else
                        {
                            // not a GM character during maintenance
                            selection.View.Send(ViewErrorResponse(ViewResponse.Errors.WORLD_SERVER_MAINTENANCE));
                        }
                    }
                    else
                    {
                        // invalid zone?
                        selection.View.Send(ViewErrorResponse(ViewResponse.Errors.REGISTRATION_1_FAILED));
                    }
                }
            }
            else
            {
                // no session or was banned/suspended while playing; ignore
            }

            if (LOG_USER_IP && selection != null)
            {
                // TODO truncation logic
                InsertAccountIpRecord(client, selection);
            }
        }

        #region "Data Events"
        private void DataServer_Connecting(ServerSocket sender, ClientSocket client, SocketEventArgs args)
        {
            Logger.Information(BuildLogMessage(client, args), MethodBase.GetCurrentMethod());
        }

        private void DataServer_Connected(ServerSocket sender, ClientSocket client, SocketEventArgs args)
        {
            Logger.Information(BuildLogMessage(client, args), MethodBase.GetCurrentMethod());

            client.Send(DataResponse.Constants.WHO_ARE_YOU);
        }

        private void DataServer_Sent(ServerSocket sender, ClientSocket client, SocketEventArgs args)
        {
            Logger.Information(BuildLogMessage(client, args), MethodBase.GetCurrentMethod());
        }

        private void DataServer_Received(ServerSocket sender, ClientSocket client, SocketEventArgs args)
        {
            // Could test length, but the client buffer is small.
            byte dataRequest = args.Data[0x00];

            Logger.Information(BuildLogMessage(client, args, dataRequest), MethodBase.GetCurrentMethod());

            // Despite the caller being able to catch IndexOutOfRangeException and NullReferenceException,
            // it is less expensive to validate in the event that an attacker uses known thrown exceptions
            // to degrade performance or perform denial of service.
            if (dataRequest < DataRequests.Length)
            {
                var action = DataRequests[dataRequest];

                if (action != null)
                {
                    action.Invoke(client, args);
                }
                else
                {
                    // Request index not set; ignore.
                    Logger.Warning("Data request index not set.", MethodBase.GetCurrentMethod());
                }
            }
            else
            {
                // Unsupported request; ignore.
                Logger.Warning("Unsupported data request index.", MethodBase.GetCurrentMethod());
            }
        }

        private void DataServer_Disconnected(ServerSocket sender, ClientSocket client, SocketEventArgs args)
        {
            Logger.Information(BuildLogMessage(client, args), MethodBase.GetCurrentMethod());

            if (ActiveSessions.TryGetValue(client.ActiveKey, out Selection selection))
            {
                selection.Data = null;

                if (selection.View == null)
                {
                    if (ActiveSessions.TryRemove(client.ActiveKey, out selection))
                    {
                        selection.Closed = DateTime.UtcNow.ToLocalTime();

                        ClosedSessions.Enqueue(selection);
                    }
                    else
                    {
                        Logger.Error(new ApplicationException($"Data socket for accid {client.AccountId} disconnected, but the active session could not be removed."), MethodBase.GetCurrentMethod());
                    }

                    if (ViewSessionIds.TryGetValue(client.ClientAddress, out List<uint> accountIds))
                    {
                        if (accountIds.Contains(client.AccountId))
                        {
                            accountIds.Remove(client.AccountId);
                        }

                        if (accountIds.Count <= 0)
                        {
                            ViewSessionIds.TryRemove(client.ClientAddress, out _);
                        }
                    }
                }
                else
                {
                    Logger.Warning($"Data socket for accid {client.AccountId} disconnected, but the View socket is still connected.", MethodBase.GetCurrentMethod());
                }
            }
            else
            {
                if (client.AccountId == 0)
                {
                    Logger.Warning($"Data socket for accid {client.AccountId} disconnected.  The connection was interrupted before an accid could be assigned.", MethodBase.GetCurrentMethod());
                }
                else
                {
                    Logger.Warning($"Data socket for accid {client.AccountId} disconnected, but the accid has already been removed.", MethodBase.GetCurrentMethod());
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
                    //-TODO: dispose managed state (managed objects).
                    Logger.Information("Stopping...", MethodBase.GetCurrentMethod());

                    CleanUpSessionsTimer.Dispose();

                    if (ClearSessionsOnDispose)
                    {
                        Database.Delete(Statements.Delete.ACCOUNTS_SESSIONS_ALL);
                    }
                    
                    PendingSessions.Clear();
                    ActiveSessions.Clear();
                    ViewSessionIds.Clear();
                    while (ClosedSessions.TryDequeue(out Selection _)) { }

                    // Destruction order should be from first instantiated to last to ensure everything is tore down properly.
                    if (AuthenticationServer != null)
                    {
                        AuthenticationServer.Disconnected -= AuthenticationServer_Disconnected;
                        AuthenticationServer.Sent -= AuthenticationServer_Sent;
                        AuthenticationServer.Authenticating -= AuthenticationServer_Authenticating;
                        AuthenticationServer.Connected -= AuthenticationServer_Connected;
                        AuthenticationServer.Connecting -= AuthenticationServer_Connecting;
                        AuthenticationServer.Dispose();
                    }

                    if (ViewServer != null)
                    {
                        ViewServer.Disconnected -= ViewServer_Disconnected;
                        ViewServer.Sent -= ViewServer_Sent;
                        ViewServer.Received -= ViewServer_Received;
                        ViewServer.Connected -= ViewServer_Connected;
                        ViewServer.Connecting -= ViewServer_Connecting;
                        ViewServer.Dispose();
                    }

                    if (DataServer != null)
                    {
                        DataServer.Disconnected -= DataServer_Disconnected;
                        DataServer.Sent -= DataServer_Sent;
                        DataServer.Received -= DataServer_Received;
                        DataServer.Connected -= DataServer_Connected;
                        DataServer.Connecting -= DataServer_Connecting;
                        DataServer.Dispose();
                    }

                    Database?.Dispose();

                    Logger.Information("Stopped.", MethodBase.GetCurrentMethod());
                    Logger.Close();
                }

                //-TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                //-TODO: set large fields to null.

                disposedValue = true;
            }
        }

        //-TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~LoginServer()
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
