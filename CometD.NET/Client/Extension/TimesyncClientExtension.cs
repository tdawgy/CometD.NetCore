using System;
using System.Collections.Generic;
using Cometd.Common;
using CometD.NetCore.Bayeux;
using CometD.NetCore.Bayeux.Client;

namespace CometD.NetCore.Client.Extension
{
    public class TimesyncClientExtension : IExtension
    {
        public int Offset => _offset;

        public int Lag => _lag;

        public long ServerTime => (DateTime.Now.Ticks - 621355968000000000) / 10000 + _offset;

        private volatile int _lag;
        private volatile int _offset;

        public bool rcv(IClientSession session, IMutableMessage message)
        {
            return true;
        }

        public bool rcvMeta(IClientSession session, IMutableMessage message)
        {
            var ext = (Dictionary<String, Object>)message.getExt(false);
            var sync = (Dictionary<string, object>) ext?["timesync"];
            if (sync != null)
            {
                var now = (DateTime.Now.Ticks - 621355968000000000) / 10000;

                var tc = ObjectConverter.ToInt64(sync["tc"], 0);
                var ts = ObjectConverter.ToInt64(sync["ts"], 0);
                var p = ObjectConverter.ToInt32(sync["p"], 0);

                var l2 = (int)((now - tc - p) / 2);
                var o2 = (int)(ts - tc - l2);

                _lag = _lag == 0 ? l2 : (_lag + l2) / 2;
                _offset = _offset == 0 ? o2 : (_offset + o2) / 2;
            }

            return true;
        }

        public bool send(IClientSession session, IMutableMessage message)
        {
            return true;
        }

        public bool sendMeta(IClientSession session, IMutableMessage message)
        {
            var ext = (Dictionary<String, Object>)message.getExt(true);
            var now = (DateTime.Now.Ticks - 621355968000000000) / 10000;
            // Changed JSON.Literal to String
            var timesync = "{\"tc\":" + now + ",\"l\":" + _lag + ",\"o\":" + _offset + "}";
            ext["timesync"] = timesync;
            return true;
        }
    }
}
