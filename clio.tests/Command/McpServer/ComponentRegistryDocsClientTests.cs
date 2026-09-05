using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
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

	/// <summary>
	/// Root of the simulated producer output directory (registry JSON plus its docs tree).
	/// Built with <see cref="Path.Combine(string, string)"/> from a rooted temp path so the
	/// containment check and the assertions run identically on Windows, macOS and Linux.
	/// </summary>
	private static readonly string WorkingCopyRoot = Path.Combine(Path.GetTempPath(), "clio-1361-working-copy");

	[Test]
	[Description("A fresh cache hit returns the cached markdown without touching the network.")]
	public async Task GetDocAsync_Returns_From_Cache_When_Fresh() {
		FakeDocsCacheStore cache = new();
		cache.Seed("8.2.1", "docs/sample.md", SamplePayload, isFresh: true);
		FakeHttpHandler handler = new();
		ComponentRegistryDocsClient client = CreateClient(cache, handler);

		ComponentDocumentationFetchResult result = await client.GetDocAsync("8.2.1", "docs/sample.md");

		result.Content.Should().Be(SamplePayload, because: "a fresh cache entry must satisfy the request");
		result.Source.Should().Be(ComponentDocumentationSource.FileCache,
			because: "the response must name the tier that served it so mixed provenance is visible");
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
		ComponentDocumentationFetchResult result = await client.GetDocAsync("8.2.1", "docs/sample.md");

		// Assert
		result.Source.Should().Be(ComponentDocumentationSource.Cdn,
			because: "a successful revalidation is a CDN read, not a cache read");
		result.Content.Should().Be(freshContent,
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
		ComponentDocumentationFetchResult result = await client.GetDocAsync("8.2.1", "docs/sample.md");

		// Assert
		result.Source.Should().Be(ComponentDocumentationSource.FileCache,
			because: "the stale fallback is served from disk, so the reported tier must say cache");
		result.Content.Should().Be(staleContent,
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

		ComponentDocumentationFetchResult result = await client.GetDocAsync("8.2.1", "docs/sample.md");

		result.Content.Should().Be(SamplePayload, because: "the CDN payload is the response body");
		result.Source.Should().Be(ComponentDocumentationSource.Cdn,
			because: "a cold-cache download is a CDN read");
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

		ComponentDocumentationFetchResult result = await client.GetDocAsync("8.2.1", "docs/missing.md");

		result.Content.Should().BeNull(because: "the caller will skip the missing doc and keep any successfully-fetched siblings");
		result.Source.Should().Be(ComponentDocumentationSource.None,
			because: "nothing was served, and the tier must say so rather than being guessed by the caller");
		result.ExpectedLocalPath.Should().BeNull(
			because: "no local override is active, so there is no working-copy path to point the developer at");
		handler.Requests.Should().HaveCount(1,
			because: "4xx is treated as permanent — no exponential-backoff retries");
	}

	[Test]
	[Description("The path validator rejects traversal attempts before any HTTP or filesystem activity.")]
	public async Task GetDocAsync_Rejects_Invalid_Paths_Without_IO() {
		FakeDocsCacheStore cache = new();
		FakeHttpHandler handler = new();
		ComponentRegistryDocsClient client = CreateClient(cache, handler);

		ComponentDocumentationFetchResult result = await client.GetDocAsync("8.2.1", "../etc/passwd.md");

		result.Content.Should().BeNull(because: "the producer contract forbids this path");
		result.Source.Should().Be(ComponentDocumentationSource.None,
			because: "a rejected path served nothing");
		handler.Requests.Should().BeEmpty(because: "the validator runs ahead of any side-effect");
	}

	[Test]
	[Description("With CLIO_COMPONENT_REGISTRY_LOCAL_FILE set, a docs/ file is served from the directory of the override file, ahead of a fresh cache entry, and is never written to the cache.")]
	public async Task GetDocAsync_Serves_Doc_From_Local_Override_Directory() {
		// Arrange
		const string localMarkdown = "# Local doc\n\nEdited in the working copy.";
		string registryPath = Path.Combine(WorkingCopyRoot, "ComponentRegistry.json");
		string docPath = Path.Combine(WorkingCopyRoot, "docs", "sample.md");
		MockFileSystem fs = new();
		fs.AddFile(registryPath, new MockFileData("[]"));
		fs.AddFile(docPath, new MockFileData(localMarkdown));
		FakeDocsCacheStore cache = new();
		cache.Seed("8.2.1", "docs/sample.md", SamplePayload, isFresh: true);
		FakeHttpHandler handler = new();
		ComponentRegistryDocsClient client = CreateClient(cache, handler, fs);
		using EnvironmentVariableScope envScope = new(RegistryFlavor.Web.LocalFileEnvironmentVariable, registryPath);

		// Act
		ComponentDocumentationFetchResult result = await client.GetDocAsync("8.2.1", "docs/sample.md");

		// Assert
		result.Content.Should().Be(localMarkdown,
			because: "the whole point of the override is that the developer reads their own edit, not the published copy");
		result.Source.Should().Be(ComponentDocumentationSource.Local,
			because: "the response must declare that documentation came from the working copy");
		handler.Requests.Should().BeEmpty(
			because: "a local hit must short-circuit the network exactly as the registry-JSON override does");
		cache.Written.Should().BeEmpty(
			because: "writing an unpublished draft into the docs cache would poison the next env-unset call");
	}

	[Test]
	[Description("A declared doc that is absent from the working copy returns source None with the expected path, and never substitutes the published CDN copy.")]
	public async Task GetDocAsync_Does_Not_Fall_Back_To_Cdn_When_Local_Override_Lacks_The_Doc() {
		// Arrange
		string registryPath = Path.Combine(WorkingCopyRoot, "ComponentRegistry.json");
		MockFileSystem fs = new();
		fs.AddFile(registryPath, new MockFileData("[]"));
		FakeDocsCacheStore cache = new();
		cache.Seed("8.2.1", "docs/sample.md", SamplePayload, isFresh: true);
		FakeHttpHandler handler = new();
		handler.EnqueueAlways(HttpStatusCode.OK, SamplePayload);
		ComponentRegistryDocsClient client = CreateClient(cache, handler, fs);
		using EnvironmentVariableScope envScope = new(RegistryFlavor.Web.LocalFileEnvironmentVariable, registryPath);

		// Act
		ComponentDocumentationFetchResult result = await client.GetDocAsync("8.2.1", "docs/sample.md");

		// Assert
		result.Content.Should().BeNull(
			because: "silently substituting published prose for a missing local file is the defect being fixed (issue #1361)");
		result.Source.Should().Be(ComponentDocumentationSource.None,
			because: "the caller needs to tell 'not generated locally' apart from 'served'");
		result.ExpectedLocalPath.Should().Be(Path.Combine(WorkingCopyRoot, "docs", "sample.md"),
			because: "the warning must name the exact file the developer has to generate");
		handler.Requests.Should().BeEmpty(
			because: "with the override active the CDN must not be consulted at all");
	}

	[Test]
	[Description("A docs namespace whose flavour override is unset keeps using the cache/CDN chain even while another flavour's override is active.")]
	public async Task GetDocAsync_Ignores_Override_Of_A_Different_Flavour() {
		// Arrange
		string registryPath = Path.Combine(WorkingCopyRoot, "MobileComponentRegistry.json");
		MockFileSystem fs = new();
		fs.AddFile(registryPath, new MockFileData("[]"));
		FakeDocsCacheStore cache = new();
		cache.Seed("8.2.1", "docs/sample.md", SamplePayload, isFresh: true);
		FakeHttpHandler handler = new();
		ComponentRegistryDocsClient client = CreateClient(cache, handler, fs);
		using EnvironmentVariableScope envScope = new(RegistryFlavor.Mobile.LocalFileEnvironmentVariable, registryPath);

		// Act
		ComponentDocumentationFetchResult result = await client.GetDocAsync("8.2.1", "docs/sample.md");

		// Assert
		result.Source.Should().Be(ComponentDocumentationSource.FileCache,
			because: "'docs/' belongs to the web flavour, whose override is unset — the mobile override must not capture it");
		result.Content.Should().Be(SamplePayload,
			because: "the untouched flavour keeps its existing cache/CDN behaviour");
	}

	[Test]
	[Description("Each documentation namespace resolves to the registry flavour that publishes it, longest prefix first.")]
	public void TryResolveFlavor_Maps_Each_Documentation_Namespace_To_Its_Flavour() {
		// Arrange
		(string Path, RegistryFlavor Expected)[] cases = [
			("docs/a.md", RegistryFlavor.Web),
			("mobile-docs/a.md", RegistryFlavor.Mobile),
			("request-docs/a.md", RegistryFlavor.Requests),
			("mobile-request-docs/a.md", RegistryFlavor.MobileRequests)
		];

		foreach ((string path, RegistryFlavor expected) in cases) {
			// Act
			bool resolved = ComponentRegistryDocsPath.TryResolveFlavor(path, out RegistryFlavor? flavor);

			// Assert
			resolved.Should().BeTrue(because: $"'{path}' uses a documentation namespace clio publishes");
			flavor.Should().BeSameAs(expected,
				because: $"'{path}' must consult {expected.LocalFileEnvironmentVariable}, otherwise the wrong working copy is read");
		}
	}

	private static ComponentRegistryDocsClient CreateClient(
		FakeDocsCacheStore cache, FakeHttpHandler handler, IFileSystem? fileSystem = null) {
		return new ComponentRegistryDocsClient(
			new FakeHttpClientFactory(handler),
			cache,
			fileSystem ?? new MockFileSystem(),
			NullLogger<ComponentRegistryDocsClient>.Instance,
			CdnBaseUrl);
	}

	/// <summary>
	/// Scopes an environment variable to one test and restores the previous value on
	/// dispose, so the process-wide override cannot leak into sibling tests.
	/// </summary>
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
