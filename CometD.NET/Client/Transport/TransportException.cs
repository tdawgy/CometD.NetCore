using System;

namespace CometD.NetCore.Client.Transport
{
    [Serializable]
    public class TransportException : Exception
    {
        public TransportException()
        {
        }

        public TransportException(string message)
            : base(message)
        {
        }

        public TransportException(string message, Exception cause)
            : base(message, cause)
        {
        }
    }
}