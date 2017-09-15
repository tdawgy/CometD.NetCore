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
        private List<IExtension> _extensions = new List<IExtension>();
        private Dictionary<String, Object> _attributes = new Dictionary<String, Object>();
        private Dictionary<String, AbstractSessionChannel> _channels = new Dictionary<String, AbstractSessionChannel>();
        private int _batch;
        private int _idGen;

        protected String newMessageId()
        {
            return Convert.ToString(_idGen++);
        }

        public void addExtension(IExtension extension)
        {
            _extensions.Add(extension);
        }

        public void removeExtension(IExtension extension)
        {
            _extensions.Remove(extension);
        }

        protected bool extendSend(IMutableMessage message)
        {
            if (message.Meta)
            {
                foreach (var extension in _extensions)
                    if (!extension.sendMeta(this, message))
                        return false;
            }
            else
            {
                foreach (var extension in _extensions)
                    if (!extension.send(this, message))
                        return false;
            }
            return true;
        }

        protected bool extendRcv(IMutableMessage message)
        {
            if (message.Meta)
            {
                foreach (var extension in _extensions)
                    if (!extension.rcvMeta(this, message))
                        return false;
            }
            else
            {
                foreach (var extension in _extensions)
                    if (!extension.rcv(this, message))
                        return false;
            }
            return true;
        }

        /* ------------------------------------------------------------ */
        protected abstract ChannelId newChannelId(String channelId);

        /* ------------------------------------------------------------ */
        protected abstract AbstractSessionChannel newChannel(ChannelId channelId);

        /* ------------------------------------------------------------ */
        public IClientSessionChannel getChannel(String channelId)
        {
            _channels.TryGetValue(channelId, out var channel);

            if (channel == null)
            {
                var id = newChannelId(channelId);
                var new_channel = newChannel(id);

                if (_channels.ContainsKey(channelId))
                    channel = _channels[channelId];
                else
                    _channels[channelId] = new_channel;

                if (channel == null)
                    channel = new_channel;
            }
            return channel;
        }

        protected Dictionary<String, AbstractSessionChannel> Channels => _channels;

        /* ------------------------------------------------------------ */
        public void startBatch()
        {
            _batch++;
        }

        /* ------------------------------------------------------------ */
        protected abstract void sendBatch();

        /* ------------------------------------------------------------ */
        public bool endBatch()
        {
            if (--_batch == 0)
            {
                sendBatch();
                return true;
            }
            return false;
        }

        /* ------------------------------------------------------------ */
        public void batch(BatchDelegate batch)
        {
            startBatch();
            try
            {
                batch();
            }
            finally
            {
                endBatch();
            }
        }

        protected bool Batching => _batch > 0;

        /* ------------------------------------------------------------ */
        public Object getAttribute(String name)
        {
            _attributes.TryGetValue(name, out var obj);
            return obj;
        }

        /* ------------------------------------------------------------ */
        public ICollection<String> AttributeNames => _attributes.Keys;

        /* ------------------------------------------------------------ */
        public Object removeAttribute(String name)
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
        public void setAttribute(String name, Object val)
        {
            _attributes[name] = val;
        }

        /* ------------------------------------------------------------ */
        public void resetSubscriptions()
        {
            foreach (var channel in _channels)
            {
                channel.Value.resetSubscriptions();
            }
        }

        /* ------------------------------------------------------------ */
        /// <summary> <p>Receives a message (from the server) and process it.</p>
        /// <p>Processing the message involves calling the receive {@link ClientSession.Extension extensions}
        /// and the channel {@link ClientSessionChannel.ClientSessionChannelListener listeners}.</p>
        /// </summary>
        /// <param name="message">the message received.
        /// </param>
        /// <param name="mutable">the mutable version of the message received
        /// </param>
        public void receive(IMutableMessage message)
        {
            var id = message.Channel;
            if (id == null)
            {
                throw new ArgumentException("Bayeux messages must have a channel, " + message);
            }

            if (!extendRcv(message))
                return;

            var channel = (AbstractSessionChannel)getChannel(id);
            var channelId = channel.ChannelId;

            channel.notifyMessageListeners(message);

            foreach (var channelPattern in channelId.Wilds)
            {
                var channelIdPattern = newChannelId(channelPattern);
                if (channelIdPattern.matches(channelId))
                {
                    var wildChannel = (AbstractSessionChannel)getChannel(channelPattern);
                    wildChannel.notifyMessageListeners(message);
                }
            }
        }

        public abstract void handshake(IDictionary<String, Object> template);
        public abstract void handshake();
        public abstract void disconnect();
        public abstract bool Handshook { get; }
        public abstract String Id { get; }
        public abstract bool Connected { get; }

        /// <summary> <p>A channel scoped to a {@link ClientSession}.</p></summary>
        public abstract class AbstractSessionChannel : IClientSessionChannel
        {
            private ChannelId _id;
            private Dictionary<String, Object> _attributes = new Dictionary<String, Object>();
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
            public void addListener(IClientSessionChannelListener listener)
            {
                _listeners.Add(listener);
            }

            /* ------------------------------------------------------------ */
            public void removeListener(IClientSessionChannelListener listener)
            {
                _listeners.Remove(listener);
            }

            /* ------------------------------------------------------------ */
            protected abstract void sendSubscribe();

            /* ------------------------------------------------------------ */
            protected abstract void sendUnSubscribe();

            /* ------------------------------------------------------------ */
            public void subscribe(IMessageListener listener)
            {
                _subscriptions.Add(listener);

                _subscriptionCount++;
                var count = _subscriptionCount;
                if (count == 1)
                    sendSubscribe();
            }

            /* ------------------------------------------------------------ */
            public void unsubscribe(IMessageListener listener)
            {
                _subscriptions.Remove(listener);

                _subscriptionCount--;
                if (_subscriptionCount < 0) _subscriptionCount = 0;
                var count = _subscriptionCount;
                if (count == 0)
                    sendUnSubscribe();
            }

            /* ------------------------------------------------------------ */
            public void unsubscribe()
            {
                foreach (var listener in new List<IMessageListener>(_subscriptions))
                    unsubscribe(listener);
            }

            /* ------------------------------------------------------------ */
            public void resetSubscriptions()
            {
                foreach (var listener in new List<IMessageListener>(_subscriptions))
                {
                    _subscriptions.Remove(listener);
                    _subscriptionCount--;
                }
            }

            /* ------------------------------------------------------------ */
            public String Id => _id.ToString();

            /* ------------------------------------------------------------ */
            public bool DeepWild => _id.DeepWild;

            /* ------------------------------------------------------------ */
            public bool Meta => _id.isMeta();

            /* ------------------------------------------------------------ */
            public bool Service => _id.isService();

            /* ------------------------------------------------------------ */
            public bool Wild => _id.Wild;

            public void notifyMessageListeners(IMessage message)
            {
                foreach (var listener in _listeners)
                {
                    if (listener is IMessageListener)
                    {
                        try
                        {
                            ((IMessageListener)listener).onMessage(this, message);
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
                                listener.onMessage(this, message);
                            }
                            catch (Exception x)
                            {
                                Console.WriteLine("{0}", x);
                            }
                        }
                    }
                }
            }

            public void setAttribute(String name, Object val)
            {
                _attributes[name] = val;
            }

            public Object getAttribute(String name)
            {
                _attributes.TryGetValue(name, out var obj);
                return obj;
            }

            public ICollection<String> AttributeNames => _attributes.Keys;

            public Object removeAttribute(String name)
            {
                var old = getAttribute(name);
                _attributes.Remove(name);
                return old;
            }

            public abstract IClientSession Session { get; }

            /* ------------------------------------------------------------ */
            public override String ToString()
            {
                return _id.ToString();
            }

            public abstract void publish(Object param1);
            public abstract void publish(Object param1, String param2);
        }
    }
}
