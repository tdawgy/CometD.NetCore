using CometD.NetCore.Bayeux;
using CometD.NetCore.Common;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CometD.NetCore.Client.Transport
{
    public class LongPollingTransport : HttpClientTransport
    {
        private readonly List<TransportExchange> _exchanges = new List<TransportExchange>();
        private bool _appendMessageType;

        public LongPollingTransport(IDictionary<string, object> options)
            : base("long-polling", options)
        {
        }

        public override bool Accept(string bayeuxVersion)
        {
            return true;
        }

        public override void Init()
        {
            base.Init();
            var uriRegex = new Regex("(^https?://(([^:/\\?#]+)(:(\\d+))?))?([^\\?#]*)(.*)?");
            var uriMatch = uriRegex.Match(Url);

            if (!uriMatch.Success) return;

            var afterPath = uriMatch.Groups[7].ToString();
            _appendMessageType = afterPath == null || afterPath.Trim().Length == 0;
        }

        public override void Abort()
        {
            lock (this)
            {
                foreach (var exchange in _exchanges)
                    exchange.Abort();

                _exchanges.Clear();
            }
        }

        public override void Reset()
        {
        }

        public class LongPollingRequest
        {
            private readonly ITransportListener _listener;
            private readonly IList<IMutableMessage> _messages;
            private readonly HttpWebRequest _request;
            public TransportExchange Exchange { get; set; }

            public LongPollingRequest(ITransportListener listener, IList<IMutableMessage> messages,
                    HttpWebRequest request)
            {
                _listener = listener;
                _messages = messages;
                _request = request;
            }

            public void Send()
            {
                try
                {
                    _request.BeginGetRequestStream(GetRequestStreamCallback, Exchange);
                }
                catch (Exception e)
                {
                    Exchange.Dispose();
                    _listener.OnException(e, ObjectConverter.ToListOfIMessage(_messages));
                }
            }
        }

        private readonly List<LongPollingRequest> _transportQueue = new List<LongPollingRequest>();
        private readonly HashSet<LongPollingRequest> _transmissions = new HashSet<LongPollingRequest>();

        private void PerformNextRequest()
        {
            var ok = false;
            LongPollingRequest nextRequest = null;

            lock (this)
            {
                if (_transportQueue.Count > 0 && _transmissions.Count <= 1)
                {
                    ok = true;
                    nextRequest = _transportQueue[0];
                    _transportQueue.Remove(nextRequest);
                    _transmissions.Add(nextRequest);
                }
            }

            if (ok)
            {
                nextRequest?.Send();
            }
        }

        public void AddRequest(LongPollingRequest request)
        {
            lock (this)
            {
                _transportQueue.Add(request);
            }

            PerformNextRequest();
        }

        public void RemoveRequest(LongPollingRequest request)
        {
            lock (this)
            {
                _transmissions.Remove(request);
            }

            PerformNextRequest();
        }

        public override void Send(ITransportListener listener, IList<IMutableMessage> messages)
        {
            var url = Url;

            if (_appendMessageType && messages.Count == 1 && messages[0].Meta)
            {
                var type = messages[0].Channel.Substring(ChannelFields.Meta.Length);
                if (url.EndsWith("/"))
                    url = url.Substring(0, url.Length - 1);
                url += type;
            }

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json;charset=UTF-8";

            if (request.CookieContainer == null)
                request.CookieContainer = new CookieContainer();

            if (CookieCollection != null)
                request.CookieContainer.Add(request.RequestUri, CookieCollection);

            if (request.Headers == null)
                request.Headers = new WebHeaderCollection();

            request.Headers.Add(HeaderCollection);

            var content = JsonConvert.SerializeObject(ObjectConverter.ToListOfDictionary(messages));

            var longPollingRequest = new LongPollingRequest(listener, messages, request);

            var exchange = new TransportExchange(this, listener, messages, longPollingRequest)
            {
                Content = content,
                Request = request
            };
            lock (this)
            {
                _exchanges.Add(exchange);
            }

            longPollingRequest.Exchange = exchange;
            AddRequest(longPollingRequest);
        }

        public override bool IsSending
        {
            get
            {
                lock (this)
                {
                    return _transportQueue.Count > 0 || _transmissions.Any(transmission => transmission.Exchange.IsSending);
                }
            }
        }

        // From http://msdn.microsoft.com/en-us/library/system.net.httpwebrequest.begingetrequeststream.aspx
        private static void GetRequestStreamCallback(IAsyncResult asynchronousResult)
        {
            var exchange = (TransportExchange)asynchronousResult.AsyncState;

            try
            {
                // End the operation
                using (var postStream = exchange.Request.EndGetRequestStream(asynchronousResult))
                {
                    // Convert the string into a byte array.
                    var byteArray = Encoding.UTF8.GetBytes(exchange.Content);
#if DEBUG
#if IgnoreMetaConnect
                    if (!exchange.Content.Contains("meta/connect")) // Don't write polling messages to the output
#endif
                    System.Diagnostics.Debug.WriteLine($"Sending message(s): {exchange.Content}");
#endif

                    // Write to the request stream.
                    postStream.WriteAsync(byteArray, 0, exchange.Content.Length);
                }

                // Start the asynchronous operation to get the response
                exchange.Listener.OnSending(ObjectConverter.ToListOfIMessage(exchange.Messages));
                var result = exchange.Request.BeginGetResponse(GetResponseCallback, exchange);

                const long timeout = 120000;
                ThreadPool.RegisterWaitForSingleObject(result.AsyncWaitHandle, TimeoutCallback, exchange, timeout, true);

                exchange.IsSending = false;
            }
            catch (Exception e)
            {
                exchange.Request?.Abort();
                exchange.Dispose();
                exchange.Listener.OnException(e, ObjectConverter.ToListOfIMessage(exchange.Messages));
            }
        }

        private static void GetResponseCallback(IAsyncResult asynchronousResult)
        {
            var exchange = (TransportExchange)asynchronousResult.AsyncState;

            try
            {
                // End the operation
                string responsestring;
                using (var response = (HttpWebResponse)exchange.Request.EndGetResponse(asynchronousResult))
                {
                    using (var streamResponse = response.GetResponseStream())
                    {
                        using (var streamRead = new StreamReader(streamResponse))
                            responsestring = streamRead.ReadToEnd();
                    }

                    if (response.Cookies != null)
                        foreach (Cookie cookie in response.Cookies)
                            exchange.AddCookie(cookie);
                }
                exchange.Messages = DictionaryMessage.ParseMessages(responsestring);

                exchange.Listener.OnMessages(exchange.Messages);
                exchange.Dispose();
            }
            catch (Exception e)
            {
                exchange.Listener.OnException(e, ObjectConverter.ToListOfIMessage(exchange.Messages));
                exchange.Dispose();
            }
        }

        // From http://msdn.microsoft.com/en-us/library/system.net.httpwebrequest.begingetresponse.aspx
        // Abort the request if the timer fires.
        private static void TimeoutCallback(object state, bool timedOut)
        {
            if (!timedOut) return;

            if (!(state is TransportExchange exchange)) return;

            exchange.Request?.Abort();
            exchange.Dispose();
        }

        public class TransportExchange
        {
            private readonly LongPollingTransport _parent;
            public string Content { get; set; }
            public HttpWebRequest Request { get; set; }
            public ITransportListener Listener { get; set; }
            public IList<IMutableMessage> Messages { get; set; }
            public LongPollingRequest LpRequest { get; set; }
            public bool IsSending { get; set; }

            public TransportExchange(LongPollingTransport parent, ITransportListener listener, IList<IMutableMessage> messages,
                    LongPollingRequest lprequest)
            {
                _parent = parent;
                Listener = listener;
                Messages = messages;
                Request = null;
                LpRequest = lprequest;
                IsSending = true;
            }

            public void AddCookie(Cookie cookie)
            {
                _parent.AddCookie(cookie);
            }

            public void Dispose()
            {
                _parent.RemoveRequest(LpRequest);
                lock (_parent)
                    _parent._exchanges.Remove(this);
            }

            public void Abort()
            {
                Request?.Abort();
            }
        }
    }
}
