using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenOnboarding.Application.Interfaces;
using OpenOnboarding.Application.Tests.TestHelpers;

namespace OpenOnboarding.Application.Tests;

/// <summary>
/// Document upload and download were the only session endpoints carrying
/// <c>[Authorize(Policy = "ApplicantOrOperator")]</c> without a resource-based ownership check. The
/// policy asserts the caller holds some applicant or operator credential; it says nothing about
/// which session that credential names, which is the whole point of the per-session token.
/// <para>
/// Download was worse than an absent check: <c>sessionId</c> and <c>nodeId</c> were in the route but
/// never reached the service, which resolved the file id straight from storage. The segments that
/// looked like they scoped the request did nothing at all.
/// </para>
/// </summary>
public sealed class DocumentOwnershipTests
{
    private const string OperatorApiKey = "test-api-key";

    private sealed record StartResponse(Guid SessionId, bool IsCompleted, NodeStub? CurrentNode, string? ApplicantToken);
    private sealed record NodeStub(Guid Id, string Key, string Type);
    private sealed record FlowStub(Guid Id);
    private sealed record StoredFile(string FileId, string FileName);

    private static WebApplicationFactory<Program> CreateFactory()
        => TestWebAppFactory.Create(configureServices: services =>
        {
            // Otherwise uploads hit the real filesystem provider. The double keeps the test about
            // authorization rather than about storage.
            services.RemoveAll<IDocumentStorageService>();
            services.AddSingleton<IDocumentStorageService, RecordingDocumentStorageService>();
        });

