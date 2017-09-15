using System;
using System.Collections.Generic;
using System.Text;

namespace CometD.NetCore.Bayeux
{
	/// <summary> Holder of a channel ID broken into path segments</summary>
	public class ChannelId
	{
		public const String WILD = "*";
		public const String DEEPWILD = "**";

		private readonly String _name;
		private readonly String[] _segments;
		private readonly int _wild;
	    private readonly String _parent;

		public ChannelId(String name)
		{
			_name = name;
			if (string.IsNullOrEmpty(name) || name[0] != '/' || "/".Equals(name))
				throw new ArgumentException(name);

		    if (name[name.Length - 1] == '/')
				name = name.Substring(0, (name.Length - 1) - (0));

			_segments = name.Substring(1).Split('/');
			var wilds = new String[_segments.Length + 1];

			var b = new StringBuilder();
			b.Append('/');

			for (var i = 0; i < _segments.Length; i++)
			{
				if (_segments[i] == null || _segments[i].Length == 0)
					throw new ArgumentException(name);

				if (i > 0)
					b.Append(_segments[i - 1]).Append('/');
				wilds[_segments.Length - i] = b + "**";
			}
			wilds[0] = b + "*";

			_parent = _segments.Length == 1 ? null : b.ToString().Substring(0, b.Length - 1);

			if (_segments.Length == 0)
				_wild = 0;
			else if (WILD.Equals(_segments[_segments.Length - 1]))
				_wild = 1;
			else if (DEEPWILD.Equals(_segments[_segments.Length - 1]))
				_wild = 2;
			else
				_wild = 0;

			if (_wild == 0)
			{
				Wilds = (new List<String>(wilds)).AsReadOnly();
			}
			else
				Wilds = (new List<String>()).AsReadOnly();
		}

		public bool Wild => _wild > 0;

	    public bool DeepWild => _wild > 1;

	    public bool isMeta()
		{
			return _segments.Length > 0 && "meta".Equals(_segments[0]);
		}

		public bool isService()
		{
			return _segments.Length > 0 && "service".Equals(_segments[0]);
		}

		public override bool Equals(Object obj)
		{
			if (this == obj)
				return true;

			if (obj is ChannelId id)
			{
			    if (id.depth() == depth())
				{
					for (var i = id.depth(); i-- > 0; )
						if (!id.getSegment(i).Equals(getSegment(i)))
							return false;
					return true;
				}
			}

			return false;
		}

		/* ------------------------------------------------------------ */
		/// <summary>Match channel IDs with wildcard support</summary>
		/// <param name="name">
		/// </param>
		/// <returns> true if this channelID matches the passed channel ID. If this channel is wild, then matching is wild.
		/// If the passed channel is wild, then it is the same as an equals call.
		/// </returns>
		public bool matches(ChannelId name)
		{
			if (name.Wild)
				return Equals(name);

			switch (_wild)
			{

				case 0:
					return Equals(name);

				case 1:
					if (name._segments.Length != _segments.Length)
						return false;
					for (var i = _segments.Length - 1; i-- > 0; )
						if (!_segments[i].Equals(name._segments[i]))
							return false;
					return true;


				case 2:
					if (name._segments.Length < _segments.Length)
						return false;
					for (var i = _segments.Length - 1; i-- > 0; )
						if (!_segments[i].Equals(name._segments[i]))
							return false;
					return true;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return _name.GetHashCode();
		}

		public override String ToString()
		{
			return _name;
		}

		public int depth()
		{
			return _segments.Length;
		}

		/* ------------------------------------------------------------ */
		public bool isAncestorOf(ChannelId id)
		{
			if (Wild || depth() >= id.depth())
				return false;

			for (var i = _segments.Length; i-- > 0; )
			{
				if (!_segments[i].Equals(id._segments[i]))
					return false;
			}
			return true;
		}

		/* ------------------------------------------------------------ */
		public bool isParentOf(ChannelId id)
		{
			if (Wild || depth() != id.depth() - 1)
				return false;

			for (var i = _segments.Length; i-- > 0; )
			{
				if (!_segments[i].Equals(id._segments[i]))
					return false;
			}
			return true;
		}

		/* ------------------------------------------------------------ */
		public String getParent()
		{
			return _parent;
		}

		public String getSegment(int i)
		{
			if (i > _segments.Length)
				return null;
			return _segments[i];
		}

		/* ------------------------------------------------------------ */
		/// <returns> The list of wilds channels that match this channel, or
		/// the empty list if this channel is already wild.
		/// </returns>
		public IList<String> Wilds { get; }

	    public static bool isMeta(String channelId)
		{
			return channelId != null && channelId.StartsWith("/meta/");
		}

		public static bool isService(String channelId)
		{
			return channelId != null && channelId.StartsWith("/service/");
		}
	}
}
