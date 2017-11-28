namespace CometD.NetCore.Bayeux.Client
{
    /// <summary> <p>A client side channel representation.</p>
    /// <p>A {@link ClientSessionChannel} is scoped to a particular {@link ClientSession}
    /// that is obtained by a call to {@link ClientSession#GetChannel(String)}.</p>
    /// <p>Typical usage examples are:</p>
    /// <pre>
    /// clientSession.GetChannel("/foo/bar").subscribe(mySubscriptionListener);
    /// clientSession.GetChannel("/foo/bar").Publish("Hello");
    /// clientSession.GetChannel("/meta/*").addListener(myMetaChannelListener);
    /// </pre>
    /// 
    /// </summary>
    public interface IClientSessionChannel : IChannel
    {
        /// <returns> the client session associated with this channel
        /// </returns>
        IClientSession Session { get; }

        /// <param name="listener">the listener to add
        /// </param>
        void AddListener(IClientSessionChannelListener listener);

        /// <param name="listener">the listener to remove
        /// </param>
        void RemoveListener(IClientSessionChannelListener listener);

        /// <summary> Equivalent to {@link #Publish(Object, Object) Publish(data, null)}.</summary>
        /// <param name="data">the data to publish
        /// </param>
        void Publish(object data);

        /// <summary> Publishes the given {@code data} to this channel,
        /// optionally specifying the {@code messageId} to set on the
        /// publish message.
        /// </summary>
        /// <param name="data">the data to publish
        /// </param>
        /// <param name="messageId">the message id to set on the message, or null to let the
        /// implementation choose the message id.
        /// </param>
        void Publish(object data, string messageId);

        void Subscribe(IMessageListener listener);

        void Unsubscribe(IMessageListener listener);

        void Unsubscribe();

    }

    /// <inheritdoc />
    /// <summary> <p>Represents a listener on a {@link ClientSessionChannel}.</p>
    /// <p>Sub-interfaces specify the exact semantic of the listener.</p>
    /// </summary>
    public interface IClientSessionChannelListener : IBayeuxListener
    {
    }

    /// <inheritdoc />
    /// <summary> A listener for messages on a {@link ClientSessionChannel}.</summary>
    public interface IMessageListener : IClientSessionChannelListener
    {
        /// <summary> Callback invoked when a message is received on the given {@code channel}.</summary>
        /// <param name="channel">the channel that received the message
        /// </param>
        /// <param name="message">the message received
        /// </param>
        void OnMessage(IClientSessionChannel channel, IMessage message);
    }
}
