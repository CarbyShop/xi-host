using XI.Host.Common;
using System;

namespace XI.Host.Login
{
    public static class Columns
    {
        public const string ID_COLUMN = "id";
        public const string ACCOUNT_ID_COLUMN = "accid";
        public const string CHARACTER_ID_COLUMN = "charid";
        public const string CHARACTER_NAME_COLUMN = "charname";
        public const string LOGIN_COLUMN = "login";
        public const string PASSWORD_COLUMN = "password";
        public const string CLIENT_IP_COLUMN = "client_ip";
        public const string CLIENT_ADDRESS_COLUMN = "client_addr";
        public const string TIME_LAST_MODIFY_COLUMN = "timelastmodify";
        public const string ZONE_ID = "zoneid";

        public static readonly Type BOOLEAN_TYPE = typeof(bool);
        public static readonly Type BYTE_TYPE = typeof(byte);
        public static readonly Type USHORT_TYPE = typeof(ushort);
        public static readonly Type INT_TYPE = typeof(int);
        public static readonly Type UINT_TYPE = typeof(uint);
        public static readonly Type ULONG_TYPE = typeof(ulong);
        public static readonly Type STRING_TYPE = typeof(string);

        public static class Select
        {
            public static readonly Couple<Type>[] AUTHENTICATE = new Couple<Type>[]
            {
                new Couple<Type>("id", UINT_TYPE),
                new Couple<Type>("login", STRING_TYPE),
                new Couple<Type>("password", STRING_TYPE),
                new Couple<Type>("status", BYTE_TYPE),
            };

            public static readonly Couple<Type>[] GMLEVEL = new Couple<Type>[]
            {
                new Couple<Type>("gmlevel", USHORT_TYPE),
            };

            public static readonly Couple<Type>[] ACCOUNTS_SESSIONS = new Couple<Type>[]
            {
                new Couple<Type>(ACCOUNT_ID_COLUMN, UINT_TYPE),
            };

            public static readonly Couple<Type>[] ACCOUNTS_SESSIONS_IP = new Couple<Type>[]
            {
                new Couple<Type>("client_addr", UINT_TYPE),
            };

            public static readonly Couple<Type>[] ACCOUNTS_LOGIN = new Couple<Type>[]
            {
                new Couple<Type>("id", UINT_TYPE),
            };

            public static readonly Couple<Type>[] ACCOUNT_MAX_ID = new Couple<Type>[]
            {
                new Couple<Type>("max", UINT_TYPE)
            };

            public static readonly Couple<Type>[] ACCOUNT_STATUS = new Couple<Type>[]
            {
                new Couple<Type>("status", UINT_TYPE),
            };

            //public static readonly Couple<Type>[] MAX_CHARID = new Couple<Type>[]
            //{
            //    new Couple<Type>("max", UINT_TYPE),
            //};

            public static readonly Couple<Type>[] CHARID = new Couple<Type>[]
            {
                new Couple<Type>(CHARACTER_ID_COLUMN, UINT_TYPE),
            };

            public static readonly Couple<Type>[] CHARNAME = new Couple<Type>[]
            {
                new Couple<Type>("charname", STRING_TYPE),
            };

            public static readonly Couple<Type>[] EXPANSIONS_FEATURES = new Couple<Type>[]
            {
                new Couple<Type>("expansions", UINT_TYPE),
                new Couple<Type>("features", UINT_TYPE),
            };

            public static readonly Couple<Type>[] CONTENT_IDS = new Couple<Type>[]
            {
                new Couple<Type>("content_ids", UINT_TYPE),
            };

            public static readonly Couple<Type>[] CHARS = new Couple<Type>[]
            {
                new Couple<Type>("gmlevel", USHORT_TYPE),
                new Couple<Type>("war", BYTE_TYPE),
                new Couple<Type>("mnk", BYTE_TYPE),
                new Couple<Type>("whm", BYTE_TYPE),
                new Couple<Type>("blm", BYTE_TYPE),
                new Couple<Type>("rdm", BYTE_TYPE),
                new Couple<Type>("thf", BYTE_TYPE),
                new Couple<Type>("pld", BYTE_TYPE),
                new Couple<Type>("drk", BYTE_TYPE),
                new Couple<Type>("bst", BYTE_TYPE),
                new Couple<Type>("brd", BYTE_TYPE),
                new Couple<Type>("rng", BYTE_TYPE),
                new Couple<Type>("sam", BYTE_TYPE),
                new Couple<Type>("nin", BYTE_TYPE),
                new Couple<Type>("drg", BYTE_TYPE),
                new Couple<Type>("smn", BYTE_TYPE),
                new Couple<Type>("blu", BYTE_TYPE),
                new Couple<Type>("cor", BYTE_TYPE),
                new Couple<Type>("pup", BYTE_TYPE),
                new Couple<Type>("dnc", BYTE_TYPE),
                new Couple<Type>("sch", BYTE_TYPE),
                new Couple<Type>("geo", BYTE_TYPE),
                new Couple<Type>("run", BYTE_TYPE),
                new Couple<Type>(CHARACTER_ID_COLUMN, UINT_TYPE),
                new Couple<Type>("charname", STRING_TYPE),
                new Couple<Type>("pos_zone", BYTE_TYPE),
                //new Couple<Type>("pos_prevzone", typeof(ushort)),
                new Couple<Type>("race", USHORT_TYPE),
                new Couple<Type>("mjob", USHORT_TYPE),
                new Couple<Type>("face", BYTE_TYPE),
                new Couple<Type>("size", BYTE_TYPE),
                new Couple<Type>("head", USHORT_TYPE),
                new Couple<Type>("body", USHORT_TYPE),
                new Couple<Type>("hands", USHORT_TYPE),
                new Couple<Type>("legs", USHORT_TYPE),
                new Couple<Type>("feet", USHORT_TYPE),
                new Couple<Type>("main", USHORT_TYPE),
                new Couple<Type>("sub", USHORT_TYPE),
            };

            public static readonly Couple<Type>[] CHAR_ZONE_SETTINGS = new Couple<Type>[]
            {
                new Couple<Type>("pos_prevzone", USHORT_TYPE),
                new Couple<Type>("gmlevel", USHORT_TYPE),
                new Couple<Type>(ACCOUNT_ID_COLUMN, UINT_TYPE),
                new Couple<Type>(ZONE_ID, USHORT_TYPE),
                new Couple<Type>(CHARACTER_ID_COLUMN, UINT_TYPE),
                new Couple<Type>("zoneip", STRING_TYPE),
                new Couple<Type>("zoneport", USHORT_TYPE),
            };

            public static readonly Couple<Type>[] SERVER_CODES = new Couple<Type>[]
            {
                new Couple<Type>("code", STRING_TYPE),
                new Couple<Type>(ACCOUNT_ID_COLUMN, UINT_TYPE),
                new Couple<Type>("expiry", UINT_TYPE),
            };

            public static readonly Couple<Type>[] SESSION_COUNT = new Couple<Type>[]
            {
                new Couple<Type>("count", INT_TYPE),
            };

            public static readonly Couple<Type>[] EXCEPTION_TIME = new Couple<Type>[]
            {
                new Couple<Type>("exception", ULONG_TYPE),
            };
        }
    }
}
