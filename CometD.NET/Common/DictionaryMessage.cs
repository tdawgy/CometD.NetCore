using System;
using System.Collections.Generic;
using CometD.NetCore.Bayeux;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CometD.NetCore.Common
{
    [Serializable]
    public class DictionaryMessage : Dictionary<string, object>, IMutableMessage
    {
        public DictionaryMessage()
        {
        }

        public DictionaryMessage(IDictionary<string, object> message)
        {
            foreach (var kvp in message)
            {
                Add(kvp.Key, kvp.Value);
            }
        }

        public IDictionary<string, object> Advice
        {
            get
            {
                TryGetValue(MessageFields.AdviceField, out var advice);
                if (advice is string)
                {
                    advice = JsonConvert.DeserializeObject((string)advice);
                    this[MessageFields.AdviceField] = advice;
                }
                else if (advice is JObject)
                {
                    advice = JsonConvert.DeserializeObject<IDictionary<string, object>>(advice.ToString());
                }
                return (IDictionary<string, object>)advice;
            }
        }

        public string Channel
        {
            get
            {
                TryGetValue(MessageFields.ChannelField, out var obj);
                return (string)obj;
            }
            set => this[MessageFields.ChannelField] = value;
        }

        public ChannelId ChannelId => new ChannelId(Channel);

        public string ClientId
        {
            get
            {
                TryGetValue(MessageFields.ClientIdField, out var obj);
                return (string)obj;
            }
            set
            {
                this[MessageFields.ClientIdField] = value;
            }
        }

        public object Data
        {
            get
            {
                TryGetValue(MessageFields.DataField, out var obj);
                return obj;
            }
            set => this[MessageFields.DataField] = value;
        }

        public IDictionary<string, object> DataAsDictionary
        {
            get
            {
                TryGetValue(MessageFields.DataField, out var data);
                if (data is string)
                {
                    data = JsonConvert.DeserializeObject((string)data);
                    this[MessageFields.DataField] = data;
                }
                return (Dictionary<string, object>)data;
            }
        }

        public IDictionary<string, object> Ext
        {
            get
            {
                TryGetValue(MessageFields.ExtField, out var ext);
                if (ext is string)
                {
                    ext = JsonConvert.DeserializeObject((string)ext);
                    this[MessageFields.ExtField] = ext;
                }
                return (Dictionary<string, object>)ext;
            }
        }

        public string Id
        {
            get
            {
                TryGetValue(MessageFields.IdField, out var obj);
                return (string)obj;
            }
            set => this[MessageFields.IdField] = value;
        }

        public string Json => JsonConvert.SerializeObject(this);

        public IDictionary<string, object> GetAdvice(bool create)
        {
            var advice = Advice;
            if (create && advice == null)
            {
                advice = new Dictionary<string, object>();
                this[MessageFields.AdviceField] = advice;
            }
            return advice;
        }

        public IDictionary<string, object> GetDataAsDictionary(bool create)
        {
            var data = DataAsDictionary;
            if (create && data == null)
            {
                data = new Dictionary<string, object>();
                this[MessageFields.DataField] = data;
            }
            return data;
        }

        public IDictionary<string, object> GetExt(bool create)
        {
            var ext = Ext;
            if (create && ext == null)
            {
                ext = new Dictionary<string, object>();
                this[MessageFields.ExtField] = ext;
            }
            return ext;
        }

        public bool Meta => ChannelId.IsMeta(Channel);

        public bool Successful
        {
            get
            {
                TryGetValue(MessageFields.SuccessfulField, out var obj);
                return ObjectConverter.ToBoolean(obj, false);
            }
            set => this[MessageFields.SuccessfulField] = value;
        }

        public override string ToString()
        {
            return Json;
        }

        public static IList<IMutableMessage> ParseMessages(string content)
        {
            IList<IDictionary<string, object>> dictionaryList = null;
            try
            {
                dictionaryList = JsonConvert.DeserializeObject<IList<IDictionary<string, object>>>(content);
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception when parsing json {0}", e);
            }

            IList<IMutableMessage> messages = new List<IMutableMessage>();
            if (dictionaryList == null)
            {
                return messages;
            }

            foreach (var message in dictionaryList)
            {
                if (message != null)
                    messages.Add(new DictionaryMessage(message));
            }

            return messages;
        }
    }
}
