using System.Collections.Generic;

namespace CometD.NetCore.Bayeux
{
    /// <summary> <p>A Bayeux channel is the primary message routing mechanism within Bayeux:
    /// both Bayeux clients and Bayeux server use channels to group listeners that
    /// are interested in receiving messages with that channel.</p>
    /// 
    /// <p>This interface is the common root for both the
    /// {@link org.cometd.bayeux.client.ClientSessionChannel client side} representation
    /// of a channel and the {@link org.cometd.bayeux.server.ServerChannel server side}
    /// representation of a channel.</p>
    /// 
    /// <p>Channels are identified with strings that look like paths (e.g. "/foo/bar")
    /// called "channel id".<br/>
    /// Meta channels have channel ids starting with "/meta/" and are reserved for the
    /// operation of they Bayeux protocol.<br/>
    /// Service channels have channel ids starting with "/service/" and are channels
    /// for which Publish is disabled, so that only server side listeners will receive
    /// the messages.</p>
    /// 
    /// <p>A channel id may also be specified with wildcards.<br/>
    /// For example "/meta/*" refers to all top level meta channels
    /// like "/meta/subscribe" or "/meta/handshake".<br/>
    /// The channel "/foo/**" is deeply wild and refers to all channels like "/foo/bar",
    /// "/foo/bar/bob" and "/foo/bar/wibble/bip".<br/>
    /// Wildcards can only be specified as last segment of a channel; therefore channel
    /// "/foo/&#42;/bar/** is an invalid channel.</p>
    /// 
    /// </summary>

    public class ChannelFields
    {
        public const string Meta = "/meta";
        public const string MetaHandshake = Meta + "/handshake";
        public const string MetaConnect = Meta + "/connect";
        public const string MetaSubscribe = Meta + "/subscribe";
        public const string MetaUnsubscribe = Meta + "/unsubscribe";
        public const string MetaDisconnect = Meta + "/disconnect";
    }

    public interface IChannel
    {
        /// <returns> The channel id as a string
        /// </returns>
        string Id { get; }

        /// <returns> The channel ID as a {@link ChannelId}
        /// </returns>
        ChannelId ChannelId { get; }

        /// <returns> true if the channel is a meta channel
        /// </returns>
        bool Meta { get; }

        /// <returns> true if the channel is a service channel
        /// </returns>
        bool Service { get; }

        /// <returns> true if the channel is wild.
        /// </returns>
        bool Wild { get; }

        /// <returns> true if the channel is deeply wild.
        /// </returns>
        bool DeepWild { get; }

        /// <summary> <p>Sets a named channel attribute value.</p>
        /// <p>Channel attributes are convenience data that allows arbitrary
        /// application data to be associated with a channel.</p>
        /// </summary>
        /// <param name="name">the attribute name
        /// </param>
        /// <param name="value">the attribute value
        /// </param>
        void SetAttribute(string name, object value);

        /// <summary> <p>Retrieves the value of named channel attribute.</p></summary>
        /// <param name="name">the name of the attribute
        /// </param>
        /// <returns> the attribute value or null if the attribute is not present
        /// </returns>
        object GetAttribute(string name);

        /// <returns> the list of channel attribute names.
        /// </returns>
        ICollection<string> AttributeNames { get; }

        /// <summary> <p>Removes a named channel attribute.</p></summary>
        /// <param name="name">the name of the attribute
        /// </param>
        /// <returns> the value of the attribute
        /// </returns>
        object RemoveAttribute(string name);
    }
}
