using CometD.NetCore.Bayeux;
using System.Collections.Generic;

namespace CometD.NetCore.Common
{
    public class AbstractTransport : ITransport
    {
        protected IDictionary<string, object> Options;
        protected string[] Prefix;

        public AbstractTransport(string name, IDictionary<string, object> options)
        {
            Name = name;
            Options = options ?? new Dictionary<string, object>();
            Prefix = new string[0];
        }

        public string Name { get; }

        public object GetOption(string name)
        {
            Options.TryGetValue(name, out var value);

            string prefix = null;

            foreach (var segment in Prefix)
            {
                prefix = prefix == null ? segment : (prefix + "." + segment);
                var key = prefix + "." + name;

                if (Options.ContainsKey(key))
                    value = key;
            }

            return value;
        }

        public void SetOption(string name, object value)
        {
            var prefix = OptionPrefix;
            Options.Add(prefix == null ? name : (prefix + "." + name), value);
        }

        public ICollection<string> OptionNames
        {
            get
            {
                var names = new HashSet<string>();
                foreach (var name in Options.Keys)
                {
                    var lastDot = name.LastIndexOf('.');
                    names.Add(lastDot >= 0 ? name.Substring(lastDot + 1) : name);
                }
                return names;
            }
        }

        public string OptionPrefix
        {
            get
            {
                string prefix = null;
                foreach (var segment in Prefix)
                    prefix = prefix == null ? segment : (prefix + "." + segment);

                return prefix;
            }
            set => Prefix = value.Split('.');
        }

        public string GetOption(string option, string dftValue)
        {
            var value = GetOption(option);
            return ObjectConverter.ToString(value, dftValue);
        }

        public long GetOption(string option, long dftValue)
        {
            var value = GetOption(option);
            return ObjectConverter.ToInt64(value, dftValue);
        }

        public int GetOption(string option, int dftValue)
        {
            var value = GetOption(option);
            return ObjectConverter.ToInt32(value, dftValue);
        }

        public bool GetOption(string option, bool dftValue)
        {
            var value = GetOption(option);
            return ObjectConverter.ToBoolean(value, dftValue);
        }
    }
}
