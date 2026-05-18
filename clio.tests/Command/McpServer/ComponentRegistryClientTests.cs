using System;
using System.Collections.Generic;
using System.IO;
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
	[Description("When CDN returns 5xx repeatedly and no cache exists, the client falls back to the embedded snapshot.")]
	public async Task GetAsync_Falls_Back_To_Embedded_When_Cdn_Down() {
		// Arrange
		FakeRegistryCacheStore cache = new();
		FakeHttpHandler handler = new();
		handler.EnqueueAlways(HttpStatusCode.InternalServerError, body: null);
		FakeEmbeddedRegistryReader embedded = new(SamplePayload, "latest");
		ComponentRegistryClient client = CreateClient(cache, handler, embedded);

		// Act
		ComponentRegistryFetchResult result = await client.GetAsync("8.2.1");

		// Assert
		result.Source.Should().Be(ComponentRegistrySource.Embedded,
			because: "after CDN exhaustion and empty cache the embedded snapshot is the final tier");
		result.ResolvedVersion.Should().Be("latest",
			because: "embedded snapshot carries its own version label baked at clio build time");
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
		FakeHttpHandler handler,
		IEmbeddedRegistryReader? embedded = null) {
		embedded ??= new FakeEmbeddedRegistryReader(SamplePayload, "latest");
		return new ComponentRegistryClient(
			new FakeHttpClientFactory(handler),
			cache,
			embedded,
			NullLogger<ComponentRegistryClient>.Instance,
			CdnBaseUrl);
	}

	private sealed class FakeHttpClientFactory(FakeHttpHandler handler) : IHttpClientFactory {
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

		public Task WriteAsync(string version, byte[] payload, System.Net.Http.Headers.EntityTagHeaderValue? etag, DateTimeOffset? lastModified, CancellationToken cancellationToken = default) {
			_entries[version] = (payload, IsFresh: true);
			WrittenVersions.Add(version);
			return Task.CompletedTask;
		}
	}

	private sealed class FakeEmbeddedRegistryReader(string payload, string version) : IEmbeddedRegistryReader {
		public Stream OpenRegistryStream() => new MemoryStream(Encoding.UTF8.GetBytes(payload), writable: false);
		public string EmbeddedVersion => version;
	}
}
