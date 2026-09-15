using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ComponentRegistryCacheStoreTests {
	private const string SamplePayloadJson = """[ { "componentType": "crt.Sample" } ]""";

	[Test]
	[Description("Reading a missing cache entry returns null instead of throwing.")]
	public async Task TryReadAsync_Returns_Null_When_No_Entry_Exists() {
		// Arrange
		ComponentRegistryCacheStore store = CreateStore(new MockFileSystem(), new FakeTimeProvider());

		// Act
		ComponentRegistryCacheReadResult? result = await store.TryReadAsync("8.2.1");

		// Assert
		result.Should().BeNull(
			because: "an empty cache should not raise; the client must fall through to the CDN or embedded fallback");
	}

	[Test]
	[Description("WriteAsync persists payload + sidecar so the next read returns IsFresh=true within the TTL.")]
	public async Task WriteAsync_Then_TryReadAsync_Returns_Fresh_Result() {
		// Arrange
		MockFileSystem fileSystem = new();
		FakeTimeProvider clock = new();
		clock.SetUtcNow(DateTimeOffset.Parse("2026-05-13T10:00:00Z"));
		ComponentRegistryCacheStore store = CreateStore(fileSystem, clock);
		byte[] payload = Encoding.UTF8.GetBytes(SamplePayloadJson);

		// Act
		await store.WriteAsync("8.2.1", payload, EntityTagHeaderValue.Parse("\"abc\""), DateTimeOffset.Parse("2026-05-13T09:30:00Z"), sourceUrl: "https://academy.creatio.com/api/mcp/8.2.1/ComponentRegistry.json");
		ComponentRegistryCacheReadResult? read = await store.TryReadAsync("8.2.1");

		// Assert
		read.Should().NotBeNull(because: "a just-written cache entry must be retrievable");
		read!.IsFresh.Should().BeTrue(because: "TTL is 5min and only zero seconds elapsed");
		using StreamReader reader = new(read.Content);
		(await reader.ReadToEndAsync()).Should().Be(SamplePayloadJson,
			because: "the cached payload must round-trip byte-for-byte");
	}

	[Test]
	[Description("Once the 5-minute TTL passes the cache entry is reported as stale but still served.")]
	public async Task TryReadAsync_Reports_Stale_After_Ttl() {
		// Arrange
		MockFileSystem fileSystem = new();
		FakeTimeProvider clock = new();
		clock.SetUtcNow(DateTimeOffset.Parse("2026-05-13T10:00:00Z"));
		ComponentRegistryCacheStore store = CreateStore(fileSystem, clock);
		await store.WriteAsync("latest", Encoding.UTF8.GetBytes(SamplePayloadJson), etag: null, lastModified: null, sourceUrl: "https://academy.creatio.com/api/mcp/latest/ComponentRegistry.json");

		// Act
		clock.Advance(TimeSpan.FromMinutes(6));
		ComponentRegistryCacheReadResult? read = await store.TryReadAsync("latest");

		// Assert
		read.Should().NotBeNull(because: "stale entries are still returned to support stale-while-revalidate");
		read!.IsFresh.Should().BeFalse(because: "6min > 5min TTL so the entry is past its ExpiresAt");
	}

	[Test]
	[Description("Corrupted metadata sidecar causes both files to be removed so the next write starts clean.")]
	public async Task TryReadAsync_Deletes_Corrupted_Entry() {
		// Arrange
		string root = "/cache";
		MockFileSystem fileSystem = new(new Dictionary<string, MockFileData> {
			[$"{root}/8.2.1.json"] = new(SamplePayloadJson),
			[$"{root}/8.2.1.meta.json"] = new("this is not JSON")
		});
		ComponentRegistryCacheStore store = new(fileSystem, new FakeTimeProvider(), root);

		// Act
		ComponentRegistryCacheReadResult? read = await store.TryReadAsync("8.2.1");

		// Assert
		read.Should().BeNull(because: "an unreadable sidecar must be treated as a cache miss");
		fileSystem.File.Exists($"{root}/8.2.1.json").Should().BeFalse(
			because: "self-heal: a corrupted entry is deleted so the next write does not fall over a stale payload");
		fileSystem.File.Exists($"{root}/8.2.1.meta.json").Should().BeFalse(
			because: "the sidecar that triggered the failure is removed along with the payload");
	}

	[Test]
	[Description("Versions with path-traversal-style characters are sanitised so the cache cannot escape its directory.")]
	public async Task WriteAsync_Sanitises_Version_For_Path_Safety() {
		// Arrange
		string root = "/cache";
		MockFileSystem fileSystem = new();
		ComponentRegistryCacheStore store = new(fileSystem, new FakeTimeProvider(), root);

		// Act — the version contains directory separators that, if naïvely concatenated into a
		// path, would escape the cache root. The sanitiser strips the separators (and any other
		// non-alphanumeric / non-dot / non-dash / non-underscore characters), keeping the
		// resulting file under the cache root.
		await store.WriteAsync("../etc/passwd", Encoding.UTF8.GetBytes(SamplePayloadJson), etag: null, lastModified: null, sourceUrl: "https://academy.creatio.com/api/mcp/../etc/passwd/ComponentRegistry.json");

		// Assert
		fileSystem.File.Exists($"{root}/..etcpasswd.json").Should().BeTrue(
			because: "path separators must be stripped so the file is forced under the cache root");
		fileSystem.File.Exists("/etc/passwd").Should().BeFalse(
			because: "no payload should land on a real /etc path even when the version literally contains '../etc/passwd'");
		fileSystem.File.Exists("/etc/passwd.json").Should().BeFalse(
			because: "and no '..json'-style escape is possible either");
	}

	[Test]
	[Description("ENG-91859: a cache entry written from a staging CDN prefix is NOT served to a reader whose effective base URL is production. The cache is keyed on the sanitised version alone, and the ticket's own validation procedure is to point CLIO_COMPONENT_REGISTRY_CDN_BASE_URL at a latest-mobile-preview/ prefix — which writes the staging catalog into the very latest.json slot a later run with the variable unset reads back, for the whole TTL and with nothing in the payload saying so.")]
	public async Task TryReadAsync_Refuses_An_Entry_Written_From_A_Different_Cdn_Base_Url() {
		// Arrange
		const string stagingBase = "https://academy.creatio.com/api/mcp/latest-mobile-preview/";
		const string productionBase = "https://academy.creatio.com/api/mcp/";
		MockFileSystem fileSystem = new();
		ComponentRegistryCacheStore store = CreateStore(fileSystem, new FakeTimeProvider());
		await store.WriteAsync("latest", Encoding.UTF8.GetBytes(SamplePayloadJson), etag: null, lastModified: null,
			sourceUrl: stagingBase + "latest/MobileComponentRegistry.json");

		// Act
		ComponentRegistryCacheReadResult? fromStaging = await store.TryReadAsync("latest", stagingBase);
		ComponentRegistryCacheReadResult? fromProduction = await store.TryReadAsync("latest", productionBase);

		// Assert
		fromStaging.Should().NotBeNull(
			because: "the reader whose base URL produced the entry must still get its own cache hit — the guard "
				+ "isolates prefixes, it does not disable caching whenever an override is in effect");
		fromProduction.Should().BeNull(
			because: "the staging payload occupies the same 'latest' slot, so a production reader served from it "
				+ "would ship a preview catalog as the released one with no signal at all");
	}

	[Test]
	[Description("ENG-91859: the prefix guard is a match on the recorded SourceUrl, not a blanket refusal — a reader that supplies no prefix, and one whose prefix is exactly the recorded base, are answered as before. Without this assertion the guard would be indistinguishable from disabling the cache.")]
	public async Task TryReadAsync_Serves_The_Entry_When_The_Prefix_Matches_Or_Is_Not_Supplied() {
		// Arrange
		const string baseUrl = "https://academy.creatio.com/api/mcp/";
		MockFileSystem fileSystem = new();
		ComponentRegistryCacheStore store = CreateStore(fileSystem, new FakeTimeProvider());
		await store.WriteAsync("8.2.1", Encoding.UTF8.GetBytes(SamplePayloadJson), etag: null, lastModified: null,
			sourceUrl: baseUrl + "8.2.1/ComponentRegistry.json");

		// Act
		ComponentRegistryCacheReadResult? unfiltered = await store.TryReadAsync("8.2.1");
		ComponentRegistryCacheReadResult? matching = await store.TryReadAsync("8.2.1", baseUrl);

		// Assert
		unfiltered.Should().NotBeNull(
			because: "a caller that supplies no prefix (diagnostics, cache inspection) accepts any recorded source");
		matching.Should().NotBeNull(
			because: "the recorded SourceUrl starts with the reader's own base URL, which is the ordinary hit");
	}

	private static ComponentRegistryCacheStore CreateStore(MockFileSystem fileSystem, TimeProvider clock) {
		return new ComponentRegistryCacheStore(fileSystem, clock, "/cache");
	}

	private sealed class FakeTimeProvider : TimeProvider {
		private DateTimeOffset _now = DateTimeOffset.Parse("2026-05-13T10:00:00Z");

		public override DateTimeOffset GetUtcNow() => _now;
		public void SetUtcNow(DateTimeOffset value) => _now = value;
		public void Advance(TimeSpan delta) => _now += delta;
	}
}
