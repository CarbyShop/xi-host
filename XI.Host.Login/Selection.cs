using XI.Host.Common;
using XI.Host.Sockets;
using System;

namespace XI.Host.Login
{
    internal class Selection
    {
        public uint AccountId { get; private set; }
        public uint CharacterId { get; set; }
        public string CharacterName { get; set; }
        public ClientSocket View { get; set; }
        public ClientSocket Data { get; set; }
        public DateTime Authenticated { get; private set; }
        public DateTime Closed { get; set; }

        [Custom("Useful for some custom server implementations.")]
        public string MAC { get; set; }

        public Selection(uint accid, DateTime authenticated)
        {
            AccountId = accid;
            Authenticated = authenticated;
            Closed = DateTime.MinValue.ToLocalTime();
        }

        public Selection(uint accid, DateTime authenticated, string mac) : this(accid, authenticated)
        {
            MAC = mac;
        }
    }
}
