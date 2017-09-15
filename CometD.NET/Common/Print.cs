using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CometD.NetCore.Bayeux;

namespace Cometd.Common
{
    class Print
    {
        public static String List(IList<String> L)
        {
            var s = "";
            foreach (var e in L) s += " '" + e + "'";
            return s;
        }

        public static String Dictionary(IDictionary<String, Object> D)
        {
            if (D == null) return " (null)";
            if (!(D is IDictionary<String, Object>)) return " (invalid)";
            var s = "";
            foreach (var kvp in D)
            {
                s += " '" + kvp.Key + ":";
                if (kvp.Value is IDictionary<String, Object>)
                    s += Dictionary(kvp.Value as IDictionary<String, Object>);
                else
                    s += kvp.Value.ToString();
                s += "'";
            }
            return s;
        }

        public static String Messages(IList<IMessage> M)
        {
            if (M == null) return " (null)";
            if (!(M is IList<IMessage>)) return " (invalid)";
            var s = "[";
            foreach (var m in M)
            {
                s += " " + m;
            }
            s += " ]";
            return s;
        }

        public static String Messages(IList<IMutableMessage> M)
        {
            if (M == null) return " (null)";
            if (!(M is IList<IMutableMessage>)) return " (invalid)";
            var s = "[";
            foreach (var m in M)
            {
                s += " " + m;
            }
            s += " ]";
            return s;
        }
    }
}
