using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace XI.Host.Sockets
{
    public static class Shared
    {
        //private static readonly NetworkInterface[] allNetworkInterfaces = NetworkInterface.GetAllNetworkInterfaces();

        //private static bool Valid(IPAddress ipAddress)
        //{
        //    foreach (var networkInterface in allNetworkInterfaces)
        //    {
        //        if (networkInterface.OperationalStatus == OperationalStatus.Up && (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Ethernet || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
        //        {
        //            foreach (var gatewayIPAddressInformation in networkInterface.GetIPProperties().GatewayAddresses)
        //            {
        //                if (gatewayIPAddressInformation.Address.Equals(ipAddress))
        //                {
        //                    return true;
        //                }
        //            }
        //        }
        //    }

        //    return false;
        //}

        public static IPAddress Resolve(string address)
        {
            if (!IPAddress.TryParse(address, out IPAddress result))
            {
                IPHostEntry hostEntries = Dns.GetHostEntry(address);
                IPAddress ipAddress;

                for (int i = 0; i < hostEntries.AddressList.Length; i++)
                {
                    ipAddress = hostEntries.AddressList[i];

                    if (ipAddress.AddressFamily == AddressFamily.InterNetwork) // && Valid(ipAddress))
                    {
                        result = ipAddress;
                        break;
                    }
                }
            }

            return result;
        }
    }
}