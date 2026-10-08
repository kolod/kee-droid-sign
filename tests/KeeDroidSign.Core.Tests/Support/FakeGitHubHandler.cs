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
    /// (e.g. "GET /user"); prefix routes match any longer path; unknown routes return 404. Every
    /// request is recorded.
    /// </summary>
    internal sealed class FakeGitHubHandler : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, Func<RecordedRequest, HttpResponseMessage>> _routes =
            new ConcurrentDictionary<string, Func<RecordedRequest, HttpResponseMessage>>(StringComparer.Ordinal);
        private readonly List<KeyValuePair<string, Func<RecordedRequest, HttpResponseMessage>>> _prefixRoutes =
            new List<KeyValuePair<string, Func<RecordedRequest, HttpResponseMessage>>>();

        public List<RecordedRequest> Requests { get; } = new List<RecordedRequest>();

        /// <summary>Answers every <paramref name="method"/> request whose path starts with <paramref name="pathPrefix"/>.</summary>
        public FakeGitHubHandler OnPrefix(string method, string pathPrefix, Func<RecordedRequest, HttpResponseMessage> responder)
        {
            lock (_prefixRoutes)
                _prefixRoutes.Add(new KeyValuePair<string, Func<RecordedRequest, HttpResponseMessage>>(Key(method, pathPrefix), responder));
            return this;
        }

        /// <summary>
        /// A secrets collection at <paramref name="secretsPath"/> (repository or environment): list,
        /// public key, and PUT for any name (201 for new names, 204 for <paramref name="existing"/>).
        /// </summary>
        public FakeGitHubHandler SecretStore(string secretsPath, IEnumerable<string> existing, byte[] publicKey, string keyId,
            Func<string, HttpStatusCode> putStatus = null)
        {
            var names = existing.ToList();
            string list = "{\"total_count\":" + names.Count + ",\"secrets\":[" +
                string.Join(",", names.Select(n => "{\"name\":\"" + n + "\"}")) + "]}";
            var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

            On("GET", secretsPath + "?per_page=100&page=1", HttpStatusCode.OK, list);
            On("GET", secretsPath + "/public-key", HttpStatusCode.OK,
                "{\"key_id\":\"" + keyId + "\",\"key\":\"" + Convert.ToBase64String(publicKey) + "\"}");
            return OnPrefix("PUT", secretsPath + "/", r =>
            {
                string name = r.Uri.AbsolutePath.Split('/').Last();
                return Response(putStatus?.Invoke(name) ?? (set.Contains(name) ? HttpStatusCode.NoContent : HttpStatusCode.Created));
            });
        }

        /// <summary>Recorded requests with the method whose path starts with the prefix.</summary>
        public List<RecordedRequest> RequestsTo(string method, string pathPrefix)
        {
            lock (Requests)
                return Requests.Where(r => r.Method == method && r.Uri.AbsolutePath.StartsWith(pathPrefix, StringComparison.Ordinal)).ToList();
        }

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

            string key = Key(recorded.Method, request.RequestUri.PathAndQuery);
            lock (_prefixRoutes)
            {
                var match = _prefixRoutes.Where(p => key.StartsWith(p.Key, StringComparison.Ordinal))
                    .OrderByDescending(p => p.Key.Length)
                    .FirstOrDefault();
                if (match.Value != null)
                    return match.Value(recorded);
            }

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
