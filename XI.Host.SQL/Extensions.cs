using System;

namespace XI.Host.SQL
{
    public static class Extensions
    {
        public static string ToMySQL(this DateTime datetime)
        {
            return datetime.ToString("yyyy-MM-dd hh:mm:ss");
        }
    }
}
