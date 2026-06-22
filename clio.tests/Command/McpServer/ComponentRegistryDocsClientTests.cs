using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
public sealed class ComponentRegistryDocsClientTests {
	private const string CdnBaseUrl = "https://cdn.test/api/mcp/";
	private const string SamplePayload = "# Sample doc\n\nHello.";

	[Test]
	[Description("A fresh cache hit returns the cached markdown without touching the network.")]
	public async Task GetDocAsync_Returns_From_Cache_When_Fresh() {
		FakeDocsCacheStore cache = new();
		cache.Seed("8.2.1", "docs/sample.md", SamplePayload, isFresh: true);
		FakeHttpHandler handler = new();
		ComponentRegistryDocsClient client = CreateClient(cache, handler);

		string? content = await client.GetDocAsync("8.2.1", "docs/sample.md");

		content.Should().Be(SamplePayload, because: "a fresh cache entry must satisfy the request");
		handler.Requests.Should().BeEmpty(because: "no HTTP traffic on a cache hit");
	}

	[Test]
	[Description("A stale cache entry is revalidated synchronously: when the CDN serves a fresher doc, the fresh bytes are returned (not the stale copy) and the cache is refreshed.")]
	public async Task GetDocAsync_Revalidates_Stale_From_Cdn_When_Available() {
		// Arrange
		const string staleContent = "# Stale doc\n\nold.";
		const string freshContent = "# Fresh doc\n\nnew.";
		FakeDocsCacheStore cache = new();
		cache.Seed("8.2.1", "docs/sample.md", staleContent, isFresh: false);
		FakeHttpHandler handler = new();
		handler.Enqueue("8.2.1/docs/sample.md", HttpStatusCode.OK, freshContent);
		ComponentRegistryDocsClient client = CreateClient(cache, handler);

		// Act
		string? content = await client.GetDocAsync("8.2.1", "docs/sample.md");

		// Assert
		content.Should().Be(freshContent,
			because: "a stale doc must be revalidated against the CDN so the agent gets the current guide, not an outdated cached copy (ENG-91135)");
		handler.Requests.Should().ContainSingle(
			because: "exactly one synchronous CDN fetch is issued to refresh the stale entry");
		cache.Written.Should().ContainKey(("8.2.1", "docs/sample.md"),
			because: "a successful revalidation must repopulate the cache with the fresh payload");
	}

	[Test]
	[Description("When the CDN cannot serve a fresh doc, a stale cache entry is returned as a fallback rather than failing the request (stale-if-error).")]
	public async Task GetDocAsync_Serves_Stale_When_Cdn_Cannot_Revalidate() {
		// Arrange
		const string staleContent = "# Stale doc\n\nold but usable.";
		FakeDocsCacheStore cache = new();
		cache.Seed("8.2.1", "docs/sample.md", staleContent, isFresh: false);
		FakeHttpHandler handler = new();
		handler.EnqueueAlways(HttpStatusCode.NotFound, body: null);
		ComponentRegistryDocsClient client = CreateClient(cache, handler);

		// Act
		string? content = await client.GetDocAsync("8.2.1", "docs/sample.md");

		// Assert
		content.Should().Be(staleContent,
			because: "when revalidation fails the stale copy is still more useful to the agent than no documentation at all");
		handler.Requests.Should().ContainSingle(
			because: "a 4xx revalidation result is permanent — the stale fallback kicks in without retrying");
	}

	[Test]
	[Description("On a cold cache the client downloads from the CDN, caches it, and returns the bytes as UTF-8 text.")]
	public async Task GetDocAsync_Downloads_From_Cdn_When_Cache_Missing() {
		FakeDocsCacheStore cache = new();
		FakeHttpHandler handler = new();
		handler.Enqueue("8.2.1/docs/sample.md", HttpStatusCode.OK, SamplePayload);
		ComponentRegistryDocsClient client = CreateClient(cache, handler);

		string? content = await client.GetDocAsync("8.2.1", "docs/sample.md");

		content.Should().Be(SamplePayload, because: "the CDN payload is the response body");
		cache.Written.Should().ContainKey(("8.2.1", "docs/sample.md"),
			because: "successful CDN downloads must populate the cache for the next call");
	}

