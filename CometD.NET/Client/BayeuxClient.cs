using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using Cometd.Common;
using CometD.NetCore.Bayeux;
using CometD.NetCore.Bayeux.Client;
using CometD.NetCore.Client.Transport;
using CometD.NetCore.Common;

namespace CometD.NetCore.Client
{
    /// <summary> </summary>
    public class BayeuxClient : AbstractClientSession, IBayeux
    {
        public const String BACKOFF_INCREMENT_OPTION = "backoffIncrement";
        public const String MAX_BACKOFF_OPTION = "maxBackoff";
        public const String BAYEUX_VERSION = "1.0";

        //private Logger logger;
        private TransportRegistry transportRegistry = new TransportRegistry();
        private Dictionary<String, Object> options = new Dictionary<String, Object>();
        private BayeuxClientState bayeuxClientState;
        private Queue<IMutableMessage> messageQueue = new Queue<IMutableMessage>();
        private CookieCollection cookieCollection = new CookieCollection();
        private ITransportListener handshakeListener;
        private ITransportListener connectListener;
        private ITransportListener disconnectListener;
        private ITransportListener publishListener;
        private int backoffIncrement;
        private int maxBackoff;
        private static Mutex stateUpdateInProgressMutex = new Mutex();
        private int stateUpdateInProgress;
        private AutoResetEvent stateChanged = new AutoResetEvent(false);


        public BayeuxClient(String url, IList<ClientTransport> transports)
        {
            handshakeListener = new HandshakeTransportListener(this);
            connectListener = new ConnectTransportListener(this);
            disconnectListener = new DisconnectTransportListener(this);
            publishListener = new PublishTransportListener(this);

            if (transports == null || transports.Count == 0)
                throw new ArgumentException("Transport cannot be null");

            foreach (var t in transports)
                transportRegistry.Add(t);

            foreach (var transportName in transportRegistry.KnownTransports)
            {
                var clientTransport = transportRegistry.getTransport(transportName);
                if (clientTransport is HttpClientTransport)
                {
                    var httpTransport = (HttpClientTransport)clientTransport;
                    httpTransport.setURL(url);
                    httpTransport.setCookieCollection(cookieCollection);
                }
            }

            bayeuxClientState = new DisconnectedState(this, null);
        }

        public int BackoffIncrement => backoffIncrement;

        public int MaxBackoff => maxBackoff;

        public String getCookie(String name)
        {
            var cookie = cookieCollection[name];
            if (cookie != null)
                return cookie.Value;
            return null;
        }

        public void setCookie(String name, String val)
        {
            setCookie(name, val, -1);
        }

        public void setCookie(String name, String val, int maxAge)
        {
            var cookie = new Cookie(name, val, null, null);
            if (maxAge > 0)
            {
                cookie.Expires = DateTime.Now;
                cookie.Expires.AddMilliseconds(maxAge);
            }
            cookieCollection.Add(cookie);
        }

        public override String Id => bayeuxClientState.clientId;

        public override bool Connected => isConnected(bayeuxClientState);

        private bool isConnected(BayeuxClientState bayeuxClientState)
        {
            return bayeuxClientState.type == State.CONNECTED;
        }

        public override bool Handshook => isHandshook(bayeuxClientState);

        private bool isHandshook(BayeuxClientState bayeuxClientState)
        {
            return bayeuxClientState.type == State.CONNECTING || bayeuxClientState.type == State.CONNECTED || bayeuxClientState.type == State.UNCONNECTED;
        }

        private bool isHandshaking(BayeuxClientState bayeuxClientState)
        {
            return bayeuxClientState.type == State.HANDSHAKING || bayeuxClientState.type == State.REHANDSHAKING;
        }

        public bool Disconnected => isDisconnected(bayeuxClientState);

        private bool isDisconnected(BayeuxClientState bayeuxClientState)
        {
            return bayeuxClientState.type == State.DISCONNECTING || bayeuxClientState.type == State.DISCONNECTED;
        }

