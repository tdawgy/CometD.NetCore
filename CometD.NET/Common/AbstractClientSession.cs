using System;
using System.Collections.Generic;
using CometD.NetCore.Bayeux;
using CometD.NetCore.Bayeux.Client;

namespace CometD.NetCore.Common
{
    /// <inheritdoc />
    /// <summary> <p>Partial implementation of {@link ClientSession}.</p>
    /// <p>It handles extensions and batching, and provides utility methods to be used by subclasses.</p>
    /// </summary>
    public abstract class AbstractClientSession : IClientSession
    {
        private readonly List<IExtension> _extensions = new List<IExtension>();
        private readonly Dictionary<string, object> _attributes = new Dictionary<string, object>();
        private readonly Dictionary<string, AbstractSessionChannel> _channels = new Dictionary<string, AbstractSessionChannel>();
        private int _batch;
        private int _idGen;

        protected string NewMessageId()
        {
            return Convert.ToString(_idGen++);
        }

        public void AddExtension(IExtension extension)
        {
            _extensions.Add(extension);
        }

        public void RemoveExtension(IExtension extension)
        {
            _extensions.Remove(extension);
        }

        protected bool ExtendSend(IMutableMessage message)
        {
            if (message.Meta)
            {
                foreach (var extension in _extensions)
                    if (!extension.SendMeta(this, message))
                        return false;
            }
            else
            {
                foreach (var extension in _extensions)
                    if (!extension.Send(this, message))
                        return false;
            }
            return true;
        }

        protected bool ExtendRcv(IMutableMessage message)
        {
            if (message.Meta)
            {
                foreach (var extension in _extensions)
                    if (!extension.ReceiveMeta(this, message))
                        return false;
            }
            else
            {
                foreach (var extension in _extensions)
                    if (!extension.Receive(this, message))
                        return false;
            }
            return true;
        }

        /* ------------------------------------------------------------ */
        protected abstract ChannelId NewChannelId(string channelId);

        /* ------------------------------------------------------------ */
        protected abstract AbstractSessionChannel NewChannel(ChannelId channelId);

        /* ------------------------------------------------------------ */
        public IClientSessionChannel GetChannel(string channelId)
        {
            _channels.TryGetValue(channelId, out var channel);

            if (channel == null)
            {
                var id = NewChannelId(channelId);
                var newChannel = NewChannel(id);

                if (_channels.ContainsKey(channelId))
                    channel = _channels[channelId];
                else
                    _channels[channelId] = newChannel;

                if (channel == null)
                    channel = newChannel;
            }
            return channel;
        }

        protected Dictionary<string, AbstractSessionChannel> Channels => _channels;

        /* ------------------------------------------------------------ */
        public void StartBatch()
        {
            _batch++;
        }

        /* ------------------------------------------------------------ */
        protected abstract void SendBatch();

        /* ------------------------------------------------------------ */
        public bool EndBatch()
        {
            if (--_batch == 0)
            {
                SendBatch();
                return true;
            }
            return false;
        }

        /* ------------------------------------------------------------ */
        public void Batch(BatchDelegate batch)
        {
            StartBatch();
            try
            {
                batch();
            }
            finally
            {
                EndBatch();
            }
        }

        protected bool Batching => _batch > 0;

        /* ------------------------------------------------------------ */
        public object GetAttribute(string name)
        {
            _attributes.TryGetValue(name, out var obj);
            return obj;
        }

        /* ------------------------------------------------------------ */
        public ICollection<string> AttributeNames => _attributes.Keys;

