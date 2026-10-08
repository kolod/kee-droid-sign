using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KeeDroidSign.Core.GitHub;

namespace KeeDroidSign.LiveTests
{
    /// <summary>
    /// Test-only GitHub Actions calls (dispatch, poll, artifact download). Kept out of the plugin
    /// core on purpose (Constitution IV). Error messages contain status codes and URLs only.
    /// </summary>
    internal sealed class GitHubActionsHelper : IDisposable
    {
        // This API version returns the run ID from the dispatch call (200 instead of 204).
        private const string DispatchApiVersion = "2026-03-10";
        private const string ApiVersion = "2022-11-28";
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

        private readonly HttpClient _api;
        private readonly HttpClient _download;

        public GitHubActionsHelper(string token)
        {
            // Redirects are followed manually so the token is never sent to the storage host.
            _api = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri("https://api.github.com/"),
                Timeout = TimeSpan.FromSeconds(60),
            };
            _api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            _api.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            _api.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("KeeDroidSign-LiveTests", "1.0"));

            _download = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        }

        public async Task<DispatchedRun> DispatchAsync(RepositoryTarget repo, string workflowFile, string gitRef,
            string requestId, CancellationToken ct)
        {
            string body = Serialize(new DispatchDto { Ref = gitRef, Inputs = new DispatchInputsDto { RequestId = requestId } });
            using (var request = new HttpRequestMessage(HttpMethod.Post, Repo(repo) + "/actions/workflows/" + workflowFile + "/dispatches"))
            {
                request.Headers.Add("X-GitHub-Api-Version", DispatchApiVersion);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await _api.SendAsync(request, ct).ConfigureAwait(false))
                {
                    if (response.StatusCode != HttpStatusCode.OK)
                        throw new InvalidOperationException($"Workflow dispatch failed: HTTP {(int)response.StatusCode}.");
                    var dto = Deserialize<DispatchResultDto>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                    return new DispatchedRun(dto.WorkflowRunId, dto.HtmlUrl);
                }
            }
        }

        public async Task<RunDto> GetRunAsync(RepositoryTarget repo, long runId, CancellationToken ct)
        {
            return Deserialize<RunDto>(await GetStringAsync(Repo(repo) + "/actions/runs/" + runId, ct).ConfigureAwait(false));
        }

        public async Task<RunDto> WaitForCompletionAsync(RepositoryTarget repo, long runId, TimeSpan timeout, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                RunDto run = await GetRunAsync(repo, runId, ct).ConfigureAwait(false);
                if (run.Status == "completed")
                    return run;
                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException($"Workflow run {runId} did not complete within {timeout.TotalMinutes} minutes (status: {run.Status}).");
                await Task.Delay(PollInterval, ct).ConfigureAwait(false);
            }
        }

        public async Task<SigningReportDto> DownloadSigningReportAsync(RepositoryTarget repo, long runId, CancellationToken ct)
        {
            var artifacts = Deserialize<ArtifactListDto>(
                await GetStringAsync(Repo(repo) + "/actions/runs/" + runId + "/artifacts", ct).ConfigureAwait(false));
            ArtifactDto report = artifacts.Artifacts?.FirstOrDefault(a => a.Name == "signing-report")
                ?? throw new InvalidOperationException($"Run {runId} has no 'signing-report' artifact.");

            Uri location;
            using (var request = new HttpRequestMessage(HttpMethod.Get, Repo(repo) + "/actions/artifacts/" + report.Id + "/zip"))
            {
                request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
                using (HttpResponseMessage response = await _api.SendAsync(request, ct).ConfigureAwait(false))
                {
                    if (response.StatusCode != HttpStatusCode.Found || response.Headers.Location == null)
                        throw new InvalidOperationException($"Artifact download failed: HTTP {(int)response.StatusCode}.");
                    location = response.Headers.Location;
                }
            }

            byte[] zip = await _download.GetByteArrayAsync(location).ConfigureAwait(false);
            using (var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                ZipArchiveEntry entry = archive.GetEntry("signing-report.json")
                    ?? throw new InvalidOperationException("The artifact does not contain signing-report.json.");
                using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                    return Deserialize<SigningReportDto>(await reader.ReadToEndAsync().ConfigureAwait(false));
            }
        }

        public void Dispose()
        {
            _api.Dispose();
            _download.Dispose();
        }

        private async Task<string> GetStringAsync(string uri, CancellationToken ct)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            {
                request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
                using (HttpResponseMessage response = await _api.SendAsync(request, ct).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException($"GET {uri} failed: HTTP {(int)response.StatusCode}.");
                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
        }

        private static string Repo(RepositoryTarget repo) => "repos/" + repo.Owner + "/" + repo.Name;

        private static string Serialize<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static T Deserialize<T>(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
    }

    internal sealed class DispatchedRun
    {
        public DispatchedRun(long runId, string htmlUrl)
        {
            RunId = runId;
            HtmlUrl = htmlUrl;
        }

        public long RunId { get; }
        public string HtmlUrl { get; }
    }

    [DataContract]
    internal sealed class DispatchDto
    {
        [DataMember(Name = "ref", Order = 0)] public string Ref { get; set; }
        [DataMember(Name = "inputs", Order = 1)] public DispatchInputsDto Inputs { get; set; }
    }

    [DataContract]
    internal sealed class DispatchInputsDto
    {
        [DataMember(Name = "request_id")] public string RequestId { get; set; }
    }

    [DataContract]
    internal sealed class DispatchResultDto
    {
        [DataMember(Name = "workflow_run_id")] public long WorkflowRunId { get; set; }
        [DataMember(Name = "html_url")] public string HtmlUrl { get; set; }
    }

    [DataContract]
    internal sealed class RunDto
    {
        [DataMember(Name = "status")] public string Status { get; set; }
        [DataMember(Name = "conclusion")] public string Conclusion { get; set; }
        [DataMember(Name = "display_title")] public string DisplayTitle { get; set; }
        [DataMember(Name = "html_url")] public string HtmlUrl { get; set; }
    }

    [DataContract]
    internal sealed class ArtifactListDto
    {
        [DataMember(Name = "artifacts")] public ArtifactDto[] Artifacts { get; set; }
    }

    [DataContract]
    internal sealed class ArtifactDto
    {
        [DataMember(Name = "id")] public long Id { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
    }

    [DataContract]
    internal sealed class SigningReportDto
    {
        [DataMember(Name = "request_id")] public string RequestId { get; set; }
        [DataMember(Name = "commit")] public string Commit { get; set; }
        [DataMember(Name = "apk")] public string Apk { get; set; }
        [DataMember(Name = "signer_count")] public int SignerCount { get; set; }
        [DataMember(Name = "sha256")] public string Sha256 { get; set; }
    }
}