        protected State CurrentState => bayeuxClientState.type;

        public override void handshake()
        {
            handshake(null);
        }

        public override void handshake(IDictionary<String, Object> handshakeFields)
        {
            initialize();

            var allowedTransports = AllowedTransports;
            // Pick the first transport for the handshake, it will renegotiate if not right
            var initialTransport = transportRegistry.getTransport(allowedTransports[0]);
            initialTransport.init();

            updateBayeuxClientState(
                    delegate
                    {
                        return new HandshakingState(this, handshakeFields, initialTransport);
                    });
        }

        public State handshake(int waitMs)
        {
            return handshake(null, waitMs);
        }

        public State handshake(IDictionary<String, Object> template, int waitMs)
        {
            handshake(template);
            ICollection<State> states = new List<State>();
            states.Add(State.CONNECTING);
            states.Add(State.DISCONNECTED);
            return waitFor(waitMs, states);
        }

        protected bool sendHandshake()
        {
            var bayeuxClientState = this.bayeuxClientState;

            if (isHandshaking(bayeuxClientState))
            {
                var message = newMessage();
                if (bayeuxClientState.handshakeFields != null)
                    foreach (var kvp in bayeuxClientState.handshakeFields)
                        message.Add(kvp.Key, kvp.Value);

                message.Channel = Channel_Fields.META_HANDSHAKE;
                message[Message_Fields.SUPPORTED_CONNECTION_TYPES_FIELD] = AllowedTransports;
                message[Message_Fields.VERSION_FIELD] = BAYEUX_VERSION;
                if (message.Id == null)
                    message.Id = newMessageId();

                bayeuxClientState.send(handshakeListener, message);
                return true;
            }
            return false;
        }

        public State waitFor(int waitMs, ICollection<State> states)
        {
            var stop = DateTime.Now.AddMilliseconds(waitMs);
            var duration = waitMs;

            var s = CurrentState;
            if (states.Contains(s))
                return s;

            while (stateChanged.WaitOne(duration))
            {
                if (stateUpdateInProgress == 0)
                {
                    s = CurrentState;
                    if (states.Contains(s))
                        return s;
                }

                duration = (int)(stop - DateTime.Now).TotalMilliseconds;
                if (duration <= 0) break;
            }

            s = CurrentState;
            if (states.Contains(s))
                return s;

            return State.INVALID;
        }

        protected bool sendConnect()
        {
            var bayeuxClientState = this.bayeuxClientState;
            if (isHandshook(bayeuxClientState))
            {
                var message = newMessage();
                message.Channel = Channel_Fields.META_CONNECT;
                message[Message_Fields.CONNECTION_TYPE_FIELD] = bayeuxClientState.transport.Name;
                if (bayeuxClientState.type == State.CONNECTING || bayeuxClientState.type == State.UNCONNECTED)
                {
                    // First connect after handshake or after failure, add advice
                    message.getAdvice(true)["timeout"] = 0;
                }
                bayeuxClientState.send(connectListener, message);
                return true;
            }
            return false;
        }

        protected override ChannelId newChannelId(String channelId)
        {
            // Save some parsing by checking if there is already one
            Channels.TryGetValue(channelId, out var channel);
            return channel == null ? new ChannelId(channelId) : channel.ChannelId;
        }

        protected override AbstractSessionChannel newChannel(ChannelId channelId)
        {
            return new BayeuxClientChannel(this, channelId);
        }

        protected override void sendBatch()
        {
            var bayeuxClientState = this.bayeuxClientState;
            if (isHandshaking(bayeuxClientState))
                return;

            var messages = takeMessages();
            if (messages.Count > 0)
                sendMessages(messages);
        }

