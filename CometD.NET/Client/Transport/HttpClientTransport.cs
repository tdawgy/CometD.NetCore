using System.Collections.Generic;
using System.Net;

namespace CometD.NetCore.Client.Transport
{
    public abstract class HttpClientTransport : ClientTransport
    {
        public string Url { protected get; set; }
        public CookieCollection CookieCollection { protected get; set; }
        public WebHeaderCollection HeaderCollection { protected get; set; }

        protected HttpClientTransport(string name, IDictionary<string, object> options)
            : base(name, options)
        {
        }

        protected internal void AddCookie(Cookie cookie)
        {
            var cookieCollection = CookieCollection;
            cookieCollection?.Add(cookie);
        }
    }
}
