using System;
using System.Data;
using System.Net;
using System.Runtime.CompilerServices;

namespace XI.Host.Login
{
    public static class Extensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint GetCharacterId(this byte[] data)
        {
            return BitConverter.ToUInt32(data, 28);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint GetAccountId(this byte[] data)
        {
            return BitConverter.ToUInt32(data, 1);
        }

        public static string CharacterName(this DataRow dataRow)
        {
            return Convert.ToString(dataRow["charname"]);
        }

        public static ushort MainJob(this DataRow dataRow)
        {
            return Convert.ToUInt16(dataRow["mjob"]);
        }

        public static uint CharacterId(this DataRow dataRow)
        {
            return Convert.ToUInt32(dataRow[Columns.CHARACTER_ID_COLUMN]);
        }

        public static ushort Race(this DataRow dataRow)
        {
            return Convert.ToUInt16(dataRow["race"]);
        }

        public static byte Face(this DataRow dataRow)
        {
            return Convert.ToByte(dataRow["face"]);
        }

        public static byte Size(this DataRow dataRow)
        {
            return Convert.ToByte(dataRow["size"]);
        }

        public static ushort Head(this DataRow dataRow)
        {
            return Convert.ToUInt16(dataRow["head"]);
        }

        public static ushort Body(this DataRow dataRow)
        {
            return Convert.ToUInt16(dataRow["body"]);
        }

        public static ushort Hands(this DataRow dataRow)
        {
            return Convert.ToUInt16(dataRow["hands"]);
        }

        public static ushort Legs(this DataRow dataRow)
        {
            return Convert.ToUInt16(dataRow["legs"]);
        }

        public static ushort Feet(this DataRow dataRow)
        {
            return Convert.ToUInt16(dataRow["feet"]);
        }

        public static ushort MainHand(this DataRow dataRow)
        {
            return Convert.ToUInt16(dataRow["main"]);
        }

        public static ushort OffHand(this DataRow dataRow)
        {
            return Convert.ToUInt16(dataRow["sub"]);
        }

        public static byte ZoneAsByte(this DataRow dataRow)
        {
            return Convert.ToByte(dataRow["pos_zone"]);
        }

        public static ushort ZoneAsUShort(this DataRow dataRow)
        {
            return Convert.ToUInt16(dataRow["pos_zone"]);
        }

        public static byte ContentIdCount(this DataRow dataRow)
        {
            return Convert.ToByte(dataRow["content_ids"]);
        }

        public static byte Status(this DataRow dataRow)
        {
            return Convert.ToByte(dataRow["status"]);
        }

        //public static uint AccountId(this DataRow dataRow)
        //{
        //    return Convert.ToUInt32(dataRow["accid"]);
        //}

        public static uint Id(this DataRow dataRow)
        {
            return Convert.ToUInt32(dataRow["id"]);
        }

        public static uint Max(this DataRow dataRow)
        {
            return Convert.ToUInt32(dataRow["max"]);
        }

        public static uint Expansions(this DataRow dataRow)
        {
            return Convert.ToUInt32(dataRow["expansions"]);
        }

        public static uint Features(this DataRow dataRow)
        {
            return Convert.ToUInt32(dataRow["features"]);
        }

        public static ushort PreviousZone(this DataRow dataRow)
        {
            return Convert.ToUInt16(dataRow["pos_prevzone"]);
        }

        public static ushort ZoneId(this DataRow dataRow)
        {
            return Convert.ToUInt16(dataRow[Columns.ZONE_ID]);
        }

        public static string ZoneIp(this DataRow dataRow)
        {
            return Convert.ToString(dataRow["zoneip"]);
        }

        public static uint ZoneIpAsUInt32(this DataRow dataRow)
        {
            return BitConverter.ToUInt32(IPAddress.Parse(dataRow.ZoneIp()).GetAddressBytes(), 0);
        }

        public static uint ZonePortAsUInt32(this DataRow dataRow)
        {
            return Convert.ToUInt32(dataRow["zoneport"]);
        }

        // Has duplicates.
        public static ushort ZonePort(this DataRow dataRow)
        {
            return Convert.ToUInt16(dataRow["zoneport"]);
        }

        public static ushort GameMasterLevel(this DataRow dataRow)
        {
            return Convert.ToUInt16(dataRow["gmlevel"]);
        }
    }
}