        protected bool sendMessages(IList<IMutableMessage> messages)
        {
            var bayeuxClientState = this.bayeuxClientState;
            if (bayeuxClientState.type == State.CONNECTING || isConnected(bayeuxClientState))
            {
                bayeuxClientState.send(publishListener, messages);
                return true;
            }
            failMessages(null, ObjectConverter.ToListOfIMessage(messages));
            return false;
        }

        private int PendingMessages
        {
            get
            {
                var value = messageQueue.Count;

                var state = bayeuxClientState;
                if (state.transport is ClientTransport clientTransport)
                {
                    value += clientTransport.isSending ? 1 : 0;
                }

                return value;
            }
        }

        /// <summary>
        /// Wait for send queue to be emptied
        /// </summary>
        /// <param name="timeoutMS"></param>
        /// <returns>true if queue is empty, false if timed out</returns>
        public bool waitForEmptySendQueue(int timeoutMS)
        {
            if (PendingMessages == 0)
                return true;

            var start = DateTime.Now;

            while ((DateTime.Now - start).TotalMilliseconds < timeoutMS)
            {
                if (PendingMessages == 0)
                    return true;

                Thread.Sleep(100);
            }

            return false;
        }

        private IList<IMutableMessage> takeMessages()
        {
            IList<IMutableMessage> queue = new List<IMutableMessage>(messageQueue);
            messageQueue.Clear();
            return queue;
        }

        public override void disconnect()
        {
            updateBayeuxClientState(
                    delegate (BayeuxClientState oldState)
                    {
                        if (isConnected(oldState))
                            return new DisconnectingState(this, oldState.transport, oldState.clientId);
                        return new DisconnectedState(this, oldState.transport);
                    });
        }

        public void abort()
        {
            updateBayeuxClientState(
                oldState => new AbortedState(this, oldState.transport));
        }

        protected void processHandshake(IMutableMessage handshake)
        {
            if (handshake.Successful)
            {
                handshake.TryGetValue(Message_Fields.SUPPORTED_CONNECTION_TYPES_FIELD, out var serverTransportObject);
                var serverTransports = serverTransportObject as IList<Object>;
                var negotiatedTransports = transportRegistry.Negotiate(serverTransports, BAYEUX_VERSION);
                var newTransport = negotiatedTransports.Count == 0 ? null : negotiatedTransports[0];
                if (newTransport == null)
                {
                    updateBayeuxClientState(
                        oldState => new DisconnectedState(this, oldState.transport),
                            delegate ()
                            {
                                receive(handshake);
                            });

                    // Signal the failure
                    var error = "405:c" + transportRegistry.AllowedTransports + ",s" + serverTransports + ":no transport";

                    handshake.Successful = false;
                    handshake[Message_Fields.ERROR_FIELD] = error;
                }
                else
                {
                    updateBayeuxClientState(
                            delegate (BayeuxClientState oldState)
                            {
                                if (newTransport != oldState.transport)
                                {
                                    oldState.transport.reset();
                                    newTransport.init();
                                }

                                var action = getAdviceAction(handshake.Advice, Message_Fields.RECONNECT_RETRY_VALUE);
                                if (Message_Fields.RECONNECT_RETRY_VALUE.Equals(action))
                                    return new ConnectingState(this, oldState.handshakeFields, handshake.Advice, newTransport, handshake.ClientId);
                                if (Message_Fields.RECONNECT_NONE_VALUE.Equals(action))
                                    return new DisconnectedState(this, oldState.transport);

                                return null;
                            },
                            delegate
                            {
                                receive(handshake);
                            });
                }
            }
            else
            {
                updateBayeuxClientState(
                        delegate (BayeuxClientState oldState)
                        {
                            var action = getAdviceAction(handshake.Advice, Message_Fields.RECONNECT_HANDSHAKE_VALUE);
                            if (Message_Fields.RECONNECT_HANDSHAKE_VALUE.Equals(action) || Message_Fields.RECONNECT_RETRY_VALUE.Equals(action))
                                return new RehandshakingState(this, oldState.handshakeFields, oldState.transport, oldState.nextBackoff());
                            if (Message_Fields.RECONNECT_NONE_VALUE.Equals(action))
                                return new DisconnectedState(this, oldState.transport);
                            return null;
                        },
                        delegate
                        {
                            receive(handshake);
                        });
            }
        }

