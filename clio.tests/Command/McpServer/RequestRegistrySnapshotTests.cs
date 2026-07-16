using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Snapshot guard against silent data loss in the request-registry deserialiser.
/// Every key the static-files-mcp producer publishes under
/// <c>https://academy.creatio.com/api/mcp/latest/RequestRegistry.json</c> must be
/// either mapped to a POCO field or land on an explicit
/// <see cref="System.Text.Json.Serialization.JsonExtensionDataAttribute"/> bucket
/// that this test inspects — mirroring the component-registry guard.
/// OOTB button-action requests initiative (ENG-93187).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class RequestRegistrySnapshotTests {
	private const string SnapshotRelativePath = "Command/McpServer/Fixtures/RequestRegistry.live-snapshot.json";

	/// <summary>
	/// Refreshing the snapshot: from the repo root, run
	/// <code>curl -s "https://academy.creatio.com/api/mcp/latest/RequestRegistry.json" \
	///   > clio.tests/Command/McpServer/Fixtures/RequestRegistry.live-snapshot.json</code>
	/// then re-run this test. Until the producer publishes the file to the academy CDN,
	/// the fixture pins the authored payload from the <c>static-files-mcp</c> repository
	/// (<c>latest/RequestRegistry.json</c>) — the same bytes the CDN will serve.
	/// </summary>
	[Test]
	[Description("The pinned request-registry payload must deserialise without leaving any field on an UnmappedExtensions bucket — that bucket is the canary for silent data loss when the producer schema evolves.")]
	public void Pinned_Request_Registry_Snapshot_Should_Have_No_Unmapped_Fields() {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, SnapshotRelativePath);
		File.Exists(snapshotPath).Should().BeTrue(
			because: $"the snapshot fixture must be present at '{snapshotPath}' for this guard to be meaningful");

		// Act
		using FileStream stream = File.OpenRead(snapshotPath);
		RequestCatalogState state = RequestInfoCatalog.LoadFromStream(stream);

		// Assert — root-level envelope.
		state.GlobalReferences.Should().NotBeNull(
			because: "the payload ships a top-level 'references' block (baseParameters + global typeDefinitions)");
		UnmappedKeys(state.GlobalReferences!.UnmappedExtensions).Should().BeEmpty(
			because: "any new key under root.references.* must be mapped or explicitly allowlisted");

		// Assert — per-request entries.
		state.Entries.Should().NotBeEmpty(
			because: "the pinned catalog must list at least one request");
		foreach (RequestRegistryEntry entry in state.Entries) {
			UnmappedKeys(entry.UnmappedExtensions).Should().BeEmpty(
				because: $"any new top-level key on entry '{entry.RequestType}' must be mapped");
			if (entry.References is not null) {
				UnmappedKeys(entry.References.UnmappedExtensions).Should().BeEmpty(
					because: $"any new key under '{entry.RequestType}'.references.* must be mapped");
			}
		}
	}

	[Test]
	[Description("A detail response against the pinned payload must keep the platform-injected baseParameters SEPARATE from the authorable parameters map (deliberate divergence from the component catalog's baseInputs merge) and inline the RequestBindingConfig wiring contract through the type-definition closure.")]
	public void Pinned_Snapshot_Detail_Should_Keep_BaseParameters_Separate_And_Resolve_Wiring_TypeDefinitions() {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, SnapshotRelativePath);
		using FileStream stream = File.OpenRead(snapshotPath);
		RequestCatalogState state = RequestInfoCatalog.LoadFromStream(stream);
		state.Lookup.TryGetValue("crt.ClosePageRequest", out RequestRegistryEntry? closePage).Should().BeTrue(
			because: "crt.ClosePageRequest is the pilot entry shipped in the pinned payload");

		// Act
		RequestInfoResponse detail = RequestInfoTool.CreateDetailResponse(
			closePage!,
			resolvedTargetVersion: state.ResolvedVersion,
			resolvedFrom: "latest-fallback",
			documentation: null,
			globalReferences: state.GlobalReferences);

		// Assert — the authorable surface: explicitly empty, never inflated by base fields.
		detail.Parameters.Should().NotBeNull(
			because: "the producer publishes an explicit empty parameters map on crt.ClosePageRequest");
		detail.Parameters.Should().BeEmpty(
			because: "crt.ClosePageRequest accepts no authorable parameters");
		detail.BaseParameters.Should().NotBeNull(
			because: "root.references.baseParameters must surface as its own field");
		detail.BaseParameters!.Should().ContainKey("$context",
			because: "the platform-injected context is part of the published base surface");
		detail.Parameters.Should().NotContainKey("$context",
			because: "platform-injected fields must never leak into the authorable parameters map");

		// Assert — wiring contract inlined via the closure seed.
		detail.References.Should().NotBeNull(
			because: "the payload publishes global typeDefinitions reachable from the wiring seed");
		detail.References!.TypeDefinitions.Should().ContainKey("RequestBindingConfig",
			because: "every request is wired through RequestBindingConfig, so the detail response inlines its schema");
	}

	private static IEnumerable<string> UnmappedKeys(IDictionary<string, JsonElement>? bucket) =>
		bucket is null ? System.Array.Empty<string>() : bucket.Keys;
}
