using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Cometd.Common;
using CometD.NetCore.Bayeux;
using CometD.NetCore.Common;
using Newtonsoft.Json;

namespace CometD.NetCore.Client.Transport
{
    public class LongPollingTransport : HttpClientTransport
    {
        private List<TransportExchange> _exchanges = new List<TransportExchange>();
        private bool _appendMessageType;

        public LongPollingTransport(IDictionary<String, Object> options)
            : base("long-polling", options)
        {
        }

        public override bool accept(String bayeuxVersion)
        {
            return true;
        }

        public override void init()
        {
            base.init();
            var uriRegex = new Regex("(^https?://(([^:/\\?#]+)(:(\\d+))?))?([^\\?#]*)(.*)?");
            var uriMatch = uriRegex.Match(getURL());
            if (uriMatch.Success)
            {
                var afterPath = uriMatch.Groups[7].ToString();
                _appendMessageType = afterPath == null || afterPath.Trim().Length == 0;
            }
        }

        public override void abort()
        {
            lock (this)
            {
                foreach (var exchange in _exchanges)
                    exchange.Abort();

                _exchanges.Clear();
            }
        }

        public override void reset()
        {
        }

        public class LongPollingRequest
        {
            ITransportListener listener;
            IList<IMutableMessage> messages;
            HttpWebRequest request;
            public TransportExchange exchange;

            public LongPollingRequest(ITransportListener _listener, IList<IMutableMessage> _messages,
                    HttpWebRequest _request)
            {
                listener = _listener;
                messages = _messages;
                request = _request;
            }

            public void send()
            {
                try
                {
                    request.BeginGetRequestStream(GetRequestStreamCallback, exchange);
                }
                catch (Exception e)
                {
                    exchange.Dispose();
                    listener.onException(e, ObjectConverter.ToListOfIMessage(messages));
                }
            }
        }

        private List<LongPollingRequest> transportQueue = new List<LongPollingRequest>();
        private HashSet<LongPollingRequest> transmissions = new HashSet<LongPollingRequest>();

        private void performNextRequest()
        {
            var ok = false;
            LongPollingRequest nextRequest = null;

            lock (this)
            {
                if (transportQueue.Count > 0 && transmissions.Count <= 1)
                {
                    ok = true;
                    nextRequest = transportQueue[0];
                    transportQueue.Remove(nextRequest);
                    transmissions.Add(nextRequest);
                }
            }

            if (ok)
            {
                nextRequest?.send();
            }
        }

        public void addRequest(LongPollingRequest request)
        {
            lock (this)
            {
                transportQueue.Add(request);
            }

            performNextRequest();
        }

        public void removeRequest(LongPollingRequest request)
        {
            lock (this)
            {
                transmissions.Remove(request);
            }

            performNextRequest();
        }

        public override void send(ITransportListener listener, IList<IMutableMessage> messages)
        {
            var url = getURL();

            if (_appendMessageType && messages.Count == 1 && messages[0].Meta)
            {
                var type = messages[0].Channel.Substring(Channel_Fields.META.Length);
                if (url.EndsWith("/"))
                    url = url.Substring(0, url.Length - 1);
                url += type;
            }

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json;charset=UTF-8";

            if (request.CookieContainer == null)
                request.CookieContainer = new CookieContainer();
            request.CookieContainer.Add(request.RequestUri, getCookieCollection());

            var content = JsonConvert.SerializeObject(ObjectConverter.ToListOfDictionary(messages));

            var longPollingRequest = new LongPollingRequest(listener, messages, request);

            var exchange = new TransportExchange(this, listener, messages, longPollingRequest);
            exchange.content = content;
            exchange.request = request;
            lock (this)
            {
                _exchanges.Add(exchange);
            }

            longPollingRequest.exchange = exchange;
            addRequest(longPollingRequest);
        }

        public override bool isSending
        {
            get
            {
                lock (this)
                {
                    if (transportQueue.Count > 0)
                        return true;

                    foreach (var transmission in transmissions)
                        if (transmission.exchange.isSending)
                            return true;

                    return false;
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
                using (var postStream = exchange.request.EndGetRequestStream(asynchronousResult))
                {
                    // Convert the string into a byte array.
                    var byteArray = Encoding.UTF8.GetBytes(exchange.content);
                    //Console.WriteLine("Sending message(s): {0}", exchange.content);

                    // Write to the request stream.
                    postStream.WriteAsync(byteArray, 0, exchange.content.Length);
                }

                // Start the asynchronous operation to get the response
                exchange.listener.onSending(ObjectConverter.ToListOfIMessage(exchange.messages));
                var result = exchange.request.BeginGetResponse(GetResponseCallback, exchange);

                long timeout = 120000;
                ThreadPool.RegisterWaitForSingleObject(result.AsyncWaitHandle, TimeoutCallback, exchange, timeout, true);

                exchange.isSending = false;
            }
            catch (Exception e)
            {
                exchange.request?.Abort();
                exchange.Dispose();
                exchange.listener.onException(e, ObjectConverter.ToListOfIMessage(exchange.messages));
            }
        }

        private static void GetResponseCallback(IAsyncResult asynchronousResult)
        {
            var exchange = (TransportExchange)asynchronousResult.AsyncState;

            try
            {
                // End the operation
                string responseString;
                using (var response = (HttpWebResponse)exchange.request.EndGetResponse(asynchronousResult))
                {
                    using (var streamResponse = response.GetResponseStream())
                    {
                        using (var streamRead = new StreamReader(streamResponse))
                            responseString = streamRead.ReadToEnd();
                    }

                    if (response.Cookies != null)
                        foreach (Cookie cookie in response.Cookies)
                            exchange.AddCookie(cookie);
                }
                exchange.messages = DictionaryMessage.parseMessages(responseString);

                exchange.listener.onMessages(exchange.messages);
                exchange.Dispose();
            }
            catch (Exception e)
            {
                exchange.listener.onException(e, ObjectConverter.ToListOfIMessage(exchange.messages));
                exchange.Dispose();
            }
        }

        // From http://msdn.microsoft.com/en-us/library/system.net.httpwebrequest.begingetresponse.aspx
        // Abort the request if the timer fires.
        private static void TimeoutCallback(object state, bool timedOut)
        {
            if (timedOut)
            {
                if (state is TransportExchange exchange)
                {
                    exchange.request?.Abort();
                    exchange.Dispose();
                }
            }
        }

        public class TransportExchange
        {
            private LongPollingTransport parent;
            public String content;
            public HttpWebRequest request;
            public ITransportListener listener;
            public IList<IMutableMessage> messages;
            public LongPollingRequest lprequest;
            public bool isSending;

            public TransportExchange(LongPollingTransport _parent, ITransportListener _listener, IList<IMutableMessage> _messages,
                    LongPollingRequest _lprequest)
            {
                parent = _parent;
                listener = _listener;
                messages = _messages;
                request = null;
                lprequest = _lprequest;
                isSending = true;
            }

            public void AddCookie(Cookie cookie)
            {
                parent.addCookie(cookie);
            }

            public void Dispose()
            {
                parent.removeRequest(lprequest);
                lock (parent)
                    parent._exchanges.Remove(this);
            }

            public void Abort()
            {
                request?.Abort();
            }
        }
    }
}