        protected void processConnect(IMutableMessage connect)
        {
            updateBayeuxClientState(
                    delegate (BayeuxClientState oldState)
                    {
                        var advice = connect.Advice;
                        if (advice == null)
                            advice = oldState.advice;

                        var action = getAdviceAction(advice, Message_Fields.RECONNECT_RETRY_VALUE);
                        if (connect.Successful)
                        {
                            if (Message_Fields.RECONNECT_RETRY_VALUE.Equals(action))
                                return new ConnectedState(this, oldState.handshakeFields, advice, oldState.transport, oldState.clientId);
                            if (Message_Fields.RECONNECT_NONE_VALUE.Equals(action))
                                // This case happens when the connect reply arrives after a disconnect
                                // We do not go into a disconnected state to allow normal processing of the disconnect reply
                                return new DisconnectingState(this, oldState.transport, oldState.clientId);
                        }
                        else
                        {
                            if (Message_Fields.RECONNECT_HANDSHAKE_VALUE.Equals(action))
                                return new RehandshakingState(this, oldState.handshakeFields, oldState.transport, 0);
                            if (Message_Fields.RECONNECT_RETRY_VALUE.Equals(action))
                                return new UnconnectedState(this, oldState.handshakeFields, advice, oldState.transport, oldState.clientId, oldState.nextBackoff());
                            if (Message_Fields.RECONNECT_NONE_VALUE.Equals(action))
                                return new DisconnectedState(this, oldState.transport);
                        }

                        return null;
                    },
                delegate
                {
                    receive(connect);
                });
        }

        protected void processDisconnect(IMutableMessage disconnect)
        {
            updateBayeuxClientState(
                oldState => new DisconnectedState(this, oldState.transport),
                    delegate ()
                    {
                        receive(disconnect);
                    });
        }

        protected void processMessage(IMutableMessage message)
        {
            receive(message);
        }

        private String getAdviceAction(IDictionary<String, Object> advice, String defaultResult)
        {
            var action = defaultResult;
            if (advice != null && advice.ContainsKey(Message_Fields.RECONNECT_FIELD))
                action = ((String)advice[Message_Fields.RECONNECT_FIELD]);
            return action;
        }

        protected bool scheduleHandshake(int interval, int backoff)
        {
            return scheduleAction(
                    delegate
                    {
                        sendHandshake();
                    }
                    , interval, backoff);
        }

        protected bool scheduleConnect(int interval, int backoff)
        {
            return scheduleAction(
                    delegate
                    {
                        sendConnect();
                    }
                    , interval, backoff);
        }

        private bool scheduleAction(TimerCallback action, int interval, int backoff)
        {
            var x = new object();
            var timer2 = new Timer(action, x, interval + backoff, 1);

            return true;
        }

        public IList<String> AllowedTransports => transportRegistry.AllowedTransports;

        public ICollection<String> KnownTransportNames => transportRegistry.KnownTransports;

        public ITransport getTransport(String transport)
        {
            return transportRegistry.getTransport(transport);
        }

        public void setDebugEnabled(bool debug)
        {
            // todo
        }

        public bool isDebugEnabled()
        {
            return false;
        }

        protected void initialize()
        {
            var backoffIncrement = ObjectConverter.ToInt32(getOption(BACKOFF_INCREMENT_OPTION), 1000);
            this.backoffIncrement = backoffIncrement;

            var maxBackoff = ObjectConverter.ToInt32(getOption(MAX_BACKOFF_OPTION), 30000);
            this.maxBackoff = maxBackoff;
        }

