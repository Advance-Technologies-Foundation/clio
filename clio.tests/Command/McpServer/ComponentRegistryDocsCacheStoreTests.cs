using System;
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
public sealed class ComponentRegistryDocsCacheStoreTests {
	private const string SamplePayload = "# Sample markdown\n\nBody text.";

	[Test]
	[Description("Reading a missing docs entry returns null so the client falls through to the CDN tier.")]
	public async Task TryReadAsync_Returns_Null_When_No_Entry_Exists() {
		ComponentRegistryDocsCacheStore store = CreateStore(new MockFileSystem(), new FakeTimeProvider());

		ComponentRegistryDocsCacheReadResult? result = await store.TryReadAsync("8.2.1", "docs/sample.md");

		result.Should().BeNull(because: "an empty docs cache must not raise; the caller will hit the CDN");
	}

	[Test]
	[Description("WriteAsync persists payload + sidecar so the next read returns IsFresh=true within the TTL.")]
	public async Task WriteAsync_Then_TryReadAsync_Returns_Fresh_Result() {
		MockFileSystem fileSystem = new();
		FakeTimeProvider clock = new();
		clock.SetUtcNow(DateTimeOffset.Parse("2026-05-13T10:00:00Z"));
		ComponentRegistryDocsCacheStore store = CreateStore(fileSystem, clock);
		byte[] payload = Encoding.UTF8.GetBytes(SamplePayload);

		await store.WriteAsync("8.2.1", "docs/sample.md", payload,
			EntityTagHeaderValue.Parse("\"abc\""),
			DateTimeOffset.Parse("2026-05-13T09:30:00Z"),
			cdnBaseUrl: "https://academy.creatio.com/api/mcp/");
		ComponentRegistryDocsCacheReadResult? read = await store.TryReadAsync("8.2.1", "docs/sample.md");

		read.Should().NotBeNull(because: "a just-written cache entry must be retrievable");
		read!.IsFresh.Should().BeTrue(because: "TTL is 5min and zero seconds elapsed");
		Encoding.UTF8.GetString(read.Content).Should().Be(SamplePayload,
			because: "the cached payload must round-trip byte-for-byte");
	}

	[Test]
	[Description("Once 5 minutes pass the cache entry is reported as stale but still served.")]
	public async Task TryReadAsync_Reports_Stale_After_Ttl() {
		MockFileSystem fileSystem = new();
		FakeTimeProvider clock = new();
		clock.SetUtcNow(DateTimeOffset.Parse("2026-05-13T10:00:00Z"));
		ComponentRegistryDocsCacheStore store = CreateStore(fileSystem, clock);
		await store.WriteAsync("latest", "docs/sample.md", Encoding.UTF8.GetBytes(SamplePayload), etag: null, lastModified: null, cdnBaseUrl: "https://academy.creatio.com/api/mcp/");

		clock.Advance(TimeSpan.FromMinutes(6));
		ComponentRegistryDocsCacheReadResult? read = await store.TryReadAsync("latest", "docs/sample.md");

		read.Should().NotBeNull(because: "stale entries are still returned to support stale-while-revalidate");
		read!.IsFresh.Should().BeFalse(because: "6min > 5min TTL so the entry is past its ExpiresAt");
	}

	[TestCase("../etc/passwd.md")]
	[TestCase("/abs.md")]
	[TestCase("docs/../../etc/passwd.md")]
	[Description("Hostile paths are rejected by the validator: no file lands on disk and no read succeeds.")]
	public async Task Hostile_Paths_Never_Land_On_Disk(string maliciousPath) {
		MockFileSystem fileSystem = new();
		ComponentRegistryDocsCacheStore store = CreateStore(fileSystem, new FakeTimeProvider());

		await store.WriteAsync("8.2.1", maliciousPath, Encoding.UTF8.GetBytes(SamplePayload), etag: null, lastModified: null, cdnBaseUrl: "https://academy.creatio.com/api/mcp/");
		ComponentRegistryDocsCacheReadResult? read = await store.TryReadAsync("8.2.1", maliciousPath);

		read.Should().BeNull(because: "the path validator must reject traversal attempts before any I/O");
		fileSystem.File.Exists("/etc/passwd").Should().BeFalse(because: "no payload may escape the cache root");
		fileSystem.File.Exists("/etc/passwd.md").Should().BeFalse();
	}

	[Test]
	[Description("Nested doc paths are written under the version directory, preserving the subfolder structure.")]
	public async Task WriteAsync_Preserves_Nested_Subfolders() {
		MockFileSystem fileSystem = new();
		ComponentRegistryDocsCacheStore store = new(fileSystem, new FakeTimeProvider(), "/cache");

		await store.WriteAsync("8.2.1", "docs/widgets/data-grid.component.md", Encoding.UTF8.GetBytes(SamplePayload), etag: null, lastModified: null, cdnBaseUrl: "https://academy.creatio.com/api/mcp/");

		fileSystem.File.Exists("/cache/8.2.1/docs/widgets/data-grid.component.md").Should().BeTrue(
			because: "the cache layout mirrors the docs/ namespace inside the version directory");
	}

	private static ComponentRegistryDocsCacheStore CreateStore(MockFileSystem fileSystem, TimeProvider clock) {
		return new ComponentRegistryDocsCacheStore(fileSystem, clock, "/cache");
	}

	private sealed class FakeTimeProvider : TimeProvider {
		private DateTimeOffset _now = DateTimeOffset.Parse("2026-05-13T10:00:00Z");
		public override DateTimeOffset GetUtcNow() => _now;
		public void SetUtcNow(DateTimeOffset value) => _now = value;
		public void Advance(TimeSpan delta) => _now += delta;
	}
}