	[Test]
	[Description("When the CDN returns 404 (file not in the producer payload yet) the client returns null without retrying.")]
	public async Task GetDocAsync_Returns_Null_On_Cdn_NotFound() {
		FakeDocsCacheStore cache = new();
		FakeHttpHandler handler = new();
		handler.EnqueueAlways(HttpStatusCode.NotFound, body: null);
		ComponentRegistryDocsClient client = CreateClient(cache, handler);

		string? content = await client.GetDocAsync("8.2.1", "docs/missing.md");

		content.Should().BeNull(because: "the caller will skip the missing doc and keep any successfully-fetched siblings");
		handler.Requests.Should().HaveCount(1,
			because: "4xx is treated as permanent — no exponential-backoff retries");
	}

	[Test]
	[Description("The path validator rejects traversal attempts before any HTTP or filesystem activity.")]
	public async Task GetDocAsync_Rejects_Invalid_Paths_Without_IO() {
		FakeDocsCacheStore cache = new();
		FakeHttpHandler handler = new();
		ComponentRegistryDocsClient client = CreateClient(cache, handler);

		string? content = await client.GetDocAsync("8.2.1", "../etc/passwd.md");

		content.Should().BeNull(because: "the producer contract forbids this path");
		handler.Requests.Should().BeEmpty(because: "the validator runs ahead of any side-effect");
	}

	private static ComponentRegistryDocsClient CreateClient(FakeDocsCacheStore cache, FakeHttpHandler handler) {
		return new ComponentRegistryDocsClient(
			new FakeHttpClientFactory(handler),
			cache,
			NullLogger<ComponentRegistryDocsClient>.Instance,
			CdnBaseUrl);
	}

	private sealed class FakeHttpClientFactory(FakeHttpHandler handler) : IHttpClientFactory {
		public HttpClient CreateClient(string name) => new(handler) { Timeout = TimeSpan.FromSeconds(5) };
	}

	private sealed class FakeHttpHandler : HttpMessageHandler {
		private readonly Queue<(string Suffix, HttpStatusCode Status, string? Body)> _byPath = new();
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
			if (_byPath.Count > 0 && request.RequestUri!.AbsoluteUri.EndsWith(_byPath.Peek().Suffix, StringComparison.OrdinalIgnoreCase)) {
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
				response.Content = new StringContent(body, Encoding.UTF8, "text/markdown");
			}
			return Task.FromResult(response);
		}
	}

	private sealed class FakeDocsCacheStore : IComponentRegistryDocsCacheStore {
		private readonly Dictionary<(string Version, string DocPath), (byte[] Payload, bool IsFresh)> _entries =
			new();
		public Dictionary<(string Version, string DocPath), byte[]> Written { get; } = new();

		public void Seed(string version, string docPath, string payload, bool isFresh) {
			_entries[(version, docPath)] = (Encoding.UTF8.GetBytes(payload), isFresh);
		}

		public Task<ComponentRegistryDocsCacheReadResult?> TryReadAsync(string version, string docPath, CancellationToken cancellationToken = default) {
			if (!_entries.TryGetValue((version, docPath), out (byte[] Payload, bool IsFresh) entry)) {
				return Task.FromResult<ComponentRegistryDocsCacheReadResult?>(null);
			}
			return Task.FromResult<ComponentRegistryDocsCacheReadResult?>(new ComponentRegistryDocsCacheReadResult(
				entry.Payload,
				entry.IsFresh,
				DateTimeOffset.UtcNow.AddMinutes(entry.IsFresh ? 4 : -1)));
		}

		public Task WriteAsync(string version, string docPath, byte[] payload, EntityTagHeaderValue? etag, DateTimeOffset? lastModified, string cdnBaseUrl, CancellationToken cancellationToken = default) {
			_entries[(version, docPath)] = (payload, IsFresh: true);
			Written[(version, docPath)] = payload;
			WrittenBaseUrls[(version, docPath)] = cdnBaseUrl;
			return Task.CompletedTask;
		}

		// Records the CDN base URL the client passed to WriteAsync so tests can
		// assert that override env vars (CLIO_COMPONENT_REGISTRY_CDN_BASE_URL)
		// surface verbatim in cache metadata SourceUrl.
		public Dictionary<(string Version, string DocPath), string> WrittenBaseUrls { get; } = new();
	}
}
