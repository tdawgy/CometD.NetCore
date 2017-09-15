using System;
using System.Collections.Generic;

namespace CometD.NetCore.Client.Transport
{
    public class TransportRegistry
    {
        private IDictionary<String, ClientTransport> _transports = new Dictionary<String, ClientTransport>();
        private List<String> _allowed = new List<String>();

        public void Add(ClientTransport transport)
        {
            if (transport != null)
            {
                _transports[transport.Name] = transport;
                _allowed.Add(transport.Name);
            }
        }

        public IList<String> KnownTransports
        {
            get
            {
                var newList = new List<String>(_transports.Keys.Count);
                foreach (var key in _transports.Keys)
                {
                    newList.Add(key);
                }
                return newList.AsReadOnly();
            }
        }

        public IList<String> AllowedTransports => _allowed.AsReadOnly();

        public IList<ClientTransport> Negotiate(IList<Object> requestedTransports, String bayeuxVersion)
        {
            var list = new List<ClientTransport>();

            foreach (var transportName in _allowed)
            {
                foreach (var requestedTransportName in requestedTransports)
                {
                    if (requestedTransportName.Equals(transportName))
                    {
                        var transport = getTransport(transportName);
                        if (transport.accept(bayeuxVersion))
                        {
                            list.Add(transport);
                        }
                    }
                }
            }
            return list;
        }

        public ClientTransport getTransport(String transport)
        {
            ClientTransport obj;
            _transports.TryGetValue(transport, out obj);
            return obj;
        }
    }
}
