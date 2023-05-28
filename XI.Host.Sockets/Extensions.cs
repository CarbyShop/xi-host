using System;
using System.Net;
using System.Net.Sockets;

namespace XI.Host.Sockets
{
    public static class Extensions
    {
        public static bool IsLoopback(this IPAddress ipAddress)
        {
            return ipAddress.Equals(IPAddress.Loopback);
        }

        public static bool IsAny(this IPAddress ipAddress)
        {
            return ipAddress.Equals(IPAddress.Any);
        }

        /// <summary>
        /// Gets the host machine's IP bytes to which the client socket was able to establish a connection.
        /// </summary>
        /// <param name="client"></param>
        /// <returns>A byte array representation server's IP.</returns>
        /// <remarks>Supports consistent IP identification from a remote or locally established connection (for example, from loopback or localhost, however it gets interpreted).</remarks>
        public static byte[] GetHostAddressBytes(this Socket client)
        {
            return (client.LocalEndPoint as IPEndPoint).Address.GetAddressBytes();
        }

        public static uint ToUInt(this IPAddress ipAddress)
        {
            return BitConverter.ToUInt32(ipAddress.GetAddressBytes());
        }

        /// <summary>
        /// Converts the client IP address into what C++ stores it as in the DB.
        /// </summary>
        /// <param name="ipAddress">An IP address.</param>
        /// <returns>A uint representing the reversed binary form of an IP address.</returns>
        public static uint ToCPP(this IPAddress ipAddress)
        {
            byte[] array = ipAddress.GetAddressBytes();

            Array.Reverse(array);

            return BitConverter.ToUInt32(array, 0);
        }
    }
}
