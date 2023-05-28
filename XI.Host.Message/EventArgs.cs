using System;
using ZeroMQ;

namespace XI.Host.Message
{
    public class MessageEventArgs : EventArgs
    {
        public ZMessage Message { get; private set; }
        
        public MessageEventArgs(in ZMessage message) : base()
        {
            Message = message;
        }
    }
}