        /* ------------------------------------------------------------ */
        public object RemoveAttribute(string name)
        {
            try
            {
                var old = _attributes[name];
                _attributes.Remove(name);
                return old;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /* ------------------------------------------------------------ */
        public void SetAttribute(string name, object val)
        {
            _attributes[name] = val;
        }

        /* ------------------------------------------------------------ */
        public void ResetSubscriptions()
        {
            foreach (var channel in _channels)
            {
                channel.Value.ResetSubscriptions();
            }
        }

        /* ------------------------------------------------------------ */
        /// <summary> <p>Receives a message (from the server) and process it.</p>
        /// <p>Processing the message involves calling the receive {@link ClientSession.Extension extensions}
        /// and the channel {@link ClientSessionChannel.ClientSessionChannelListener listeners}.</p>
        /// </summary>
        /// <param name="message">the message received.
        /// </param>
        public void Receive(IMutableMessage message)
        {
            var id = message.Channel;
            if (id == null)
            {
                throw new ArgumentException("Bayeux messages must have a channel, " + message);
            }

            if (!ExtendRcv(message))
                return;

            var channel = (AbstractSessionChannel)GetChannel(id);
            var channelId = channel.ChannelId;

            channel.NotifyMessageListeners(message);

            foreach (var channelPattern in channelId.Wilds)
            {
                var channelIdPattern = NewChannelId(channelPattern);
                if (channelIdPattern.Matches(channelId))
                {
                    var wildChannel = (AbstractSessionChannel)GetChannel(channelPattern);
                    wildChannel.NotifyMessageListeners(message);
                }
            }
        }

        public abstract void Handshake(IDictionary<string, object> template);
        public abstract void Handshake();
        public abstract void Disconnect();
        public abstract bool Handshook { get; }
        public abstract string Id { get; }
        public abstract bool Connected { get; }

        /// <summary> <p>A channel scoped to a {@link ClientSession}.</p></summary>
        public abstract class AbstractSessionChannel : IClientSessionChannel
        {
            private ChannelId _id;
            private Dictionary<string, object> _attributes = new Dictionary<string, object>();
            private List<IMessageListener> _subscriptions = new List<IMessageListener>();
            private int _subscriptionCount;
            private List<IClientSessionChannelListener> _listeners = new List<IClientSessionChannelListener>();

            /* ------------------------------------------------------------ */
            protected AbstractSessionChannel(ChannelId id)
            {
                _id = id;
            }

            /* ------------------------------------------------------------ */
            public ChannelId ChannelId => _id;

            /* ------------------------------------------------------------ */
            public void AddListener(IClientSessionChannelListener listener)
            {
                _listeners.Add(listener);
            }

            /* ------------------------------------------------------------ */
            public void RemoveListener(IClientSessionChannelListener listener)
            {
                _listeners.Remove(listener);
            }

            /* ------------------------------------------------------------ */
            protected abstract void SendSubscribe();

            /* ------------------------------------------------------------ */
            protected abstract void SendUnSubscribe();

            /* ------------------------------------------------------------ */
            public void Subscribe(IMessageListener listener)
            {
                _subscriptions.Add(listener);

                _subscriptionCount++;
                var count = _subscriptionCount;
                if (count == 1)
                    SendSubscribe();
            }

            /* ------------------------------------------------------------ */
            public void Unsubscribe(IMessageListener listener)
            {
                _subscriptions.Remove(listener);

                _subscriptionCount--;
                if (_subscriptionCount < 0) _subscriptionCount = 0;
                var count = _subscriptionCount;
                if (count == 0)
                    SendUnSubscribe();
            }

            /* ------------------------------------------------------------ */
            public void Unsubscribe()
            {
                foreach (var listener in new List<IMessageListener>(_subscriptions))
                    Unsubscribe(listener);
            }

            /* ------------------------------------------------------------ */
            public void ResetSubscriptions()
            {
                foreach (var listener in new List<IMessageListener>(_subscriptions))
                {
                    _subscriptions.Remove(listener);
                    _subscriptionCount--;
                }
            }

            /* ------------------------------------------------------------ */
            public string Id => _id.ToString();

            /* ------------------------------------------------------------ */
            public bool DeepWild => _id.DeepWild;

            /* ------------------------------------------------------------ */
            public bool Meta => _id.IsMeta();

            /* ------------------------------------------------------------ */
            public bool Service => _id.IsService();

            /* ------------------------------------------------------------ */
            public bool Wild => _id.Wild;

            public void NotifyMessageListeners(IMessage message)
            {
                foreach (var listener in _listeners)
                {
                    if (listener is IMessageListener)
                    {
                        try
                        {
                            ((IMessageListener)listener).OnMessage(this, message);
                        }
                        catch (Exception x)
                        {
                            Console.WriteLine("{0}", x);
                        }
                    }
                }

                var list = new List<IMessageListener>(_subscriptions);
                foreach (var listener in list)
                {
                    if (listener != null)
                    {
                        if (message.Data != null)
                        {
                            try
                            {
                                listener.OnMessage(this, message);
                            }
                            catch (Exception x)
                            {
                                Console.WriteLine("{0}", x);
                            }
                        }
                    }
                }
            }

            public void SetAttribute(string name, object val)
            {
                _attributes[name] = val;
            }

            public object GetAttribute(string name)
            {
                _attributes.TryGetValue(name, out var obj);
                return obj;
            }

            public ICollection<string> AttributeNames => _attributes.Keys;

            public object RemoveAttribute(string name)
            {
                var old = GetAttribute(name);
                _attributes.Remove(name);
                return old;
            }

            public abstract IClientSession Session { get; }

            /* ------------------------------------------------------------ */
            public override string ToString()
            {
                return _id.ToString();
            }

            public abstract void Publish(object param1);
            public abstract void Publish(object param1, string param2);
        }
    }
}
