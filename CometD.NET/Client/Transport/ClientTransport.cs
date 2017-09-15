using System;
using System.Collections.Generic;
using Cometd.Common;
using CometD.NetCore.Bayeux;
using CometD.NetCore.Common;

namespace CometD.NetCore.Client.Transport
{
    public abstract class ClientTransport : AbstractTransport
    {
        public const String TIMEOUT_OPTION = "timeout";
        public const String INTERVAL_OPTION = "interval";
        public const String MAX_NETWORK_DELAY_OPTION = "maxNetworkDelay";

        public ClientTransport(String name, IDictionary<String, Object> options)
            : base(name, options)
        {
        }

        public virtual void init()
        {
        }

        public abstract void abort();

        public abstract void reset();

        public abstract bool accept(String version);

        public abstract void send(ITransportListener listener, IList<IMutableMessage> messages);

        public abstract bool isSending { get; }
    }
}
