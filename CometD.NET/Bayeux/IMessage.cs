using System.Collections.Generic;

namespace CometD.NetCore.Bayeux
{
    /// <summary> <p>The Bayeux protocol exchange information by means of messages.</p>
    /// <p>This interface represents the API of a Bayeux message, and consists
    /// mainly of convenience methods to access the known fields of the message map.</p>
    /// <p>This interface comes in both an immutable and {@link Mutable mutable} versions.<br/>
    /// Mutability may be deeply enforced by an implementation, so that it is not correct
    /// to cast a passed Message, to a Message.Mutable, even if the implementation
    /// allows this.</p>
    /// 
    /// </summary>
    public class MessageFields
    {
        public const string ClientIdField = "clientId";
        public const string DataField = "data";
        public const string ChannelField = "channel";
        public const string IdField = "id";
        public const string ErrorField = "error";
        public const string TimestampField = "timestamp";
        public const string TransportField = "transport";
        public const string AdviceField = "advice";
        public const string SuccessfulField = "successful";
        public const string SubscriptionField = "subscription";
        public const string ExtField = "ext";
        public const string ConnectionTypeField = "connectionType";
        public const string VersionField = "version";
        public const string MinVersionField = "minimumVersion";
        public const string SupportedConnectionTypesField = "supportedConnectionTypes";
        public const string ReconnectField = "reconnect";
        public const string IntervalField = "interval";
        public const string ReconnectRetryValue = "retry";
        public const string ReconnectHandshakeValue = "handshake";
        public const string ReconnectNoneValue = "none";
    }

    public interface IMessage : IDictionary<string, object>
    {
        /// <summary> Convenience method to retrieve the {@link #ADVICE_FIELD}</summary>
        /// <returns> the advice of the message
        /// </returns>
        IDictionary<string, object> Advice { get; }

        /// <summary> Convenience method to retrieve the {@link #CHANNEL_FIELD}.
        /// Bayeux message always have a non null channel.
        /// </summary>
        /// <returns> the channel of the message
        /// </returns>
        string Channel { get; }

        /// <summary> Convenience method to retrieve the {@link #CHANNEL_FIELD}.
        /// Bayeux message always have a non null channel.
        /// </summary>
        /// <returns> the channel of the message
        /// </returns>
        ChannelId ChannelId { get; }

        /// <summary> Convenience method to retrieve the {@link #CLIENT_ID_FIELD}</summary>
        /// <returns> the client id of the message
        /// </returns>
        string ClientId { get; }

        /// <summary> Convenience method to retrieve the {@link #DATA_FIELD}</summary>
        /// <returns> the data of the message
        /// </returns>
        object Data { get; }

        /// <summary> A messages that has a meta channel is dubbed a "meta message".</summary>
        /// <returns> whether the channel's message is a meta channel
        /// </returns>
        bool Meta { get; }

        /// <summary> Convenience method to retrieve the {@link #SUCCESSFUL_FIELD}</summary>
        /// <returns> whether the message is successful
        /// </returns>
        bool Successful { get; }

        /// <returns> the data of the message as a map
        /// </returns>
        IDictionary<string, object> DataAsDictionary { get; }

        /// <summary> Convenience method to retrieve the {@link #EXT_FIELD}</summary>
        /// <returns> the ext of the message
        /// </returns>
        IDictionary<string, object> Ext { get; }

        /// <summary> Convenience method to retrieve the {@link #ID_FIELD}</summary>
        /// <returns> the id of the message
        /// </returns>
        string Id { get; }

        /// <returns> this message as a JSON string
        /// </returns>
        string Json { get; }
    }


    /// <summary> The mutable version of a {@link Message}</summary>
    public interface IMutableMessage : IMessage
    {
        /// <summary> Convenience method to retrieve the {@link #ADVICE_FIELD} and create it if it does not exist</summary>
        /// <param name="create">whether to create the advice field if it does not exist
        /// </param>
        /// <returns> the advice of the message
        /// </returns>
        IDictionary<string, object> GetAdvice(bool create);

        /// <summary> Convenience method to retrieve the {@link #DATA_FIELD} and create it if it does not exist</summary>
        /// <param name="create">whether to create the data field if it does not exist
        /// </param>
        /// <returns> the data of the message
        /// </returns>
        IDictionary<string, object> GetDataAsDictionary(bool create);

        /// <summary> Convenience method to retrieve the {@link #EXT_FIELD} and create it if it does not exist</summary>
        /// <param name="create">whether to create the ext field if it does not exist
        /// </param>
        /// <returns> the ext of the message
        /// </returns>
        IDictionary<string, object> GetExt(bool create);

        /// <summary>the channel of this message
        /// </summary>
        new string Channel { get; set; }

        /// <summary name="clientId">the client id of this message
        /// </summary>
        new string ClientId { get; set; }

        /// <summary>the data of this message
        /// </summary>
        new object Data { get; set; }

        /// <summary>the id of this message
        /// </summary>
        new string Id { get; set; }

        /// <summary>the successfulness of this message
        /// </summary>
        new bool Successful { get; set; }
    }
}
