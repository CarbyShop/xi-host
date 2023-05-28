using XI.Host.Common;
using System;

namespace XI.Host.Message
{
    public static class Columns
    {
        public const string CHARACTER_ID_COLUMN = "charid";

        public static readonly Type BOOLEAN_TYPE = typeof(bool);
        public static readonly Type BYTE_TYPE = typeof(byte);
        public static readonly Type USHORT_TYPE = typeof(ushort);
        public static readonly Type UINT_TYPE = typeof(uint);
        public static readonly Type STRING_TYPE = typeof(string);

        public static class Select
        {
            public static readonly Couple<Type>[] SERVER_ADDRESS_PORT = new Couple<Type>[] {
                new Couple<Type>("server_addr", UINT_TYPE),
                new Couple<Type>("server_port", USHORT_TYPE),
            };

            public static readonly Couple<Type>[] SERVER_ADDRESS_PORT_BY_PARTYID = new Couple<Type>[] {
                new Couple<Type>("server_addr", UINT_TYPE),
                new Couple<Type>("server_port", USHORT_TYPE),
                new Couple<Type>("mincharid", UINT_TYPE),
            };

            public static readonly Couple<Type>[] ZONE_ADDRESS_PORT = new Couple<Type>[] {
                new Couple<Type>("zoneip", STRING_TYPE),
                new Couple<Type>("zoneport", USHORT_TYPE),
            };
        }
    }
}
