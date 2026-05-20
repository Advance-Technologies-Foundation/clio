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
	[Description("A stale cache entry is returned synchronously while a background refresh runs.")]
	public async Task GetDocAsync_Returns_Stale_Cache_Without_Blocking() {
		FakeDocsCacheStore cache = new();
		cache.Seed("8.2.1", "docs/sample.md", SamplePayload, isFresh: false);
		FakeHttpHandler handler = new();
		ComponentRegistryDocsClient client = CreateClient(cache, handler);

		string? content = await client.GetDocAsync("8.2.1", "docs/sample.md");

		content.Should().Be(SamplePayload,
			because: "stale-while-revalidate must keep AI latency low even when the cache TTL has passed");
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

		public Task WriteAsync(string version, string docPath, byte[] payload, EntityTagHeaderValue? etag, DateTimeOffset? lastModified, CancellationToken cancellationToken = default) {
			_entries[(version, docPath)] = (payload, IsFresh: true);
			Written[(version, docPath)] = payload;
			return Task.CompletedTask;
		}
	}
}
