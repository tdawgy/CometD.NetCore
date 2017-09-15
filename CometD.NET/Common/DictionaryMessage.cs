using System;
using System.Collections.Generic;
using Cometd.Common;
using CometD.NetCore.Bayeux;
using Newtonsoft.Json;

namespace CometD.NetCore.Common
{
    [Serializable]
    public class DictionaryMessage : Dictionary<String, Object>, IMutableMessage
    {
        public DictionaryMessage()
        {
        }

        public DictionaryMessage(IDictionary<String, Object> message)
        {
            foreach (var kvp in message)
            {
                Add(kvp.Key, kvp.Value);
            }
        }

        public IDictionary<String, Object> Advice
        {
            get
            {
                TryGetValue(Message_Fields.ADVICE_FIELD, out var advice);
                if (advice is String)
                {
                    advice = JsonConvert.DeserializeObject((String) advice);
                    this[Message_Fields.ADVICE_FIELD] = advice;
                }
                return (IDictionary<String, Object>)advice;
            }
        }

        public String Channel
        {
            get
            {
                TryGetValue(Message_Fields.CHANNEL_FIELD, out var obj);
                return (String)obj;
            }
            set => this[Message_Fields.CHANNEL_FIELD] = value;
        }

        public ChannelId ChannelId => new ChannelId(Channel);

        public String ClientId
        {
            get
            {
                TryGetValue(Message_Fields.CLIENT_ID_FIELD, out var obj);
                return (String)obj;
            }
            set
            {
                this[Message_Fields.CLIENT_ID_FIELD] = value;
            }

        }

        public Object Data
        {
            get
            {
                TryGetValue(Message_Fields.DATA_FIELD, out var obj);
                return obj;
            }
            set => this[Message_Fields.DATA_FIELD] = value;
        }

        public IDictionary<String, Object> DataAsDictionary
        {
            get
            {
                TryGetValue(Message_Fields.DATA_FIELD, out var data);
                if (data is String)
                {
                    data = JsonConvert.DeserializeObject((String) data);
                    this[Message_Fields.DATA_FIELD] = data;
                }
                return (Dictionary<String, Object>)data;
            }
        }

        public IDictionary<String, Object> Ext
        {
            get
            {
                TryGetValue(Message_Fields.EXT_FIELD, out var ext);
                if (ext is String)
                {
                    ext = JsonConvert.DeserializeObject((String) ext);
                    this[Message_Fields.EXT_FIELD] = ext;
                }
                return (Dictionary<String, Object>)ext;
            }
        }

        public String Id
        {
            get
            {
                TryGetValue(Message_Fields.ID_FIELD, out var obj);
                return (String)obj;
            }
            set => this[Message_Fields.ID_FIELD] = value;
        }

        public String JSON => JsonConvert.SerializeObject(this);

        public IDictionary<String, Object> getAdvice(bool create)
        {
            var advice = Advice;
            if (create && advice == null)
            {
                advice = new Dictionary<String, Object>();
                this[Message_Fields.ADVICE_FIELD] = advice;
            }
            return advice;
        }

        public IDictionary<String, Object> getDataAsDictionary(bool create)
        {
            var data = DataAsDictionary;
            if (create && data == null)
            {
                data = new Dictionary<String, Object>();
                this[Message_Fields.DATA_FIELD] = data;
            }
            return data;
        }

        public IDictionary<String, Object> getExt(bool create)
        {
            var ext = Ext;
            if (create && ext == null)
            {
                ext = new Dictionary<String, Object>();
                this[Message_Fields.EXT_FIELD] = ext;
            }
            return ext;
        }

        public bool Meta => ChannelId.isMeta(Channel);

        public bool Successful
        {
            get
            {
                TryGetValue(Message_Fields.SUCCESSFUL_FIELD, out var obj);
                return ObjectConverter.ToBoolean(obj, false);
            }
            set => this[Message_Fields.SUCCESSFUL_FIELD] = value;
        }

        public override String ToString()
        {
            return JSON;
        }

        public static IList<IMutableMessage> parseMessages(String content)
        {
            IList<IDictionary<String, Object>> dictionaryList = null;
            try
            {
                dictionaryList = JsonConvert.DeserializeObject<IList<IDictionary<String, Object>>>(content);
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
