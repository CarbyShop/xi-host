using System;
using System.Reflection;
using System.Text;

namespace XI.Host.Common
{
    public static class Utilities
    {
        private static readonly char UNTIL = '\0';

        public static DateTime FromUnixTime(in uint stamp)
        {
            return DateTime.UnixEpoch.AddSeconds(stamp).ToLocalTime();
        }

        public static string TryReadUntil(in byte[] data, in int start)
        {
            return TryReadUntil(data, start, data.Length - start, UNTIL);
        }

        public static string TryReadUntil(in byte[] data, in int start, in int length)
        {
            return TryReadUntil(data, start, length, UNTIL);
        }

        public static string TryReadUntil(in byte[] data, in int start, in int length, in char orUntil)
        {
            return TryReadUntil(data, start, length, orUntil, 0x21, 0x7E); // Range of valid printable characters.
        }

        public static string TryReadUntil(in byte[] data, in int start, in int length, in char orUntil, in byte minByte, in byte maxByte)
        {
            StringBuilder result = new StringBuilder();

            try
            {
                char value;
                int stop = start + length;

                for (int i = start; i < stop; i++)
                {
                    value = Convert.ToChar(data[i]);

                    if (value == orUntil)
                    {
                        break;
                    }

                    if (data[i] >= minByte && data[i] <= maxByte)
                    {
                        result.Append(value);
                    }
                    else
                    {
                        result.Clear();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, MethodBase.GetCurrentMethod());
            }

            return result.ToString();
        }
    }
}
