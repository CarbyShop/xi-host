using XI.Host.Common;
using System.Text;

namespace XI.Host.Login
{
    public static class Statements
    {
        public static class Select
        {
            public static readonly byte[] BANNED = Encoding.UTF8.GetBytes("SELECT address, mask FROM ip_bans"); // WHERE address = @address
            public static readonly byte[] AUTHENTICATE = Encoding.UTF8.GetBytes("SELECT id, login, password, status FROM accounts WHERE login = @login AND password = PASSWORD(@password)");
            public static readonly byte[] GMLEVEL = Encoding.UTF8.GetBytes("SELECT max(gmlevel) AS max FROM chars WHERE accid = @accid");
            public static readonly byte[] ACCOUNTS_SESSIONS = Encoding.UTF8.GetBytes("SELECT accid FROM accounts_sessions WHERE accid = @accid");
            public static readonly byte[] ACCOUNTS_SESSIONS_ALL = Encoding.UTF8.GetBytes("SELECT accid FROM accounts_sessions");
            public static readonly byte[] ACCOUNTS_SESSIONS_IP = Encoding.UTF8.GetBytes("SELECT client_addr FROM accounts_sessions WHERE client_addr = @client_addr");
            public static readonly byte[] ACCOUNTS_LOGIN = Encoding.UTF8.GetBytes("SELECT id FROM accounts WHERE login = @login");
            public static readonly byte[] ACCOUNTS_MAX_ID = Encoding.UTF8.GetBytes("SELECT max(id) FROM accounts");
            public static readonly byte[] ACCOUNT_STATUS = Encoding.UTF8.GetBytes("SELECT status FROM accounts WHERE id = @id");
            public static readonly byte[] CHARS_GM = Encoding.UTF8.GetBytes("SELECT charid FROM chars WHERE accid = @accid AND gmlevel > 0");
            public static readonly byte[] FREE_CHARID = Encoding.UTF8.GetBytes("SELECT charid FROM chars ORDER BY charid ASC");
            public static readonly byte[] CHARNAME = Encoding.UTF8.GetBytes("SELECT charname FROM chars WHERE charname = @charname");
            public static readonly byte[] EXPANSIONS_FEATURES = Encoding.UTF8.GetBytes("SELECT expansions, features FROM accounts WHERE id = @accid");
            public static readonly byte[] CONTENT_IDS = Encoding.UTF8.GetBytes("SELECT content_ids FROM accounts WHERE id = @accid");
            public static readonly byte[] CHARS = Encoding.UTF8.GetBytes("SELECT gmlevel, war, mnk, whm, blm, rdm, thf, pld, drk, bst, brd, rng, sam, nin, drg, smn, blu, cor, pup, dnc, sch, geo, run, charid, charname, pos_zone, race, mjob, face, size, head, body, hands, legs, feet, main, sub FROM chars INNER JOIN char_stats USING(charid) INNER JOIN char_look USING(charid) INNER JOIN char_jobs USING(charid) WHERE accid = @accid ORDER BY charid ASC"); // Removed columns: pos_prevzone, dnc, sch, geo, run, Removed limit: LIMIT 0, @limit
            public static readonly byte[] ZONE_SETTINGS_CHARS = Encoding.UTF8.GetBytes("SELECT pos_prevzone, gmlevel, accid, zoneid, charid, zoneip, zoneport FROM zone_settings, chars WHERE IF(pos_zone = 0, zoneid = pos_prevzone, zoneid = pos_zone) AND charid = @charid");
            public static readonly byte[] SERVER_CODES = Encoding.UTF8.GetBytes("SELECT code, accid, expiry FROM server_codes WHERE code = @code");

            public static readonly byte[] SESSION_COUNT = Encoding.UTF8.GetBytes("SELECT COUNT(client_addr) FROM accounts_sessions WHERE client_addr = @client_addr");
            public static readonly byte[] EXCEPTION_TIME = Encoding.UTF8.GetBytes("SELECT UNIX_TIMESTAMP(exception) FROM ip_exceptions WHERE accid = @accid");
        }