        protected void terminate()
        {
            var messages = takeMessages();
            failMessages(null, ObjectConverter.ToListOfIMessage(messages));
        }

        public Object getOption(String qualifiedName)
        {
            options.TryGetValue(qualifiedName, out var obj);
            return obj;
        }

        public void setOption(String qualifiedName, Object val)
        {
            options[qualifiedName] = val;
        }

        public ICollection<String> OptionNames => options.Keys;

        public IDictionary<String, Object> Options => options;

        protected IMutableMessage newMessage()
        {
            return new DictionaryMessage();
        }

        protected void enqueueSend(IMutableMessage message)
        {
            if (canSend())
            {
                IList<IMutableMessage> messages = new List<IMutableMessage>();
                messages.Add(message);
                sendMessages(messages);
            }
            else
            {
                messageQueue.Enqueue(message);
            }
        }

        private bool canSend()
        {
            return !isDisconnected(bayeuxClientState) && !Batching && !isHandshaking(bayeuxClientState);
        }

        protected void failMessages(Exception x, IList<IMessage> messages)
        {
            foreach (var message in messages)
            {
                var failed = newMessage();
                failed.Id = message.Id;
                failed.Successful = false;
                failed.Channel = message.Channel;
                failed["message"] = messages;
                if (x != null)
                    failed["exception"] = x;
                receive(failed);
            }
        }

        public void onSending(IList<IMessage> messages)
        {
        }

        public void onMessages(IList<IMutableMessage> messages)
        {
        }

        public virtual void onFailure(Exception x, IList<IMessage> messages)
        {
            Console.WriteLine("{0}", x.ToString());
        }

        private void updateBayeuxClientState(BayeuxClientStateUpdater_createDelegate create, BayeuxClientStateUpdater_postCreateDelegate postCreate = null)
        {
            stateUpdateInProgressMutex.WaitOne();
            ++stateUpdateInProgress;
            stateUpdateInProgressMutex.ReleaseMutex();

            BayeuxClientState newState;
            var oldState = bayeuxClientState;

            newState = create(oldState);
            if (newState == null)
                throw new Exception();

            if (!oldState.isUpdateableTo(newState))
            {
                return;
            }

            bayeuxClientState = newState;

            postCreate?.Invoke();

            if (oldState.Type != newState.Type)
                newState.enter(oldState.Type);

            newState.execute();

            // Notify threads waiting in waitFor()
            stateUpdateInProgressMutex.WaitOne();
            --stateUpdateInProgress;

            if (stateUpdateInProgress == 0)
                stateChanged.Set();
            stateUpdateInProgressMutex.ReleaseMutex();
        }

        public String dump()
        {
            return "";
        }

        public enum State
        {
            INVALID, UNCONNECTED, HANDSHAKING, REHANDSHAKING, CONNECTING, CONNECTED, DISCONNECTING, DISCONNECTED
        }

        private class PublishTransportListener : ITransportListener
        {
            protected readonly BayeuxClient bayeuxClient;

            public PublishTransportListener(BayeuxClient bayeuxClient)
            {
                this.bayeuxClient = bayeuxClient;
            }

            public void onSending(IList<IMessage> messages)
            {
                bayeuxClient.onSending(messages);
            }

            public void onMessages(IList<IMutableMessage> messages)
            {
                bayeuxClient.onMessages(messages);
                foreach (var message in messages)
                    processMessage(message);
            }

            public void onConnectException(Exception x, IList<IMessage> messages)
            {
                onFailure(x, messages);
            }

            public void onException(Exception x, IList<IMessage> messages)
            {
                onFailure(x, messages);
            }

            public void onExpire(IList<IMessage> messages)
            {
                onFailure(new TimeoutException("expired"), messages);
            }

            public void onProtocolError(String info, IList<IMessage> messages)
            {
                onFailure(new ProtocolViolationException(info), messages);
            }

            protected virtual void processMessage(IMutableMessage message)
            {
                bayeuxClient.processMessage(message);
            }

