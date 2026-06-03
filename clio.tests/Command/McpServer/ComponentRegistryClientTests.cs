using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ComponentRegistryClientTests {
	private const string CdnBaseUrl = "https://cdn.test/api/mcp/";
	private const string SamplePayload = """[ { "componentType": "crt.Sample", "category": "interactive", "description": "test", "container": false, "properties": {} } ]""";

	[Test]
	[Description("A fresh cache hit returns FileCache without ever touching the network.")]
	public async Task GetAsync_Returns_From_Cache_When_Fresh() {
		// Arrange
		FakeRegistryCacheStore cache = new();
		cache.Seed("8.2.1", SamplePayload, isFresh: true);
		FakeHttpHandler handler = new();
		ComponentRegistryClient client = CreateClient(cache, handler);

		// Act
		ComponentRegistryFetchResult result = await client.GetAsync("8.2.1");

		// Assert
		result.Source.Should().Be(ComponentRegistrySource.FileCache,
			because: "fresh cache must short-circuit the CDN call so the AI never blocks on the network");
		result.ResolvedVersion.Should().Be("8.2.1");
		handler.Requests.Should().BeEmpty(
			because: "a fresh cache hit must not issue an HTTP request");
	}

	[Test]
	[Description("A stale cache entry is returned synchronously while the client schedules a background refresh.")]
	public async Task GetAsync_Returns_Stale_Cache_Without_Blocking() {
		// Arrange
		FakeRegistryCacheStore cache = new();
		cache.Seed("8.2.1", SamplePayload, isFresh: false);
		FakeHttpHandler handler = new();
		ComponentRegistryClient client = CreateClient(cache, handler);

		// Act
		ComponentRegistryFetchResult result = await client.GetAsync("8.2.1");

		// Assert
		result.Source.Should().Be(ComponentRegistrySource.FileCache,
			because: "stale-while-revalidate returns stale immediately to keep AI latency low");
		result.ResolvedVersion.Should().Be("8.2.1");
	}

	[Test]
	[Description("On a cold cache the client downloads from the CDN, caches it, and reports source=Cdn.")]
	public async Task GetAsync_Downloads_From_Cdn_When_Cache_Missing() {
		// Arrange
		FakeRegistryCacheStore cache = new();
		FakeHttpHandler handler = new();
		handler.Enqueue("8.2.1/ComponentRegistry.json", HttpStatusCode.OK, SamplePayload);
		ComponentRegistryClient client = CreateClient(cache, handler);

		// Act
		ComponentRegistryFetchResult result = await client.GetAsync("8.2.1");

		// Assert
		result.Source.Should().Be(ComponentRegistrySource.Cdn,
			because: "an empty cache must trigger a CDN fetch");
		result.ResolvedVersion.Should().Be("8.2.1");
		cache.WrittenVersions.Should().Contain("8.2.1",
			because: "successful CDN downloads must populate the cache for the next call");
	}

	[Test]
	[Description("When the per-version file 404s the client falls back to latest/ComponentRegistry.json on CDN.")]
	public async Task GetAsync_Falls_Back_To_Latest_When_Version_Missing_On_Cdn() {
		// Arrange
		FakeRegistryCacheStore cache = new();
		FakeHttpHandler handler = new();
		handler.Enqueue("9.9.9/ComponentRegistry.json", HttpStatusCode.NotFound, body: null);
		handler.Enqueue("latest/ComponentRegistry.json", HttpStatusCode.OK, SamplePayload);
		ComponentRegistryClient client = CreateClient(cache, handler);

		// Act
		ComponentRegistryFetchResult result = await client.GetAsync("9.9.9");

		// Assert
		result.Source.Should().Be(ComponentRegistrySource.Cdn,
			because: "the latest.json fallback also lives on the CDN tier");
		result.ResolvedVersion.Should().Be("latest",
			because: "the fallback resolution must be visible to the caller so the MCP Response can carry it");
	}

	[Test]
	[Description("When CDN returns 5xx repeatedly and no cache exists, the client surfaces ComponentRegistryUnavailableException with a message that points operators at the local-override env var.")]
	public async Task GetAsync_Throws_When_Cache_Empty_And_Cdn_Down() {
		// Arrange
		FakeRegistryCacheStore cache = new();
		FakeHttpHandler handler = new();
		handler.EnqueueAlways(HttpStatusCode.InternalServerError, body: null);
		ComponentRegistryClient client = CreateClient(cache, handler);

		// Act
		System.Func<System.Threading.Tasks.Task> act = async () => await client.GetAsync("8.2.1");

		// Assert
		ComponentRegistryUnavailableException ex = (await act.Should().ThrowAsync<ComponentRegistryUnavailableException>(
			because: "after CDN exhaustion + empty cache + no local override the chain has no tier left")).Which;
		ex.Message.Should().Contain("CLIO_COMPONENT_REGISTRY_LOCAL_FILE",
			because: "the message must guide the operator to the documented offline override");
		ex.Message.Should().Contain(CdnBaseUrl,
			because: "the message must surface the CDN base URL that the client could not reach");
		ex.RequestedVersion.Should().Be("8.2.1");
		ex.CdnBaseUrl.Should().Be(CdnBaseUrl);
	}

	[Test]
	[Description("RefreshAsync issues a CDN fetch and reports success when the response is 2xx.")]
	public async Task RefreshAsync_Returns_True_On_Successful_Cdn_Fetch() {
		// Arrange
		FakeRegistryCacheStore cache = new();
		FakeHttpHandler handler = new();
		handler.Enqueue("latest/ComponentRegistry.json", HttpStatusCode.OK, SamplePayload);
		ComponentRegistryClient client = CreateClient(cache, handler);

		// Act
		bool refreshed = await client.RefreshAsync("latest");

		// Assert
		refreshed.Should().BeTrue(because: "a 2xx CDN response signals a successful refresh");
		cache.WrittenVersions.Should().Contain("latest");
	}

	[Test]
	[Description("CLIO_COMPONENT_REGISTRY_LOCAL_FILE wins over the cache and the CDN — and is never written to cache.")]
	public async Task GetAsync_Returns_Local_Override_When_Env_Var_Points_To_Existing_File() {
		// Arrange
		const string overridePath = "/tmp/local-override.json";
		MockFileSystem fs = new();
		fs.AddFile(overridePath, new MockFileData(SamplePayload));
		FakeRegistryCacheStore cache = new();
		cache.Seed("8.2.1", "[ { \"componentType\": \"crt.Cached\", \"category\": \"display\", \"properties\": {} } ]", isFresh: true);
		FakeHttpHandler handler = new();
		ComponentRegistryClient client = CreateClient(cache, handler, fileSystem: fs);

		using EnvironmentVariableScope envScope = new(ComponentRegistryClient.LocalFileEnvironmentVariable, overridePath);

		// Act
		ComponentRegistryFetchResult result = await client.GetAsync("8.2.1");

		// Assert
		result.Source.Should().Be(ComponentRegistrySource.Local,
			because: "the local-file override is Tier 0 and must trump even a fresh cache entry");
		result.ResolvedVersion.Should().Be("8.2.1",
			because: "the override is transparent — the requested version is what gets resolved");
		handler.Requests.Should().BeEmpty(
			because: "a successful local override must short-circuit the network");
		cache.WrittenVersions.Should().BeEmpty(
			because: "the override is a read-only test channel; writing it to cache would poison the next env-unset call");
	}

	[Test]
	[Description("Missing override file raises a FileNotFoundException — fail-fast on misconfiguration.")]
	public async Task GetAsync_Throws_When_Local_Override_File_Missing() {
		// Arrange
		const string overridePath = "/tmp/does-not-exist.json";
		MockFileSystem fs = new();
		FakeRegistryCacheStore cache = new();
		FakeHttpHandler handler = new();
		// CDN is enqueued only to prove it is NOT touched: silently falling back to it would
		// mask the configuration error and make the AI serve stale data while the developer
		// thinks they are testing a local payload.
		handler.Enqueue("8.2.1/ComponentRegistry.json", HttpStatusCode.OK, SamplePayload);
		ComponentRegistryClient client = CreateClient(cache, handler, fileSystem: fs);

		using EnvironmentVariableScope envScope = new(ComponentRegistryClient.LocalFileEnvironmentVariable, overridePath);

		// Act
		System.Func<Task> act = async () => await client.GetAsync("8.2.1");

		// Assert
		await act.Should().ThrowAsync<FileNotFoundException>(
				because: "a non-empty override env var that points at nothing is a developer mistake, not a fallback signal")
			.Where(ex => ex.FileName == overridePath,
				"the exception must identify the bad path so the user can fix the env var");
		handler.Requests.Should().BeEmpty(
			because: "fail-fast must never touch the CDN — otherwise the override config error is masked");
	}

	[Test]
	[Description("RefreshAsync ignores the local-file override and always pulls fresh CDN bytes into the cache.")]
	public async Task RefreshAsync_Ignores_Local_Override_And_Fetches_From_Cdn() {
		// Arrange
		const string overridePath = "/tmp/local-override.json";
		MockFileSystem fs = new();
		fs.AddFile(overridePath, new MockFileData(SamplePayload));
		FakeRegistryCacheStore cache = new();
		FakeHttpHandler handler = new();
		handler.Enqueue("latest/ComponentRegistry.json", HttpStatusCode.OK, SamplePayload);
		ComponentRegistryClient client = CreateClient(cache, handler, fileSystem: fs);

		using EnvironmentVariableScope envScope = new(ComponentRegistryClient.LocalFileEnvironmentVariable, overridePath);

		// Act
		bool refreshed = await client.RefreshAsync("latest");

		// Assert
		refreshed.Should().BeTrue(
			because: "refresh is a deliberate CDN pull — the local override must not silently mask the network round-trip");
		handler.Requests.Should().NotBeEmpty(
			because: "RefreshAsync must hit the CDN even when an override is configured");
		cache.WrittenVersions.Should().Contain("latest",
			because: "RefreshAsync persists CDN bytes (not override bytes) so subsequent reads serve real data");
	}

	[Test]
	[Description("DrainAsync waits for a background refresh triggered by a stale cache hit to write to the cache store.")]
	public async Task DrainAsync_Waits_For_Background_Refresh_To_Complete() {
		// Arrange
		FakeRegistryCacheStore cache = new();
		cache.Seed("latest", SamplePayload, isFresh: false);
		FakeHttpHandler handler = new();
		handler.Enqueue("latest/ComponentRegistry.json", HttpStatusCode.OK, SamplePayload);
		ComponentRegistryClient client = CreateClient(cache, handler);

		// Act — stale hit schedules a background refresh; DrainAsync must not
		// return until that refresh has written its result to the cache store.
		await client.GetAsync("latest");
		await ComponentRegistryClient.DrainAsync(TimeSpan.FromSeconds(5));

		// Assert
		cache.WrittenVersions.Should().Contain("latest",
			because: "the background refresh must complete (and persist to cache) before DrainAsync returns");
	}

	[Test]
	[Description("DrainAsync returns within the timeout even when a background refresh task hangs on the CDN.")]
	public async Task DrainAsync_Returns_Within_Timeout_When_Refresh_Hangs() {
		// Arrange — CDN that never responds (simulates a network hang).
		// Use a version key distinct from other tests to avoid sharing the
		// static BackgroundRefreshGate and causing gate-leak interference.
		const string version = "hang-test";
		FakeRegistryCacheStore cache = new();
		cache.Seed(version, SamplePayload, isFresh: false);
		HangingHttpHandler hangingHandler = new();
		ComponentRegistryClient client = CreateClient(cache, hangingHandler);

		// Act — stale hit schedules a background refresh that will hang indefinitely
		await client.GetAsync(version);
		Stopwatch sw = Stopwatch.StartNew();
		await ComponentRegistryClient.DrainAsync(TimeSpan.FromMilliseconds(300));
		sw.Stop();

		// Assert
		sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
			because: "DrainAsync must honour its timeout and not block the process shutdown indefinitely");

		hangingHandler.Dispose();
	}

	[Test]
	[Description("RefreshAsync reports failure when the CDN never returns success.")]
	public async Task RefreshAsync_Returns_False_When_Cdn_Down() {
		// Arrange
		FakeRegistryCacheStore cache = new();
		FakeHttpHandler handler = new();
		handler.EnqueueAlways(HttpStatusCode.InternalServerError, body: null);
		ComponentRegistryClient client = CreateClient(cache, handler);

		// Act
		bool refreshed = await client.RefreshAsync("latest");

		// Assert
		refreshed.Should().BeFalse(
			because: "RefreshAsync must surface CDN failures so the CLI verb can print an actionable diagnostic");
	}

	private static ComponentRegistryClient CreateClient(
		FakeRegistryCacheStore cache,
		HttpMessageHandler handler,
		IFileSystem? fileSystem = null) {
		fileSystem ??= new MockFileSystem();
		return new ComponentRegistryClient(
			new FakeHttpClientFactory(handler),
			cache,
			fileSystem,
			NullLogger<ComponentRegistryClient>.Instance,
			CdnBaseUrl);
	}

	private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory {
		public HttpClient CreateClient(string name) => new(handler) {
			// Short timeout keeps the test fast in case of an accidental real network call.
			Timeout = TimeSpan.FromSeconds(5)
		};
	}

	private sealed class FakeHttpHandler : HttpMessageHandler {
		private readonly Queue<(string Path, HttpStatusCode Status, string? Body)> _byPath = new();
		private (HttpStatusCode Status, string? Body)? _fallback;
		public List<Uri> Requests { get; } = new();

		public void Enqueue(string pathSuffix, HttpStatusCode status, string? body) {
			_byPath.Enqueue((pathSuffix, status, body));
		}

		public void EnqueueAlways(HttpStatusCode status, string? body) {
			_fallback = (status, body);
		}

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
			Requests.Add(request.RequestUri!);

			HttpStatusCode status;
			string? body;
			if (_byPath.Count > 0 && request.RequestUri!.AbsoluteUri.EndsWith(_byPath.Peek().Path, StringComparison.OrdinalIgnoreCase)) {
				(string _, HttpStatusCode s, string? b) = _byPath.Dequeue();
				status = s;
				body = b;
			} else if (_fallback is { } fb) {
				status = fb.Status;
				body = fb.Body;
			} else {
				status = HttpStatusCode.NotFound;
				body = null;
			}

			HttpResponseMessage response = new(status);
			if (body is not null) {
				response.Content = new StringContent(body, Encoding.UTF8, "application/json");
			}
			return Task.FromResult(response);
		}
	}

	private sealed class FakeRegistryCacheStore : IComponentRegistryCacheStore {
		private readonly Dictionary<string, (byte[] Payload, bool IsFresh)> _entries = new(StringComparer.OrdinalIgnoreCase);
		public List<string> WrittenVersions { get; } = new();

		public void Seed(string version, string payload, bool isFresh) {
			_entries[version] = (Encoding.UTF8.GetBytes(payload), isFresh);
		}

		public Task<ComponentRegistryCacheReadResult?> TryReadAsync(string version, CancellationToken cancellationToken = default) {
			if (!_entries.TryGetValue(version, out (byte[] Payload, bool IsFresh) entry)) {
				return Task.FromResult<ComponentRegistryCacheReadResult?>(null);
			}
			return Task.FromResult<ComponentRegistryCacheReadResult?>(new ComponentRegistryCacheReadResult(
				new MemoryStream(entry.Payload, writable: false),
				entry.IsFresh,
				DateTimeOffset.UtcNow.AddHours(entry.IsFresh ? 23 : -1)));
		}

		public Task WriteAsync(string version, byte[] payload, System.Net.Http.Headers.EntityTagHeaderValue? etag, DateTimeOffset? lastModified, string sourceUrl, CancellationToken cancellationToken = default) {
			_entries[version] = (payload, IsFresh: true);
			WrittenVersions.Add(version);
			WrittenSourceUrls[version] = sourceUrl;
			return Task.CompletedTask;
		}

		// Captures the SourceUrl the registry client passes to WriteAsync so tests
		// can assert that an override env var (CLIO_COMPONENT_REGISTRY_CDN_BASE_URL)
		// + flavor selection both surface verbatim in cache metadata.
		public Dictionary<string, string> WrittenSourceUrls { get; } = new();
	}

	private sealed class HangingHttpHandler : HttpMessageHandler {
		private readonly TaskCompletionSource<HttpResponseMessage> _tcs = new();
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			_tcs.Task;
		protected override void Dispose(bool disposing) {
			_tcs.TrySetCanceled();
			base.Dispose(disposing);
		}
	}

	private sealed class EnvironmentVariableScope : IDisposable {
		private readonly string _name;
		private readonly string? _previous;

		public EnvironmentVariableScope(string name, string? value) {
			_name = name;
			_previous = Environment.GetEnvironmentVariable(name);
			Environment.SetEnvironmentVariable(name, value);
		}

		public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
	}
}
