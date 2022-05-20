using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace GrpcWebSocketBridge.AspNetCore.Internal
{
    internal class BridgeHttpRequestFeature : IHttpRequestFeature
    {
        private readonly IHttpRequestFeature _origHttpRequestFeature;

        public BridgeHttpRequestFeature(IHttpRequestFeature origHttpRequestFeature)
        {
            _origHttpRequestFeature = origHttpRequestFeature;
            Headers = new HeaderDictionary(_origHttpRequestFeature.Headers.ToDictionary(k => k.Key, v => v.Value));
        }

        public string Protocol
        {
            get => _origHttpRequestFeature.Protocol;
            set => _origHttpRequestFeature.Protocol = value;
        }

        public string Scheme
        {
            get => _origHttpRequestFeature.Scheme;
            set => _origHttpRequestFeature.Scheme = value;
        }

        public string Method
        {
            get => _origHttpRequestFeature.Method;
            set => _origHttpRequestFeature.Method = value;
        }

        public string PathBase
        {
            get => _origHttpRequestFeature.PathBase;
            set => _origHttpRequestFeature.PathBase = value;
        }

        public string Path
        {
            get => _origHttpRequestFeature.Path;
            set => _origHttpRequestFeature.Path = value;
        }

        public string QueryString
        {
            get => _origHttpRequestFeature.QueryString;
            set => _origHttpRequestFeature.QueryString = value;
        }

        public string RawTarget
        {
            get => _origHttpRequestFeature.RawTarget;
            set => _origHttpRequestFeature.RawTarget = value;
        }

        public IHeaderDictionary Headers
        {
            get;
            set;
        }

        public Stream Body
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
    }
}
