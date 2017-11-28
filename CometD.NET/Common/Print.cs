using CometD.NetCore.Bayeux;
using System.Collections.Generic;

namespace CometD.NetCore.Common
{
    public class Print
    {
        public static string List(IList<string> l)
        {
            var s = "";
            foreach (var e in l) s += " '" + e + "'";
            return s;
        }

        public static string Dictionary(IDictionary<string, object> d)
        {
            if (d == null) return " (null)";

            var s = "";
            foreach (var kvp in d)
            {
                s += " '" + kvp.Key + ":";
                if (kvp.Value is IDictionary<string, object>)
                    s += Dictionary(kvp.Value as IDictionary<string, object>);
                else
                    s += kvp.Value.ToString();
                s += "'";
            }
            return s;
        }

        public static string Messages(IList<IMessage> messages)
        {
            if (messages == null) return " (null)";

            var s = "[";
            foreach (var message in messages)
            {
                s += " " + message;
            }
            s += " ]";
            return s;
        }

        public static string Messages(IList<IMutableMessage> messages)
        {
            if (messages == null) return " (null)";

            var s = "[";
            foreach (var message in messages)
            {
                s += " " + message;
            }
            s += " ]";
            return s;
        }
    }
}
