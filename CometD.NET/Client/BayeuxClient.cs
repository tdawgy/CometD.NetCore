using CometD.NetCore.Bayeux;
using CometD.NetCore.Bayeux.Client;
using CometD.NetCore.Client.Transport;
using CometD.NetCore.Common;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace CometD.NetCore.Client
{
    /// <summary> </summary>
    public class BayeuxClient : AbstractClientSession, IBayeux
    {
        public const string BackoffIncrementOption = "backoffIncrement";
        public const string MaxBackoffOption = "maxBackoff";
        public const string BayeuxVersion = "1.0";

        //private Logger logger;
        private readonly TransportRegistry _transportRegistry = new TransportRegistry();
        private readonly Dictionary<string, object> _options = new Dictionary<string, object>();
        private BayeuxClientState _bayeuxClientState;
        private readonly Queue<IMutableMessage> _messageQueue = new Queue<IMutableMessage>();
        private readonly ITransportListener _handshakeListener;
        private readonly ITransportListener _connectListener;
        private readonly ITransportListener _disconnectListener;
        private readonly ITransportListener _publishListener;
        private static readonly Mutex StateUpdateInProgressMutex = new Mutex();
        private int _stateUpdateInProgress;
        private readonly AutoResetEvent _stateChanged = new AutoResetEvent(false);


        public BayeuxClient(string url, IList<ClientTransport> transports)
        {
            _handshakeListener = new HandshakeTransportListener(this);
            _connectListener = new ConnectTransportListener(this);
            _disconnectListener = new DisconnectTransportListener(this);
            _publishListener = new PublishTransportListener(this);

            if (transports == null || transports.Count == 0)
                throw new ArgumentException("Transport cannot be null");

            foreach (var t in transports)
                _transportRegistry.Add(t);

            foreach (var transportName in _transportRegistry.KnownTransports)
            {
                var clientTransport = _transportRegistry.GetTransport(transportName);
                if (!(clientTransport is HttpClientTransport)) continue;

                var httpTransport = (HttpClientTransport)clientTransport;
                httpTransport.Url = url;
            }

            _bayeuxClientState = new DisconnectedState(this, null);
        }

        public int BackoffIncrement { get; private set; }

        public int MaxBackoff { get; private set; }

        public override string Id => _bayeuxClientState.ClientId;

        public override bool Connected => IsConnected(_bayeuxClientState);

        private bool IsConnected(BayeuxClientState bayeuxClientState)
        {
            return bayeuxClientState.StateType == State.Connected;
        }

        public override bool Handshook => IsHandshook(_bayeuxClientState);

        private bool IsHandshook(BayeuxClientState bayeuxClientState)
        {
            return bayeuxClientState.StateType == State.Connecting || bayeuxClientState.StateType == State.Connected || bayeuxClientState.StateType == State.Unconnected;
        }

        private bool IsHandshaking(BayeuxClientState bayeuxClientState)
        {
            return bayeuxClientState.StateType == State.Handshaking || bayeuxClientState.StateType == State.Rehandshaking;
        }

        public bool Disconnected => IsDisconnected(_bayeuxClientState);

        private bool IsDisconnected(BayeuxClientState bayeuxClientState)
        {
            return bayeuxClientState.StateType == State.Disconnecting || bayeuxClientState.StateType == State.Disconnected;
        }

        protected State CurrentState => _bayeuxClientState.StateType;

        public override void Handshake()
        {
            Handshake(null);
        }

        public override void Handshake(IDictionary<string, object> handshakeFields)
        {
            Initialize();

            var allowedTransports = AllowedTransports;
            // Pick the first transport for the handshake, it will renegotiate if not right
            var initialTransport = _transportRegistry.GetTransport(allowedTransports[0]);
            initialTransport.Init();

            UpdateBayeuxClientState(
                    delegate
                    {
                        return new HandshakingState(this, handshakeFields, initialTransport);
                    });
        }

        public State Handshake(int waitMs)
        {
            return Handshake(null, waitMs);
        }

        public State Handshake(IDictionary<string, object> template, int waitMs)
        {
            Handshake(template);
            ICollection<State> states = new List<State>
            {
                State.Connecting,
                State.Disconnected
            };
            return WaitFor(waitMs, states);
        }

        protected void SendHandshake()
        {
            var bayeuxClientState = _bayeuxClientState;

            if (!IsHandshaking(bayeuxClientState)) return;

            var message = NewMessage();
            if (bayeuxClientState.HandshakeFields != null)
                foreach (var kvp in bayeuxClientState.HandshakeFields)
                    message.Add(kvp.Key, kvp.Value);

            message.Channel = ChannelFields.MetaHandshake;
            message[MessageFields.SupportedConnectionTypesField] = AllowedTransports;
            message[MessageFields.VersionField] = BayeuxVersion;
            if (message.Id == null)
                message.Id = NewMessageId();

            bayeuxClientState.Send(_handshakeListener, message);
        }

        public State WaitFor(int waitMs, ICollection<State> states)
        {
            var stop = DateTime.Now.AddMilliseconds(waitMs);
            var duration = waitMs;

            var s = CurrentState;
            if (states.Contains(s))
                return s;

            while (_stateChanged.WaitOne(duration))
            {
                if (_stateUpdateInProgress == 0)
                {
                    s = CurrentState;
                    if (states.Contains(s))
                        return s;
                }

                duration = (int)(stop - DateTime.Now).TotalMilliseconds;
                if (duration <= 0) break;
            }

            s = CurrentState;
            return states.Contains(s) ? s : State.Invalid;
        }

        protected void SendConnect()
        {
            var bayeuxClientState = _bayeuxClientState;
            if (!IsHandshook(bayeuxClientState)) return;

            var message = NewMessage();
            message.Channel = ChannelFields.MetaConnect;
            message[MessageFields.ConnectionTypeField] = bayeuxClientState.Transport.Name;
            if (bayeuxClientState.StateType == State.Connecting || bayeuxClientState.StateType == State.Unconnected)
            {
                // First connect after handshake or after failure, add advice
                message.GetAdvice(true)["timeout"] = 0;
            }
            bayeuxClientState.Send(_connectListener, message);
        }

        protected override ChannelId NewChannelId(string channelId)
        {
            // Save some parsing by checking if there is already one
            Channels.TryGetValue(channelId, out var channel);
            return channel == null ? new ChannelId(channelId) : channel.ChannelId;
        }

        protected override AbstractSessionChannel NewChannel(ChannelId channelId)
        {
            return new BayeuxClientChannel(this, channelId);
        }

        protected override void SendBatch()
        {
            var bayeuxClientState = _bayeuxClientState;
            if (IsHandshaking(bayeuxClientState))
                return;

            var messages = TakeMessages();
            if (messages.Count > 0)
                SendMessages(messages);
        }

        protected bool SendMessages(IList<IMutableMessage> messages)
        {
            var bayeuxClientState = _bayeuxClientState;
            if (bayeuxClientState.StateType == State.Connecting || IsConnected(bayeuxClientState))
            {
                bayeuxClientState.Send(_publishListener, messages);
                return true;
            }
            FailMessages(null, ObjectConverter.ToListOfIMessage(messages));
            return false;
        }

        private int PendingMessages
        {
            get
            {
                var value = _messageQueue.Count;

                var state = _bayeuxClientState;
                // ReSharper disable once PatternAlwaysOfType
                if (state.Transport is ClientTransport clientTransport)
                {
                    value += clientTransport.IsSending ? 1 : 0;
                }

                return value;
            }
        }

        /// <summary>
        /// Wait for send queue to be emptied
        /// </summary>
        /// <param name="timeoutMs"></param>
        /// <returns>true if queue is empty, false if timed out</returns>
        public bool WaitForEmptySendQueue(int timeoutMs)
        {
            if (PendingMessages == 0)
                return true;

            var start = DateTime.Now;

            while ((DateTime.Now - start).TotalMilliseconds < timeoutMs)
            {
                if (PendingMessages == 0)
                    return true;

                Thread.Sleep(100);
            }

            return false;
        }

        private IList<IMutableMessage> TakeMessages()
        {
            IList<IMutableMessage> queue = new List<IMutableMessage>(_messageQueue);
            _messageQueue.Clear();
            return queue;
        }

        public override void Disconnect()
        {
            UpdateBayeuxClientState(
                    delegate (BayeuxClientState oldState)
                    {
                        if (IsConnected(oldState))
                            return new DisconnectingState(this, oldState.Transport, oldState.ClientId);
                        return new DisconnectedState(this, oldState.Transport);
                    });
        }

        public void Abort()
        {
            UpdateBayeuxClientState(
                oldState => new AbortedState(this, oldState.Transport));
        }

        protected void ProcessHandshake(IMutableMessage handshake)
        {
            if (handshake.Successful)
            {
                handshake.TryGetValue(MessageFields.SupportedConnectionTypesField, out var serverTransportObject);
                var serverTransports = JsonConvert.DeserializeObject<IList<object>>(serverTransportObject.ToString());
                var negotiatedTransports = _transportRegistry.Negotiate(serverTransports, BayeuxVersion);
                var newTransport = negotiatedTransports.Count == 0 ? null : negotiatedTransports[0];
                if (newTransport == null)
                {
                    UpdateBayeuxClientState(
                        oldState => new DisconnectedState(this, oldState.Transport),
                            delegate
                            {
                                Receive(handshake);
                            });

                    // Signal the failure
                    var error = "405:c" + _transportRegistry.AllowedTransports + ",s" + serverTransports + ":no transport";

                    handshake.Successful = false;
                    handshake[MessageFields.ErrorField] = error;
                }
                else
                {
                    UpdateBayeuxClientState(
                            delegate (BayeuxClientState oldState)
                            {
                                if (newTransport != oldState.Transport)
                                {
                                    oldState.Transport.Reset();
                                    newTransport.Init();
                                }

                                var action = GetAdviceAction(handshake.Advice, MessageFields.ReconnectRetryValue);
                                if (MessageFields.ReconnectRetryValue.Equals(action))
                                    return new ConnectingState(this, oldState.HandshakeFields, handshake.Advice, newTransport, handshake.ClientId);
                                if (MessageFields.ReconnectNoneValue.Equals(action))
                                    return new DisconnectedState(this, oldState.Transport);

                                return null;
                            },
                            delegate
                            {
                                Receive(handshake);
                            });
                }
            }
            else
            {
                UpdateBayeuxClientState(
                        delegate (BayeuxClientState oldState)
                        {
                            var action = GetAdviceAction(handshake.Advice, MessageFields.ReconnectHandshakeValue);
                            if (MessageFields.ReconnectHandshakeValue.Equals(action) || MessageFields.ReconnectRetryValue.Equals(action))
                                return new RehandshakingState(this, oldState.HandshakeFields, oldState.Transport, oldState.NextBackoff());
                            if (MessageFields.ReconnectNoneValue.Equals(action))
                                return new DisconnectedState(this, oldState.Transport);
                            return null;
                        },
                        delegate
                        {
                            Receive(handshake);
                        });
            }
        }

        protected void ProcessConnect(IMutableMessage connect)
        {
            UpdateBayeuxClientState(
                    delegate (BayeuxClientState oldState)
                    {
                        var advice = connect.Advice ?? oldState.Advice;

                        var action = GetAdviceAction(advice, MessageFields.ReconnectRetryValue);
                        if (connect.Successful)
                        {
                            if (MessageFields.ReconnectRetryValue.Equals(action))
                                return new ConnectedState(this, oldState.HandshakeFields, advice, oldState.Transport, oldState.ClientId);
                            if (MessageFields.ReconnectNoneValue.Equals(action))
                                // This case happens when the connect reply arrives after a disconnect
                                // We do not go into a disconnected state to allow normal processing of the disconnect reply
                                return new DisconnectingState(this, oldState.Transport, oldState.ClientId);
                        }
                        else
                        {
                            if (MessageFields.ReconnectHandshakeValue.Equals(action))
                                return new RehandshakingState(this, oldState.HandshakeFields, oldState.Transport, 0);
                            if (MessageFields.ReconnectRetryValue.Equals(action))
                                return new UnconnectedState(this, oldState.HandshakeFields, advice, oldState.Transport, oldState.ClientId, oldState.NextBackoff());
                            if (MessageFields.ReconnectNoneValue.Equals(action))
                                return new DisconnectedState(this, oldState.Transport);
                        }

                        return null;
                    },
                delegate
                {
                    Receive(connect);
                });
        }

        protected void ProcessDisconnect(IMutableMessage disconnect)
        {
            UpdateBayeuxClientState(
                oldState => new DisconnectedState(this, oldState.Transport),
                    delegate
                    {
                        Receive(disconnect);
                    });
        }

        protected void ProcessMessage(IMutableMessage message)
        {
            Receive(message);
        }

        private string GetAdviceAction(IDictionary<string, object> advice, string defaultResult)
        {
            var action = defaultResult;
            if (advice != null && advice.ContainsKey(MessageFields.ReconnectField))
                action = ((string)advice[MessageFields.ReconnectField]);
            return action;
        }

        protected void ScheduleHandshake(int interval, int backoff)
        {
            ScheduleAction(SendHandshake, interval, backoff);
        }

        protected void ScheduleConnect(int interval, int backoff)
        {
            ScheduleAction(SendConnect, interval, backoff);
        }

        private readonly ConcurrentDictionary<int, Timer> _schedulingTimers = new ConcurrentDictionary<int, Timer>();
        private void ScheduleAction(Action action, int interval, int backoff)
        {
            Timer timer = null;

            timer = new Timer(delegate
            {
                action();
                // ReSharper disable once AccessToModifiedClosure
                if (timer == null) return;
                // ReSharper disable once AccessToModifiedClosure
                timer.Dispose();
                // ReSharper disable once AccessToModifiedClosure
                // ReSharper disable once UnusedVariable
                _schedulingTimers.TryRemove(timer.GetHashCode(), out var t);
            });

            // Hold on to a ref to the timer so it doesn't get garbage collected
            _schedulingTimers.AddOrUpdate(timer.GetHashCode(), timer, (k, v) => timer);

            var wait = interval + backoff;
            if (wait <= 0) wait = 1;
            timer.Change(wait, Timeout.Infinite);
        }

        public IList<string> AllowedTransports => _transportRegistry.AllowedTransports;

        public ICollection<string> KnownTransportNames => _transportRegistry.KnownTransports;

        public ITransport GetTransport(string transport)
        {
            return _transportRegistry.GetTransport(transport);
        }

        public void SetDebugEnabled(bool debug)
        {
            // todo
        }

        public bool IsDebugEnabled()
        {
            return false;
        }

        protected void Initialize()
        {
            var backoffIncrement = ObjectConverter.ToInt32(GetOption(BackoffIncrementOption), 1000);
            BackoffIncrement = backoffIncrement;

            var maxBackoff = ObjectConverter.ToInt32(GetOption(MaxBackoffOption), 30000);
            MaxBackoff = maxBackoff;
        }

        protected void Terminate()
        {
            var messages = TakeMessages();
            FailMessages(null, ObjectConverter.ToListOfIMessage(messages));
        }

        public object GetOption(string qualifiedName)
        {
            _options.TryGetValue(qualifiedName, out var obj);
            return obj;
        }

        public void SetOption(string qualifiedName, object val)
        {
            _options[qualifiedName] = val;
        }

        public ICollection<string> OptionNames => _options.Keys;

        public IDictionary<string, object> Options => _options;

        protected IMutableMessage NewMessage()
        {
            return new DictionaryMessage();
        }

        protected void EnqueueSend(IMutableMessage message)
        {
            if (CanSend())
            {
                IList<IMutableMessage> messages = new List<IMutableMessage>();
                messages.Add(message);
                SendMessages(messages);
            }
            else
            {
                _messageQueue.Enqueue(message);
            }
        }

        private bool CanSend()
        {
            return !IsDisconnected(_bayeuxClientState) && !Batching && !IsHandshaking(_bayeuxClientState);
        }

        protected void FailMessages(Exception x, IList<IMessage> messages)
        {
            foreach (var message in messages)
            {
                var failed = NewMessage();
                failed.Id = message.Id;
                failed.Successful = false;
                failed.Channel = message.Channel;
                failed["message"] = messages;
                if (x != null)
                    failed["exception"] = x;
                Receive(failed);
            }
        }

        public void OnSending(IList<IMessage> messages)
        {
        }

        public void OnMessages(IList<IMutableMessage> messages)
        {
        }

        public virtual void OnFailure(Exception x, IList<IMessage> messages)
        {
            Console.WriteLine("{0}", x.ToString());
        }

        private void UpdateBayeuxClientState(BayeuxClientStateUpdaterCreateDelegate create, BayeuxClientStateUpdaterPostCreateDelegate postCreate = null)
        {
            StateUpdateInProgressMutex.WaitOne();
            ++_stateUpdateInProgress;
            StateUpdateInProgressMutex.ReleaseMutex();

            var oldState = _bayeuxClientState;

            var newState = create(oldState);
            if (newState == null)
                throw new Exception();

            if (!oldState.IsUpdateableTo(newState))
            {
                return;
            }

            _bayeuxClientState = newState;

            postCreate?.Invoke();

            if (oldState.Type != newState.Type)
                newState.Enter(oldState.Type);

            newState.Execute();

            // Notify threads waiting in WaitFor()
            StateUpdateInProgressMutex.WaitOne();
            --_stateUpdateInProgress;

            if (_stateUpdateInProgress == 0)
                _stateChanged.Set();
            StateUpdateInProgressMutex.ReleaseMutex();
        }

        public string Dump()
        {
            return "";
        }

        public enum State
        {
            Invalid, Unconnected, Handshaking, Rehandshaking, Connecting, Connected, Disconnecting, Disconnected
        }

        private class PublishTransportListener : ITransportListener
        {
            protected readonly BayeuxClient BayeuxClient;

            public PublishTransportListener(BayeuxClient bayeuxClient)
            {
                BayeuxClient = bayeuxClient;
            }

            public void OnSending(IList<IMessage> messages)
            {
                BayeuxClient.OnSending(messages);
            }

            public void OnMessages(IList<IMutableMessage> messages)
            {
                BayeuxClient.OnMessages(messages);
                foreach (var message in messages)
                    ProcessMessage(message);
            }

            public void OnConnectException(Exception x, IList<IMessage> messages)
            {
                OnFailure(x, messages);
            }

            public void OnException(Exception x, IList<IMessage> messages)
            {
                OnFailure(x, messages);
            }

            public void OnExpire(IList<IMessage> messages)
            {
                OnFailure(new TimeoutException("expired"), messages);
            }

            public void OnProtocolError(string info, IList<IMessage> messages)
            {
                OnFailure(new ProtocolViolationException(info), messages);
            }

            protected virtual void ProcessMessage(IMutableMessage message)
            {
                BayeuxClient.ProcessMessage(message);
            }

            protected virtual void OnFailure(Exception x, IList<IMessage> messages)
            {
                BayeuxClient.OnFailure(x, messages);
                BayeuxClient.FailMessages(x, messages);
            }
        }

        private class HandshakeTransportListener : PublishTransportListener
        {
            public HandshakeTransportListener(BayeuxClient bayeuxClient)
                : base(bayeuxClient)
            {
            }

            protected override void OnFailure(Exception x, IList<IMessage> messages)
            {
                BayeuxClient.UpdateBayeuxClientState(
                    oldState => new RehandshakingState(BayeuxClient, oldState.HandshakeFields, oldState.Transport,
                        oldState.NextBackoff()));
                base.OnFailure(x, messages);
            }

            protected override void ProcessMessage(IMutableMessage message)
            {
                if (ChannelFields.MetaHandshake.Equals(message.Channel))
                    BayeuxClient.ProcessHandshake(message);
                else
                    base.ProcessMessage(message);
            }
        }

        private class ConnectTransportListener : PublishTransportListener
        {
            public ConnectTransportListener(BayeuxClient bayeuxClient)
                : base(bayeuxClient)
            {
            }

            protected override void OnFailure(Exception x, IList<IMessage> messages)
            {
                BayeuxClient.UpdateBayeuxClientState(
                    oldState => new UnconnectedState(BayeuxClient, oldState.HandshakeFields, oldState.Advice,
                        oldState.Transport, oldState.ClientId, oldState.NextBackoff()));
                base.OnFailure(x, messages);
            }

            protected override void ProcessMessage(IMutableMessage message)
            {
                if (ChannelFields.MetaConnect.Equals(message.Channel))
                    BayeuxClient.ProcessConnect(message);
                else
                    base.ProcessMessage(message);
            }
        }

        private class DisconnectTransportListener : PublishTransportListener
        {
            public DisconnectTransportListener(BayeuxClient bayeuxClient)
                : base(bayeuxClient)
            {
            }

            protected override void OnFailure(Exception x, IList<IMessage> messages)
            {
                BayeuxClient.UpdateBayeuxClientState(
                    oldState => new DisconnectedState(BayeuxClient, oldState.Transport));
                base.OnFailure(x, messages);
            }

            protected override void ProcessMessage(IMutableMessage message)
            {
                if (ChannelFields.MetaDisconnect.Equals(message.Channel))
                    BayeuxClient.ProcessDisconnect(message);
                else
                    base.ProcessMessage(message);
            }
        }

        public class BayeuxClientChannel : AbstractSessionChannel
        {
            protected BayeuxClient BayeuxClient;

            public BayeuxClientChannel(BayeuxClient bayeuxClient, ChannelId channelId)
                : base(channelId)
            {
                BayeuxClient = bayeuxClient;
            }

            public override IClientSession Session => this as IClientSession;


            protected override void SendSubscribe()
            {
                var message = BayeuxClient.NewMessage();
                message.Channel = ChannelFields.MetaSubscribe;
                message[MessageFields.SubscriptionField] = Id;
                BayeuxClient.EnqueueSend(message);
            }

            protected override void SendUnSubscribe()
            {
                var message = BayeuxClient.NewMessage();
                message.Channel = ChannelFields.MetaUnsubscribe;
                message[MessageFields.SubscriptionField] = Id;
                BayeuxClient.EnqueueSend(message);
            }

            public override void Publish(object data)
            {
                Publish(data, null);
            }

            public override void Publish(object data, string messageId)
            {
                var message = BayeuxClient.NewMessage();
                message.Channel = Id;
                message.Data = data;
                if (messageId != null)
                    message.Id = messageId;
                BayeuxClient.EnqueueSend(message);
            }
        }

        private delegate BayeuxClientState BayeuxClientStateUpdaterCreateDelegate(BayeuxClientState oldState);
        private delegate void BayeuxClientStateUpdaterPostCreateDelegate();

        public abstract class BayeuxClientState
        {
            public State StateType { get; set; }
            public IDictionary<string, object> HandshakeFields;
            public IDictionary<string, object> Advice;
            public ClientTransport Transport;
            public string ClientId;
            public int Backoff;
            protected BayeuxClient BayeuxClient;

            protected BayeuxClientState(BayeuxClient bayeuxClient, State type, IDictionary<string, object> handshakeFields,
                    IDictionary<string, object> advice, ClientTransport transport, string clientId, int backoff)
            {
                BayeuxClient = bayeuxClient;
                StateType = type;
                HandshakeFields = handshakeFields;
                Advice = advice;
                Transport = transport;
                ClientId = clientId;
                Backoff = backoff;
            }

            public int Interval
            {
                get
                {
                    var result = 0;
                    if (Advice != null && Advice.ContainsKey(MessageFields.IntervalField))
                        result = ObjectConverter.ToInt32(Advice[MessageFields.IntervalField], result);

                    return result;
                }
            }

            public void Send(ITransportListener listener, IMutableMessage message)
            {
                IList<IMutableMessage> messages = new List<IMutableMessage>();
                messages.Add(message);
                Send(listener, messages);
            }

            public void Send(ITransportListener listener, IList<IMutableMessage> messages)
            {
                foreach (var message in messages)
                {
                    if (message.Id == null)
                        message.Id = BayeuxClient.NewMessageId();
                    if (ClientId != null)
                        message.ClientId = ClientId;

                    if (!BayeuxClient.ExtendSend(message))
                        messages.Remove(message);
                }
                if (messages.Count > 0)
                {
                    Transport.Send(listener, messages);
                }
            }

            public int NextBackoff()
            {
                return Math.Min(Backoff + BayeuxClient.BackoffIncrement, BayeuxClient.MaxBackoff);
            }

            public abstract bool IsUpdateableTo(BayeuxClientState newState);

            public virtual void Enter(State oldState)
            {
            }

            public abstract void Execute();

            public State Type => StateType;

            public override string ToString()
            {
                return StateType.ToString();
            }
        }

        private class DisconnectedState : BayeuxClientState
        {
            public DisconnectedState(BayeuxClient bayeuxClient, ClientTransport transport)
                : base(bayeuxClient, State.Disconnected, null, null, transport, null, 0)
            {
            }

            public override bool IsUpdateableTo(BayeuxClientState newState)
            {
                return newState.StateType == State.Handshaking;
            }

            public override void Execute()
            {
                Transport.Reset();
                BayeuxClient.Terminate();
            }
        }

        private class AbortedState : DisconnectedState
        {
            public AbortedState(BayeuxClient bayeuxClient, ClientTransport transport)
                : base(bayeuxClient, transport)
            {
            }

            public override void Execute()
            {
                Transport.Abort();
                base.Execute();
            }
        }

        private class HandshakingState : BayeuxClientState
        {
            public HandshakingState(BayeuxClient bayeuxClient, IDictionary<string, object> handshakeFields, ClientTransport transport)
                : base(bayeuxClient, State.Handshaking, handshakeFields, null, transport, null, 0)
            {
            }

            public override bool IsUpdateableTo(BayeuxClientState newState)
            {
                return newState.StateType == State.Rehandshaking ||
                    newState.StateType == State.Connecting ||
                    newState.StateType == State.Disconnected;
            }

            public override void Enter(State oldState)
            {
                // Always reset the subscriptions when a handshake has been requested.
                BayeuxClient.ResetSubscriptions();
            }

            public override void Execute()
            {
                // The state could change between now and when SendHandshake() runs;
                // in this case the handshake message will not be sent and will not
                // be failed, because most probably the client has been disconnected.
                BayeuxClient.SendHandshake();
            }
        }

        private class RehandshakingState : BayeuxClientState
        {
            public RehandshakingState(BayeuxClient bayeuxClient, IDictionary<string, object> handshakeFields, ClientTransport transport, int backoff)
                : base(bayeuxClient, State.Rehandshaking, handshakeFields, null, transport, null, backoff)
            {
            }

            public override bool IsUpdateableTo(BayeuxClientState newState)
            {
                return newState.StateType == State.Connecting ||
                    newState.StateType == State.Rehandshaking ||
                    newState.StateType == State.Disconnected;
            }

            public override void Enter(State oldState)
            {
                // Reset the subscriptions if this is not a failure from a requested handshake.
                // Subscriptions may be queued after requested handshakes.
                if (oldState != State.Handshaking)
                {
                    // Reset subscriptions if not queued after initial handshake
                    BayeuxClient.ResetSubscriptions();
                }
            }

            public override void Execute()
            {
                BayeuxClient.ScheduleHandshake(Interval, Backoff);
            }
        }

        private class ConnectingState : BayeuxClientState
        {
            public ConnectingState(BayeuxClient bayeuxClient, IDictionary<string, object> handshakeFields, IDictionary<string, object> advice, ClientTransport transport, string clientId)
                : base(bayeuxClient, State.Connecting, handshakeFields, advice, transport, clientId, 0)
            {
            }

            public override bool IsUpdateableTo(BayeuxClientState newState)
            {
                return newState.StateType == State.Connected ||
                    newState.StateType == State.Unconnected ||
                    newState.StateType == State.Rehandshaking ||
                    newState.StateType == State.Disconnecting ||
                    newState.StateType == State.Disconnected;
            }

            public override void Execute()
            {
                // Send the messages that may have queued up before the handshake completed
                BayeuxClient.SendBatch();
                BayeuxClient.ScheduleConnect(Interval, Backoff);
            }
        }

        private class ConnectedState : BayeuxClientState
        {
            public ConnectedState(BayeuxClient bayeuxClient, IDictionary<string, object> handshakeFields, IDictionary<string, object> advice, ClientTransport transport, string clientId)
                : base(bayeuxClient, State.Connected, handshakeFields, advice, transport, clientId, 0)
            {
            }

            public override bool IsUpdateableTo(BayeuxClientState newState)
            {
                return newState.StateType == State.Connected ||
                    newState.StateType == State.Unconnected ||
                    newState.StateType == State.Rehandshaking ||
                    newState.StateType == State.Disconnecting ||
                    newState.StateType == State.Disconnected;
            }

            public override void Execute()
            {
                BayeuxClient.ScheduleConnect(Interval, Backoff);
            }
        }

        private class UnconnectedState : BayeuxClientState
        {
            public UnconnectedState(BayeuxClient bayeuxClient, IDictionary<string, object> handshakeFields, IDictionary<string, object> advice, ClientTransport transport, string clientId, int backoff)
                : base(bayeuxClient, State.Unconnected, handshakeFields, advice, transport, clientId, backoff)
            {
            }

            public override bool IsUpdateableTo(BayeuxClientState newState)
            {
                return newState.StateType == State.Connected ||
                    newState.StateType == State.Unconnected ||
                    newState.StateType == State.Rehandshaking ||
                    newState.StateType == State.Disconnected;
            }

            public override void Execute()
            {
                BayeuxClient.ScheduleConnect(Interval, Backoff);
            }
        }

        private class DisconnectingState : BayeuxClientState
        {
            public DisconnectingState(BayeuxClient bayeuxClient, ClientTransport transport, string clientId)
                : base(bayeuxClient, State.Disconnecting, null, null, transport, clientId, 0)
            {
            }

            public override bool IsUpdateableTo(BayeuxClientState newState)
            {
                return newState.StateType == State.Disconnected;
            }

            public override void Execute()
            {
                var message = BayeuxClient.NewMessage();
                message.Channel = ChannelFields.MetaDisconnect;
                Send(BayeuxClient._disconnectListener, message);
            }
        }
    }
}
