using System;
using System.Data;
using System.Net;

namespace XI.Host.Message
{
    public static class Extensions
    {
        public static uint ServerAddress(this DataRow dataRow)
        {
            return Convert.ToUInt32(dataRow["server_addr"]);
        }

        public static ushort ServerPort(this DataRow dataRow)
        {
            return Convert.ToUInt16(dataRow["server_port"]);
        }

        public static uint MinimumCharacterId(this DataRow dataRow)
        {
            return Convert.ToUInt32(dataRow["mincharid"]);
        }

        public static IPAddress ZoneIpAddress(this DataRow dataRow)
        {
            return IPAddress.Parse(Convert.ToString(dataRow["zoneip"]));
        }

        // Has duplicates.
        public static ushort ZonePort(this DataRow dataRow)
        {
            return Convert.ToUInt16(dataRow["zoneport"]);
        }
    }
}