            protected virtual void onFailure(Exception x, IList<IMessage> messages)
            {
                bayeuxClient.onFailure(x, messages);
                bayeuxClient.failMessages(x, messages);
            }
        }

        private class HandshakeTransportListener : PublishTransportListener
        {
            public HandshakeTransportListener(BayeuxClient bayeuxClient)
                : base(bayeuxClient)
            {
            }

            protected override void onFailure(Exception x, IList<IMessage> messages)
            {
                bayeuxClient.updateBayeuxClientState(
                    oldState => new RehandshakingState(bayeuxClient, oldState.handshakeFields, oldState.transport,
                        oldState.nextBackoff()));
                base.onFailure(x, messages);
            }

            protected override void processMessage(IMutableMessage message)
            {
                if (Channel_Fields.META_HANDSHAKE.Equals(message.Channel))
                    bayeuxClient.processHandshake(message);
                else
                    base.processMessage(message);
            }
        }

        private class ConnectTransportListener : PublishTransportListener
        {
            public ConnectTransportListener(BayeuxClient bayeuxClient)
                : base(bayeuxClient)
            {
            }

            protected override void onFailure(Exception x, IList<IMessage> messages)
            {
                bayeuxClient.updateBayeuxClientState(
                    oldState => new UnconnectedState(bayeuxClient, oldState.handshakeFields, oldState.advice,
                        oldState.transport, oldState.clientId, oldState.nextBackoff()));
                base.onFailure(x, messages);
            }

            protected override void processMessage(IMutableMessage message)
            {
                if (Channel_Fields.META_CONNECT.Equals(message.Channel))
                    bayeuxClient.processConnect(message);
                else
                    base.processMessage(message);
            }
        }

        private class DisconnectTransportListener : PublishTransportListener
        {
            public DisconnectTransportListener(BayeuxClient bayeuxClient)
                : base(bayeuxClient)
            {
            }

            protected override void onFailure(Exception x, IList<IMessage> messages)
            {
                bayeuxClient.updateBayeuxClientState(
                    oldState => new DisconnectedState(bayeuxClient, oldState.transport));
                base.onFailure(x, messages);
            }

            protected override void processMessage(IMutableMessage message)
            {
                if (Channel_Fields.META_DISCONNECT.Equals(message.Channel))
                    bayeuxClient.processDisconnect(message);
                else
                    base.processMessage(message);
            }
        }

        public class BayeuxClientChannel : AbstractSessionChannel
        {
            protected BayeuxClient bayeuxClient;

            public BayeuxClientChannel(BayeuxClient bayeuxClient, ChannelId channelId)
                : base(channelId)
            {
                this.bayeuxClient = bayeuxClient;
            }

            public override IClientSession Session => this as IClientSession;


            protected override void sendSubscribe()
            {
                var message = bayeuxClient.newMessage();
                message.Channel = Channel_Fields.META_SUBSCRIBE;
                message[Message_Fields.SUBSCRIPTION_FIELD] = Id;
                bayeuxClient.enqueueSend(message);
            }

            protected override void sendUnSubscribe()
            {
                var message = bayeuxClient.newMessage();
                message.Channel = Channel_Fields.META_UNSUBSCRIBE;
                message[Message_Fields.SUBSCRIPTION_FIELD] = Id;
                bayeuxClient.enqueueSend(message);
            }

            public override void publish(Object data)
            {
                publish(data, null);
            }

            public override void publish(Object data, String messageId)
            {
                var message = bayeuxClient.newMessage();
                message.Channel = Id;
                message.Data = data;
                if (messageId != null)
                    message.Id = messageId;
                bayeuxClient.enqueueSend(message);
            }
        }

        private delegate BayeuxClientState BayeuxClientStateUpdater_createDelegate(BayeuxClientState oldState);
        private delegate void BayeuxClientStateUpdater_postCreateDelegate();

