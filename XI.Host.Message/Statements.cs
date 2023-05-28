using System.Text;

namespace XI.Host.Message
{
    public static class Statements
    {
        public static class Select
        {
            public static readonly byte[] SERVER_ADDRESS_PORT_BY_CHARID = Encoding.UTF8.GetBytes("SELECT server_addr, server_port FROM accounts_sessions WHERE charid = @charid");
            public static readonly byte[] SERVER_ADDRESS_PORT_BY_CHARNAME = Encoding.UTF8.GetBytes("SELECT server_addr, server_port FROM accounts_sessions LEFT JOIN chars ON accounts_sessions.charid = chars.charid WHERE charname = @charname");
            public static readonly byte[] SERVER_ADDRESS_PORT_BY_PARTYID = Encoding.UTF8.GetBytes("SELECT server_addr, server_port, MIN(charid) as mincharid FROM accounts_sessions JOIN accounts_parties USING (charid) WHERE IF (allianceid <> 0, allianceid = (SELECT MAX(allianceid) FROM accounts_parties WHERE partyid = @partyid), partyid = @partyid) GROUP BY server_addr, server_port");
            public static readonly byte[] SERVER_ADDRESS_PORT_BY_LINKSHELL = Encoding.UTF8.GetBytes("SELECT server_addr, server_port FROM accounts_sessions WHERE linkshellid1 = @lsid OR linkshellid2 = @lsid GROUP BY server_addr, server_port");
            public static readonly byte[] ZONE_ADDRESS_PORT_BY_MISC = Encoding.UTF8.GetBytes("SELECT zoneip, zoneport FROM zone_settings WHERE misc & 1024 GROUP BY zoneip, zoneport");
            public static readonly byte[] ZONE_ADDRESS_PORT_BY_ID = Encoding.UTF8.GetBytes("SELECT zoneip, zoneport FROM zone_settings WHERE zoneid = @zoneid");
        }
    }
}
