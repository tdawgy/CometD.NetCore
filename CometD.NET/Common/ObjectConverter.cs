using System;
using System.Collections.Generic;
using CometD.NetCore.Bayeux;

namespace CometD.NetCore.Common
{
    public class ObjectConverter
    {
        public static string ToString(object obj, string defaultValue)
        {
            if (obj == null) return defaultValue;

            try
            {
                return obj.ToString();
            }
            catch (Exception)
            {
                return defaultValue;
            }
        }

        public static long ToInt64(object obj, long defaultValue)
        {
            if (obj == null) return defaultValue;

            try { return Convert.ToInt64(obj); }
            catch (Exception) { /* do nothing */ }

            try { return long.Parse(obj.ToString()); }
            catch (Exception) { /* do nothing */ }

            return defaultValue;
        }

        public static int ToInt32(object obj, int defaultValue)
        {
            if (obj == null) return defaultValue;

            try { return Convert.ToInt32(obj); }
            catch (Exception) { /* do nothing */ }

            try { return int.Parse(obj.ToString()); }
            catch (Exception) { /* do nothing */ }

            return defaultValue;
        }

        public static bool ToBoolean(object obj, bool defaultValue)
        {
            if (obj == null) return defaultValue;

            try { return Convert.ToBoolean(obj); }
            catch (Exception) { /* do nothing */ }

            try { return bool.Parse(obj.ToString()); }
            catch (Exception) { /* do nothing */ }

            return defaultValue;
        }

        public static IList<IMessage> ToListOfIMessage(IList<IMutableMessage> messages)
        {
            var r = new List<IMessage>();
            foreach (var message in messages)
            {
                r.Add(message);
            }
            return r;
        }

        public static IList<IDictionary<string, object>> ToListOfDictionary(IList<IMutableMessage> messages)
        {
            IList<IDictionary<string, object>> r = new List<IDictionary<string, object>>();

            foreach (var message in messages)
            {
                r.Add(message);
            }
            return r;
        }
    }
}