        public static class Insert
        {
            public static readonly byte[] CHARS = Encoding.UTF8.GetBytes("INSERT INTO chars (charid, accid, charname, pos_zone, nation) VALUES (@charid, @accid, @charname, @pos_zone, @nation)");
            public static readonly byte[] CHAR_LOOK = Encoding.UTF8.GetBytes("INSERT INTO char_look (charid, face, race, size) VALUES (@charid, @face, @race, @size)");
            public static readonly byte[] CHAR_STATS = Encoding.UTF8.GetBytes("INSERT INTO char_stats (charid, mjob) VALUES (@charid, @mjob)");
            public static readonly byte[] CHAR_EXP = Encoding.UTF8.GetBytes("INSERT INTO char_exp (charid) VALUES (@charid) ON DUPLICATE KEY UPDATE charid = charid");
            public static readonly byte[] CHAR_JOBS = Encoding.UTF8.GetBytes("INSERT INTO char_jobs (charid) VALUES (@charid) ON DUPLICATE KEY UPDATE charid = charid");
            public static readonly byte[] CHAR_PET = Encoding.UTF8.GetBytes("INSERT INTO char_pet (charid) VALUES (@charid) ON DUPLICATE KEY UPDATE charid = charid");
            public static readonly byte[] CHAR_POINTS = Encoding.UTF8.GetBytes("INSERT INTO char_points (charid) VALUES (@charid) ON DUPLICATE KEY UPDATE charid = charid");
            public static readonly byte[] CHAR_UNLOCKS = Encoding.UTF8.GetBytes("INSERT INTO char_unlocks (charid) VALUES (@charid) ON DUPLICATE KEY UPDATE charid = charid");
            public static readonly byte[] CHAR_PROFILE = Encoding.UTF8.GetBytes("INSERT INTO char_profile (charid) VALUES (@charid) ON DUPLICATE KEY UPDATE charid = charid");
            public static readonly byte[] CHAR_STORAGE = Encoding.UTF8.GetBytes("INSERT INTO char_storage (charid) VALUES (@charid) ON DUPLICATE KEY UPDATE charid = charid");
            public static readonly byte[] CHAR_INVENTORY = Encoding.UTF8.GetBytes("INSERT INTO char_inventory (charid) VALUES (@charid)");
            public static readonly byte[] CHAR_CUTSCENE = Encoding.UTF8.GetBytes("INSERT INTO char_vars (charid, varname, value) VALUES (@charid, @varname, @value)");
            public static readonly byte[] ACCOUNTS_SESSIONS = Encoding.UTF8.GetBytes("INSERT INTO accounts_sessions (accid, charid, session_key, server_addr, server_port, client_addr) VALUES (@accid, @charid, UNHEX(@session_key), @server_addr, @server_port, @client_addr)");
            public static readonly byte[] ACCOUNTS_CREATE = Encoding.UTF8.GetBytes("INSERT INTO accounts (id, login, password, timecreate, timelastmodify, status, priv) VALUES (@id, @login, PASSWORD(@password), @timecreate, @timelastmodify, 1, 1)");
            public static readonly byte[] ACCOUNT_IP_RECORD = Encoding.UTF8.GetBytes("INSERT INTO account_ip_record (login_time, accid, charid, client_ip) VALUES(@login_time, @accid, @charid, @client_ip)");
        }

        public static class Update
        {
            //public static readonly byte[] ACCOUNT_ATTEMPTS = Encoding.UTF8.GetBytes("UPDATE accounts SET timelastmodify = NULL, attempts = 0 WHERE id = @accid");
            public static readonly byte[] ACCOUNT_MODIFY = Encoding.UTF8.GetBytes("UPDATE accounts SET timelastmodify = @timelastmodify WHERE id = @accid");
            public static readonly byte[] ACCOUNT_PASSWORD = Encoding.UTF8.GetBytes("UPDATE accounts SET password = PASSWORD(@password) WHERE id = @accid");
            public static readonly byte[] CHARS_SOFT_DELETE = Encoding.UTF8.GetBytes("UPDATE chars SET deleted = @deleted WHERE accid = @accid AND charid = @charid");
            public static readonly byte[] CHARS_POS_PREVZONE = Encoding.UTF8.GetBytes("UPDATE chars SET pos_prevzone = @zoneid WHERE charid = @charid");
            public static readonly byte[] CHAR_STATS_ZONING = Encoding.UTF8.GetBytes("UPDATE char_stats SET zoning = 2 WHERE charid = @charid");
        }

        public static class Delete
        {
            public static readonly byte[] CHAR_INVENTORY = Encoding.UTF8.GetBytes("DELETE FROM char_inventory WHERE charid = @charid");
            public static readonly byte[] ACCOUNTS_SESSIONS = Encoding.UTF8.GetBytes("DELETE FROM accounts_sessions WHERE accid = @accid");
            public static readonly byte[] ACCOUNTS_SESSIONS_ALL = Encoding.UTF8.GetBytes("DELETE FROM accounts_sessions WHERE accid >= 0");
        }

        public static class Replace
        {
            public static readonly byte[] REPLACE_IP_OWNERS = Encoding.UTF8.GetBytes("REPLACE INTO ip_owners VALUES (@client_ip, @accid)");
        }
    }
}
