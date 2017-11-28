using System.Collections.Generic;

namespace CometD.NetCore.Client.Transport
{
    public class TransportRegistry
    {
        private readonly IDictionary<string, ClientTransport> _transports = new Dictionary<string, ClientTransport>();
        private readonly List<string> _allowed = new List<string>();

        public void Add(ClientTransport transport)
        {
            if (transport == null) return;

            _transports[transport.Name] = transport;
            _allowed.Add(transport.Name);
        }

        public IList<string> KnownTransports
        {
            get
            {
                var newList = new List<string>(_transports.Keys.Count);
                newList.AddRange(_transports.Keys);
                return newList.AsReadOnly();
            }
        }

        public IList<string> AllowedTransports => _allowed.AsReadOnly();

        public IList<ClientTransport> Negotiate(IList<object> requestedTransports, string bayeuxVersion)
        {
            var list = new List<ClientTransport>();

            foreach (var transportName in _allowed)
            {
                foreach (var requestedTransportName in requestedTransports)
                {
                    if (!requestedTransportName.Equals(transportName)) continue;

                    var transport = GetTransport(transportName);
                    if (transport.Accept(bayeuxVersion))
                    {
                        list.Add(transport);
                    }
                }
            }
            return list;
        }

        public ClientTransport GetTransport(string transport)
        {
            _transports.TryGetValue(transport, out var obj);
            return obj;
        }
    }
}
