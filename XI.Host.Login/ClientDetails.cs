using System;
using XI.Host.Common;

namespace XI.Host.Login
{
    internal class ClientDetails
    {
        public byte Magic { get; private set; }
        public ulong Flags { get; private set; }
        //public byte[] Reserved { get; private set; } // 17 bytes
        public Version Version { get; private set; }
        public CredentialContainer Credentials { get; set; }

        [Custom("Useful for some custom server implementations.")]
        public string MAC { get; set; }

        public ClientDetails(byte magic, ulong flags, Version version)
        {
            Magic = magic;
            Flags = flags;
            Version = version;
        }

        public ClientDetails(byte magic, ulong flags, Version version, string mac)
        {
            Magic = magic;
            Flags = flags;
            Version = version;
            MAC = mac;
        }
    }
}
