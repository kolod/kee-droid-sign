using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KeeDroidSign.Core.GitHub;
using KeeDroidSign.Core.Keystore;
using KeeDroidSign.Core.Tests.Keystore;
using KeeDroidSign.Core.Tests.Support;
using Xunit;

namespace KeeDroidSign.Core.Tests.CrossCutting
{
    /// <summary>SC-007: no failure path exposes a password, token or keystore content.</summary>
    public class SecretLeakTests
    {
        private const string StorePassword = "leak-store-PW-7781";
        private const string KeyPassword = "leak-key-PW-9923";
        private const string Token = "github_pat_LEAKCHECK_0123456789";

        private static readonly string[] Secrets = { StorePassword, KeyPassword, Token };

        [Fact]
        public void KeyRequestValidationErrors_DoNotLeak()
        {
            var requests = new List<KeyRequest>();
            foreach (Action<KeyRequest> breakIt in new Action<KeyRequest>[]
            {
                r => r.Alias = "bad alias",
                r => r.KeySize = 1000,
                r => r.ValidityYears = 1,
                r => r.Subject = null,
                r => r.StorePassword = StorePassword + "\u0001",
                r => r.KeyPassword = KeyPassword + "\n",
            })
            {
                var request = Request();
                breakIt(request);
                requests.Add(request);
            }

            foreach (KeyRequest request in requests)
            {
                var ex = Assert.Throws<ArgumentException>(() => request.Validate());
                SecretLeakScanner.AssertNoLeak(Secrets, SecretLeakScanner.Collect(ex));
                SecretLeakScanner.AssertNoLeak(Secrets, request.ToString());
            }
        }

        [Fact]
        public async Task KeystoreReaderErrors_DoNotLeak()
        {
            SigningKeystore keystore = await new KeystoreGenerator().GenerateAsync(Request(), CancellationToken.None);
            var attempts = new Action[]
            {
                () => KeystoreReader.Open(keystore.Content, "upload", StorePassword + "x", KeyPassword),
                () => KeystoreReader.Open(keystore.Content, "upload", StorePassword, KeyPassword + "x"),
                () => KeystoreReader.Open(keystore.Content, "missing", StorePassword, KeyPassword),
                () => KeystoreReader.GetCertificate(new byte[10], "upload", StorePassword),
            };

            foreach (Action attempt in attempts)
            {
                var ex = Assert.ThrowsAny<KeeDroidSignException>(attempt);
                SecretLeakScanner.AssertNoLeak(Secrets, SecretLeakScanner.Collect(ex));
            }
            SecretLeakScanner.AssertNoLeak(new[] { StorePassword, KeyPassword, keystore.ToBase64() }, keystore.ToString());
        }

        [Fact]
        public async Task CancelledGeneration_DoesNotLeak()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => new KeystoreGenerator().GenerateAsync(Request(), cts.Token));
                SecretLeakScanner.AssertNoLeak(Secrets, SecretLeakScanner.Collect(ex));
            }
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData((HttpStatusCode)422)]
        [InlineData((HttpStatusCode)429)]
        [InlineData(HttpStatusCode.InternalServerError)]
        public async Task GitHubErrors_DoNotLeak(HttpStatusCode status)
        {
            // The fake server echoes the token in every body, like a misbehaving proxy might.
            string echo = "{\"message\":\"" + Token + "\"}";
            var repo = RepositoryTarget.Parse("octo/app");
            var handler = new FakeGitHubHandler()
                .On("GET", "/user", status, echo)
                .On("GET", "/repos/octo/app", status, echo)
                .On("GET", "/repos/octo/app/actions/secrets?per_page=100&page=1", status, echo)
                .On("GET", "/repos/octo/app/actions/secrets/public-key", status, echo)
                .On("PUT", "/repos/octo/app/actions/secrets/X", status, echo);

            using (var client = new GitHubClient(new GitHubCredential(Token), handler))
            {
                var texts = new List<string>();
                texts.Add((await client.ValidateTokenAsync(CancellationToken.None)).ToString());
                texts.Add((await client.ValidateTokenAsync(CancellationToken.None)).Message);
                texts.Add((await client.CheckRepositoryAccessAsync(repo, CancellationToken.None)).ToString());
                texts.Add((await client.PutSecretAsync(repo, "X", "ZW5j", "1", CancellationToken.None)).Reason);

                var list = await Assert.ThrowsAsync<GitHubApiException>(() => client.ListSecretNamesAsync(repo, CancellationToken.None));
                var key = await Assert.ThrowsAsync<GitHubApiException>(() => client.GetPublicKeyAsync(repo, CancellationToken.None));
                texts.AddRange(SecretLeakScanner.Collect(list));
                texts.AddRange(SecretLeakScanner.Collect(key));

                SecretLeakScanner.AssertNoLeak(Secrets, texts.ToArray());
            }
        }

        [Fact]
        public async Task GitHubNetworkErrors_DoNotLeak()
        {
            var handler = new FakeGitHubHandler()
                .Throw("GET", "/user", new HttpRequestException("proxy said " + Token));

            using (var client = new GitHubClient(new GitHubCredential(Token), handler))
            {
                TokenValidationResult result = await client.ValidateTokenAsync(CancellationToken.None);

                SecretLeakScanner.AssertNoLeak(Secrets, result.ToString(), result.Message);
            }
        }

        private static KeyRequest Request()
        {
            var request = KeystoreGeneratorTests.FastRequest();
            request.StorePassword = StorePassword;
            request.KeyPassword = KeyPassword;
            return request;
        }
    }
}
