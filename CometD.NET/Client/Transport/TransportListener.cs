using System;
using System.Collections.Generic;
using CometD.NetCore.Bayeux;

namespace CometD.NetCore.Client.Transport
{
    public interface ITransportListener
    {
        void OnSending(IList<IMessage> messages);

        void OnMessages(IList<IMutableMessage> messages);

        void OnConnectException(Exception x, IList<IMessage> messages);

        void OnException(Exception x, IList<IMessage> messages);

        void OnExpire(IList<IMessage> messages);

        void OnProtocolError(string info, IList<IMessage> messages);
    }
}