    private static HttpClient CreateOperatorClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", OperatorApiKey);
        return client;
    }

    /// <summary>A flow whose start node accepts documents, so a session is ready to receive one.</summary>
    private static async Task<Guid> CreateDocumentFlowAsync(HttpClient operatorClient)
    {
        var response = await operatorClient.PostAsJsonAsync("/api/flows", new
        {
            name = $"Document flow {Guid.NewGuid()}",
            nodes = new[]
            {
                new
                {
                    key = "upload",
                    type = "DocumentUpload",
                    title = "Upload",
                    isStartNode = true,
                    jsonContent = "{\"acceptedFileTypes\":[\"text/plain\"],\"maxFiles\":5}"
                }
            },
            connections = Array.Empty<object>()
        });

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FlowStub>())!.Id;
    }

    private static async Task<StartResponse> StartSessionAsync(HttpClient browser, Guid flowId)
    {
        var response = await browser.PostAsJsonAsync("/api/workflow/sessions/start", new { flowId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StartResponse>())!;
    }

    private static HttpRequestMessage UploadRequest(Guid sessionId, Guid nodeId, string? token, string? apiKey = null)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent("hello"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(file, "files", "evidence.txt");

        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/workflow/sessions/{sessionId}/steps/{nodeId}/documents")
        {
            Content = content
        };

        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (apiKey is not null)
            request.Headers.Add("X-Api-Key", apiKey);

        return request;
    }

    private static HttpRequestMessage DownloadRequest(Guid sessionId, Guid nodeId, string fileId, string? token, string? apiKey = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/workflow/sessions/{sessionId}/steps/{nodeId}/documents/{fileId}");

        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (apiKey is not null)
            request.Headers.Add("X-Api-Key", apiKey);

        return request;
    }

    private static async Task<string> UploadAndGetFileIdAsync(HttpClient client, StartResponse session)
    {
        var response = await client.SendAsync(
            UploadRequest(session.SessionId, session.CurrentNode!.Id, session.ApplicantToken));
        response.EnsureSuccessStatusCode();

        var stored = await response.Content.ReadFromJsonAsync<List<StoredFile>>();
        return stored!.Single().FileId;
    }

    // ── Upload ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_WithATokenForAnotherSession_IsRefused()
    {
        using var factory = CreateFactory();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateDocumentFlowAsync(operatorClient);

        using var browser = factory.CreateClient();
        var mine = await StartSessionAsync(browser, flowId);
        var theirs = await StartSessionAsync(browser, flowId);

        // My credential, their session id in the route.
        var response = await browser.SendAsync(
            UploadRequest(theirs.SessionId, theirs.CurrentNode!.Id, mine.ApplicantToken));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Upload_WithATokenForAnotherSession_WritesNothingIntoThatSession()
    {
        using var factory = CreateFactory();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateDocumentFlowAsync(operatorClient);

        using var browser = factory.CreateClient();
        var mine = await StartSessionAsync(browser, flowId);
        var theirs = await StartSessionAsync(browser, flowId);

        await browser.SendAsync(UploadRequest(theirs.SessionId, theirs.CurrentNode!.Id, mine.ApplicantToken));

        // The refusal has to happen before the write. A rejected upload that still lands a
        // Submission row shows up in the operator console as the real applicant's document.
        var submissions = await operatorClient.GetFromJsonAsync<List<object>>(
            $"/api/workflow/sessions/{theirs.SessionId}/submissions");

        Assert.Empty(submissions!);
    }

    [Fact]
    public async Task Upload_ToMyOwnSession_Succeeds()
    {
        using var factory = CreateFactory();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateDocumentFlowAsync(operatorClient);

        using var browser = factory.CreateClient();
        var mine = await StartSessionAsync(browser, flowId);

        var response = await browser.SendAsync(
            UploadRequest(mine.SessionId, mine.CurrentNode!.Id, mine.ApplicantToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Upload_ToAnAbandonedSession_IsRefused()
    {
        using var factory = CreateFactory();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateDocumentFlowAsync(operatorClient);

        using var browser = factory.CreateClient();
        var mine = await StartSessionAsync(browser, flowId);

        var abandon = new HttpRequestMessage(HttpMethod.Delete, $"/api/workflow/sessions/{mine.SessionId}");
        abandon.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mine.ApplicantToken);
        (await browser.SendAsync(abandon)).EnsureSuccessStatusCode();

        // Consistent with the terminal-status rule: a finished journey accepts no more writes.
        var response = await browser.SendAsync(
            UploadRequest(mine.SessionId, mine.CurrentNode!.Id, mine.ApplicantToken));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Upload_ByAnOperator_ReachesAnySession()
    {
        using var factory = CreateFactory();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateDocumentFlowAsync(operatorClient);

        using var browser = factory.CreateClient();
        var applicantSession = await StartSessionAsync(browser, flowId);

        var response = await operatorClient.SendAsync(UploadRequest(
            applicantSession.SessionId, applicantSession.CurrentNode!.Id, token: null, apiKey: OperatorApiKey));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Download ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Download_OfMyOwnDocument_Succeeds()
    {
        using var factory = CreateFactory();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateDocumentFlowAsync(operatorClient);

        using var browser = factory.CreateClient();
        var mine = await StartSessionAsync(browser, flowId);
        var fileId = await UploadAndGetFileIdAsync(browser, mine);

        var response = await browser.SendAsync(
            DownloadRequest(mine.SessionId, mine.CurrentNode!.Id, fileId, mine.ApplicantToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Download_WithATokenForAnotherSession_IsRefused()
    {
        using var factory = CreateFactory();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateDocumentFlowAsync(operatorClient);

        using var browser = factory.CreateClient();
        var mine = await StartSessionAsync(browser, flowId);
        var theirs = await StartSessionAsync(browser, flowId);
        var theirFileId = await UploadAndGetFileIdAsync(browser, theirs);

        // Their session id in the route, my credential.
        var response = await browser.SendAsync(
            DownloadRequest(theirs.SessionId, theirs.CurrentNode!.Id, theirFileId, mine.ApplicantToken));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Download_OfAnotherSessionsFile_ThroughMyOwnSessionRoute_IsRefused()
    {
        using var factory = CreateFactory();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateDocumentFlowAsync(operatorClient);

        using var browser = factory.CreateClient();
        var mine = await StartSessionAsync(browser, flowId);
        var theirs = await StartSessionAsync(browser, flowId);
        var theirFileId = await UploadAndGetFileIdAsync(browser, theirs);

        // The ownership check passes - this is my own session - so only scoping the lookup to the
        // route can stop it. This is the case an authorization check alone would miss.
        var response = await browser.SendAsync(
            DownloadRequest(mine.SessionId, mine.CurrentNode!.Id, theirFileId, mine.ApplicantToken));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Download_OfAFileIdThatWasNeverUploaded_Returns404()
    {
        using var factory = CreateFactory();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateDocumentFlowAsync(operatorClient);

        using var browser = factory.CreateClient();
        var mine = await StartSessionAsync(browser, flowId);

        var response = await browser.SendAsync(DownloadRequest(
            mine.SessionId, mine.CurrentNode!.Id, Guid.NewGuid().ToString(), mine.ApplicantToken));

        // 404 rather than 403: a 403 would confirm the id names a real document.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Download_NeverReachesStorage_WhenTheFileIsNotRecordedAgainstTheSession()
    {
        using var factory = CreateFactory();
        var storage = (RecordingDocumentStorageService)factory.Services.GetRequiredService<IDocumentStorageService>();

        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateDocumentFlowAsync(operatorClient);

        using var browser = factory.CreateClient();
        var mine = await StartSessionAsync(browser, flowId);
        var theirs = await StartSessionAsync(browser, flowId);
        var theirFileId = await UploadAndGetFileIdAsync(browser, theirs);

        storage.Reads.Clear();

        await browser.SendAsync(
            DownloadRequest(mine.SessionId, mine.CurrentNode!.Id, theirFileId, mine.ApplicantToken));

        // The bytes must never be fetched, not merely withheld after the fact.
        Assert.Empty(storage.Reads);
    }

    [Fact]
    public async Task Download_ByAnOperator_ReachesAnySessionsDocument()
    {
        using var factory = CreateFactory();
        using var operatorClient = CreateOperatorClient(factory);
        var flowId = await CreateDocumentFlowAsync(operatorClient);

        using var browser = factory.CreateClient();
        var applicantSession = await StartSessionAsync(browser, flowId);
        var fileId = await UploadAndGetFileIdAsync(browser, applicantSession);

        var response = await operatorClient.SendAsync(DownloadRequest(
            applicantSession.SessionId, applicantSession.CurrentNode!.Id, fileId, token: null, apiKey: OperatorApiKey));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Stores in memory and records every read, so a test can assert the bytes were never fetched
    /// rather than only that the response withheld them.
    /// </summary>
    private sealed class RecordingDocumentStorageService : IDocumentStorageService
    {
        private readonly Dictionary<string, StoredFileInfo> _files = [];

        public List<string> Reads { get; } = [];

        public Task<StoredFileInfo> StoreAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default)
        {
            var info = new StoredFileInfo(Guid.NewGuid().ToString(), fileName, contentType, stream.Length, DateTimeOffset.UtcNow);
            _files[info.FileId] = info;
            return Task.FromResult(info);
        }

        public Task<(Stream Stream, StoredFileInfo Info)> GetStreamAsync(string fileId, CancellationToken cancellationToken = default)
        {
            Reads.Add(fileId);

            if (!_files.TryGetValue(fileId, out var info))
                throw new FileNotFoundException($"No stored file '{fileId}'.");

            return Task.FromResult<(Stream, StoredFileInfo)>((new MemoryStream("hello"u8.ToArray()), info));
        }

        public Task<ScanResult> ScanAsync(string fileId, CancellationToken cancellationToken = default)
            => Task.FromResult(new ScanResult(true, null));

        public Task DeleteAsync(string fileId, CancellationToken cancellationToken = default)
        {
            _files.Remove(fileId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<StoredFileInfo>> ListOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<StoredFileInfo>>([]);
    }
}
