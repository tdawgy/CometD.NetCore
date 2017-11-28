using CometD.NetCore.Bayeux;
using CometD.NetCore.Common;
using System.Collections.Generic;

namespace CometD.NetCore.Client.Transport
{
    public abstract class ClientTransport : AbstractTransport
    {
        public const string TimeoutOption = "timeout";
        public const string IntervalOption = "interval";
        public const string MaxNetworkDelayOption = "maxNetworkDelay";

        public ClientTransport(string name, IDictionary<string, object> options)
            : base(name, options)
        {
        }

        public virtual void Init()
        {
        }

        public abstract void Abort();

        public abstract void Reset();

        public abstract bool Accept(string version);

        public abstract void Send(ITransportListener listener, IList<IMutableMessage> messages);

        public abstract bool IsSending { get; }
    }
}