        public abstract class BayeuxClientState
        {
            public State type;
            public IDictionary<String, Object> handshakeFields;
            public IDictionary<String, Object> advice;
            public ClientTransport transport;
            public String clientId;
            public int backoff;
            protected BayeuxClient bayeuxClient;

            protected BayeuxClientState(BayeuxClient bayeuxClient, State type, IDictionary<String, Object> handshakeFields,
                    IDictionary<String, Object> advice, ClientTransport transport, String clientId, int backoff)
            {
                this.bayeuxClient = bayeuxClient;
                this.type = type;
                this.handshakeFields = handshakeFields;
                this.advice = advice;
                this.transport = transport;
                this.clientId = clientId;
                this.backoff = backoff;
            }

            public int Interval
            {
                get
                {
                    var result = 0;
                    if (advice != null && advice.ContainsKey(Message_Fields.INTERVAL_FIELD))
                        result = ObjectConverter.ToInt32(advice[Message_Fields.INTERVAL_FIELD], result);

                    return result;
                }
            }

            public void send(ITransportListener listener, IMutableMessage message)
            {
                IList<IMutableMessage> messages = new List<IMutableMessage>();
                messages.Add(message);
                send(listener, messages);
            }

            public void send(ITransportListener listener, IList<IMutableMessage> messages)
            {
                foreach (var message in messages)
                {
                    if (message.Id == null)
                        message.Id = bayeuxClient.newMessageId();
                    if (clientId != null)
                        message.ClientId = clientId;

                    if (!bayeuxClient.extendSend(message))
                        messages.Remove(message);
                }
                if (messages.Count > 0)
                {
                    transport.send(listener, messages);
                }
            }

            public int nextBackoff()
            {
                return Math.Min(backoff + bayeuxClient.BackoffIncrement, bayeuxClient.MaxBackoff);
            }

            public abstract bool isUpdateableTo(BayeuxClientState newState);

            public virtual void enter(State oldState)
            {
            }

            public abstract void execute();

            public State Type => type;

            public override String ToString()
            {
                return type.ToString();
            }
        }

        private class DisconnectedState : BayeuxClientState
        {
            public DisconnectedState(BayeuxClient bayeuxClient, ClientTransport transport)
                : base(bayeuxClient, State.DISCONNECTED, null, null, transport, null, 0)
            {
            }

            public override bool isUpdateableTo(BayeuxClientState newState)
            {
                return newState.type == State.HANDSHAKING;
            }

            public override void execute()
            {
                transport.reset();
                bayeuxClient.terminate();
            }
        }

        private class AbortedState : DisconnectedState
        {
            public AbortedState(BayeuxClient bayeuxClient, ClientTransport transport)
                : base(bayeuxClient, transport)
            {
            }

            public override void execute()
            {
                transport.abort();
                base.execute();
            }
        }

        private class HandshakingState : BayeuxClientState
        {
            public HandshakingState(BayeuxClient bayeuxClient, IDictionary<String, Object> handshakeFields, ClientTransport transport)
                : base(bayeuxClient, State.HANDSHAKING, handshakeFields, null, transport, null, 0)
            {
            }

            public override bool isUpdateableTo(BayeuxClientState newState)
            {
                return newState.type == State.REHANDSHAKING ||
                    newState.type == State.CONNECTING ||
                    newState.type == State.DISCONNECTED;
            }

            public override void enter(State oldState)
            {
                // Always reset the subscriptions when a handshake has been requested.
                bayeuxClient.resetSubscriptions();
            }

            public override void execute()
            {
                // The state could change between now and when sendHandshake() runs;
                // in this case the handshake message will not be sent and will not
                // be failed, because most probably the client has been disconnected.
                bayeuxClient.sendHandshake();
            }
        }

        private class RehandshakingState : BayeuxClientState
        {
            public RehandshakingState(BayeuxClient bayeuxClient, IDictionary<String, Object> handshakeFields, ClientTransport transport, int backoff)
                : base(bayeuxClient, State.REHANDSHAKING, handshakeFields, null, transport, null, backoff)
            {
            }

