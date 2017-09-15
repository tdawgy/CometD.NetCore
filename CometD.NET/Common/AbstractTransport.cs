using System;
using System.Collections.Generic;
using Cometd.Common;
using CometD.NetCore.Bayeux;

namespace CometD.NetCore.Common
{
    public class AbstractTransport : ITransport
    {
        private String _name;
        protected IDictionary<String, Object> _options;
        protected String[] _prefix;

        public AbstractTransport(String name, IDictionary<String, Object> options)
        {
            _name = name;
            _options = options == null ? new Dictionary<String, Object>() : options;
            _prefix = new String[0];
        }

        public String Name => _name;

        public Object getOption(String name)
        {
            _options.TryGetValue(name, out var value);

            String prefix = null;

            foreach (var segment in _prefix)
            {
                prefix = prefix == null ? segment : (prefix + "." + segment);
                var key = prefix + "." + name;

                if (_options.ContainsKey(key))
                    value = key;
            }

            return value;
        }

        public void setOption(String name, Object value)
        {
            var prefix = OptionPrefix;
            _options.Add(prefix == null ? name : (prefix + "." + name), value);
        }

        public ICollection<String> OptionNames
        {
            get
            {
                var names = new HashSet<String>();
                foreach (var name in _options.Keys)
                {
                    var lastDot = name.LastIndexOf('.');
                    if (lastDot >= 0)
                        names.Add(name.Substring(lastDot + 1));
                    else
                        names.Add(name);
                }
                return names;
            }
        }

        public String OptionPrefix
        {
            get
            {
                String prefix = null;
                foreach (var segment in _prefix)
                    prefix = prefix == null ? segment : (prefix + "." + segment);

                return prefix;
            }
            set
            {
                _prefix = value.Split('.');
            }
        }

        public String getOption(String option, String dftValue)
        {
            var value = getOption(option);
            return ObjectConverter.ToString(value, dftValue);
        }

        public long getOption(String option, long dftValue)
        {
            var value = getOption(option);
            return ObjectConverter.ToInt64(value, dftValue);
        }

        public int getOption(String option, int dftValue)
        {
            var value = getOption(option);
            return ObjectConverter.ToInt32(value, dftValue);
        }

        public bool getOption(String option, bool dftValue)
        {
            var value = getOption(option);
            return ObjectConverter.ToBoolean(value, dftValue);
        }
    }
}
