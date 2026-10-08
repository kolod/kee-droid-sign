using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KeeDroidSign.Core.Tests.Support
{
    /// <summary>
    /// In-memory stand-in for api.github.com. Routes are keyed by method and path with query
    /// (e.g. "GET /user"); unknown routes return 404. Every request is recorded.
    /// </summary>
    internal sealed class FakeGitHubHandler : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, Func<RecordedRequest, HttpResponseMessage>> _routes =
            new ConcurrentDictionary<string, Func<RecordedRequest, HttpResponseMessage>>(StringComparer.Ordinal);

        public List<RecordedRequest> Requests { get; } = new List<RecordedRequest>();

        public FakeGitHubHandler On(string method, string pathAndQuery, HttpStatusCode status,
            string json = null, IDictionary<string, string> headers = null)
        {
            _routes[Key(method, pathAndQuery)] = _ => Response(status, json, headers);
            return this;
        }

        public FakeGitHubHandler On(string method, string pathAndQuery, Func<RecordedRequest, HttpResponseMessage> responder)
        {
            _routes[Key(method, pathAndQuery)] = responder;
            return this;
        }

        public FakeGitHubHandler Throw(string method, string pathAndQuery, Exception exception)
        {
            _routes[Key(method, pathAndQuery)] = _ => throw exception;
            return this;
        }

        public static HttpResponseMessage Response(HttpStatusCode status, string json = null,
            IDictionary<string, string> headers = null)
        {
            var response = new HttpResponseMessage(status);
            if (json != null)
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            if (headers != null)
                foreach (var header in headers)
                    response.Headers.TryAddWithoutValidation(header.Key, header.Value);
            return response;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var recorded = new RecordedRequest
            {
                Method = request.Method.Method,
                Uri = request.RequestUri,
                Headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase),
                Body = request.Content == null ? null : await request.Content.ReadAsStringAsync().ConfigureAwait(false),
            };
            if (request.Content != null)
                foreach (var h in request.Content.Headers)
                    recorded.Headers[h.Key] = string.Join(", ", h.Value);

            lock (Requests)
                Requests.Add(recorded);

            if (_routes.TryGetValue(Key(recorded.Method, request.RequestUri.PathAndQuery), out var responder))
                return responder(recorded);

            return Response(HttpStatusCode.NotFound, "{\"message\":\"Not Found\"}");
        }

        private static string Key(string method, string pathAndQuery) => method.ToUpperInvariant() + " " + pathAndQuery;
    }

    internal sealed class RecordedRequest
    {
        public string Method { get; set; }
        public Uri Uri { get; set; }
        public Dictionary<string, string> Headers { get; set; }
        public string Body { get; set; }
    }
}