            public override bool isUpdateableTo(BayeuxClientState newState)
            {
                return newState.type == State.CONNECTING ||
                    newState.type == State.REHANDSHAKING ||
                    newState.type == State.DISCONNECTED;
            }

            public override void enter(State oldState)
            {
                // Reset the subscriptions if this is not a failure from a requested handshake.
                // Subscriptions may be queued after requested handshakes.
                if (oldState != State.HANDSHAKING)
                {
                    // Reset subscriptions if not queued after initial handshake
                    bayeuxClient.resetSubscriptions();
                }
            }

            public override void execute()
            {
                bayeuxClient.scheduleHandshake(Interval, backoff);
            }
        }

        private class ConnectingState : BayeuxClientState
        {
            public ConnectingState(BayeuxClient bayeuxClient, IDictionary<String, Object> handshakeFields, IDictionary<String, Object> advice, ClientTransport transport, String clientId)
                : base(bayeuxClient, State.CONNECTING, handshakeFields, advice, transport, clientId, 0)
            {
            }

            public override bool isUpdateableTo(BayeuxClientState newState)
            {
                return newState.type == State.CONNECTED ||
                    newState.type == State.UNCONNECTED ||
                    newState.type == State.REHANDSHAKING ||
                    newState.type == State.DISCONNECTING ||
                    newState.type == State.DISCONNECTED;
            }

            public override void execute()
            {
                // Send the messages that may have queued up before the handshake completed
                bayeuxClient.sendBatch();
                bayeuxClient.scheduleConnect(Interval, backoff);
            }
        }

        private class ConnectedState : BayeuxClientState
        {
            public ConnectedState(BayeuxClient bayeuxClient, IDictionary<String, Object> handshakeFields, IDictionary<String, Object> advice, ClientTransport transport, String clientId)
                : base(bayeuxClient, State.CONNECTED, handshakeFields, advice, transport, clientId, 0)
            {
            }

            public override bool isUpdateableTo(BayeuxClientState newState)
            {
                return newState.type == State.CONNECTED ||
                    newState.type == State.UNCONNECTED ||
                    newState.type == State.REHANDSHAKING ||
                    newState.type == State.DISCONNECTING ||
                    newState.type == State.DISCONNECTED;
            }

            public override void execute()
            {
                bayeuxClient.scheduleConnect(Interval, backoff);
            }
        }

        private class UnconnectedState : BayeuxClientState
        {
            public UnconnectedState(BayeuxClient bayeuxClient, IDictionary<String, Object> handshakeFields, IDictionary<String, Object> advice, ClientTransport transport, String clientId, int backoff)
                : base(bayeuxClient, State.UNCONNECTED, handshakeFields, advice, transport, clientId, backoff)
            {
            }

            public override bool isUpdateableTo(BayeuxClientState newState)
            {
                return newState.type == State.CONNECTED ||
                    newState.type == State.UNCONNECTED ||
                    newState.type == State.REHANDSHAKING ||
                    newState.type == State.DISCONNECTED;
            }

            public override void execute()
            {
                bayeuxClient.scheduleConnect(Interval, backoff);
            }
        }

        private class DisconnectingState : BayeuxClientState
        {
            public DisconnectingState(BayeuxClient bayeuxClient, ClientTransport transport, String clientId)
                : base(bayeuxClient, State.DISCONNECTING, null, null, transport, clientId, 0)
            {
            }

            public override bool isUpdateableTo(BayeuxClientState newState)
            {
                return newState.type == State.DISCONNECTED;
            }

            public override void execute()
            {
                var message = bayeuxClient.newMessage();
                message.Channel = Channel_Fields.META_DISCONNECT;
                send(bayeuxClient.disconnectListener, message);
            }
        }
    }
}
