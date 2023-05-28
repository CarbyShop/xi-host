using System;
using System.IO;
using System.Text;

namespace XI.Host.Common
{
    public static class Extensions
    {
        public static ReadOnlySpan<char> ToLog(this Exception ex)
        {           
            var stringBuilder = new StringBuilder();

            stringBuilder.Append(ex.Message);
            stringBuilder.Append(Environment.NewLine);
            stringBuilder.Append(ex.StackTrace);

            return stringBuilder.ToString().ToCharArray();
        }

        public static void Write(this FileStream fileStream, in StringBuilder stringBuilder)
        {
            for (int i = 0; i < stringBuilder.Length; i++)
            {
                fileStream.WriteByte((byte)stringBuilder[i]);
            }
        }

        /// <summary>
        /// Defaults the write operation offset to 0 and count to the length of the buffer.
        /// </summary>
        /// <param name="ms">Target MemoryStream.</param>
        /// <param name="buffer">All bytes to be written.</param>
        public static void Write(this MemoryStream ms, in byte[] buffer)
        {
            ms.Write(buffer, 0, buffer.Length);
        }

        public static ushort NextInclusive(this Random random, in ushort minValue, in ushort maxValue)
        {
            return Convert.ToUInt16(random.Next(minValue, maxValue + 1));
        }
    }
}
