using System;

namespace CometD.NetCore.Client.Transport
{
    [Serializable]
    public class TransportException : Exception
    {
        public TransportException()
        {
        }

        public TransportException(String message)
            : base(message)
        {
        }

        public TransportException(String message, Exception cause)
            : base(message, cause)
        {
        }
    }
}