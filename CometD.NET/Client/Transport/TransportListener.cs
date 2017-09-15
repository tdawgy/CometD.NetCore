using System;
using System.Collections.Generic;
using CometD.NetCore.Bayeux;

namespace CometD.NetCore.Client.Transport
{
    public interface ITransportListener
    {
        void onSending(IList<IMessage> messages);

        void onMessages(IList<IMutableMessage> messages);

        void onConnectException(Exception x, IList<IMessage> messages);

        void onException(Exception x, IList<IMessage> messages);

        void onExpire(IList<IMessage> messages);

        void onProtocolError(String info, IList<IMessage> messages);
    }
}
