using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Snapshot guard against silent data loss in the registry deserialiser.
/// Every key the static-files-mcp producer publishes under
/// <c>https://academy.creatio.com/api/mcp/latest/ComponentRegistry.json</c> must
/// be either mapped to a POCO field or land on an explicit
/// <see cref="System.Text.Json.Serialization.JsonExtensionDataAttribute"/> bucket
/// that this test inspects. If the producer adds a new field, the bucket will
/// be non-empty and the test fails — forcing a deliberate decision rather than
/// the field being dropped to <c>/dev/null</c>.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ComponentRegistrySnapshotTests {
	private const string SnapshotRelativePath = "Command/McpServer/Fixtures/ComponentRegistry.live-snapshot.json";

	/// <summary>
	/// Refreshing the snapshot: from the repo root, run
	/// <code>curl -s "https://academy.creatio.com/api/mcp/latest/ComponentRegistry.json" \
	///   > clio.tests/Command/McpServer/Fixtures/ComponentRegistry.live-snapshot.json</code>
	/// then re-run this test. If it still fails, the producer added a new field
	/// — map it on the POCOs in <c>ComponentInfoTool.cs</c> and surface it through
	/// <c>CreateDetailResponse</c>.
	/// </summary>
	[Test]
	[Description("The pinned live payload must deserialise without leaving any field on an UnmappedExtensions bucket — that bucket is the canary for silent data loss when the producer schema evolves.")]
	public void Live_Registry_Snapshot_Should_Have_No_Unmapped_Fields() {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, SnapshotRelativePath);
		File.Exists(snapshotPath).Should().BeTrue(
			because: $"the snapshot fixture must be present at '{snapshotPath}' for this guard to be meaningful");

		// Act
		using FileStream stream = File.OpenRead(snapshotPath);
		ComponentCatalogState state = ComponentInfoCatalog.LoadFromStream(stream);

		// Assert — root-level envelope.
		state.GlobalContent.Should().NotBeNull(
			because: "the live payload now ships a top-level 'content' block (baseInputs + global typeDefinitions)");
		UnmappedKeys(state.GlobalContent!.UnmappedExtensions).Should().BeEmpty(
			because: "any new key under root.content.* must be mapped or explicitly allowlisted");

		// Assert — per-component entries.
		foreach (ComponentRegistryEntry entry in state.Entries) {
			UnmappedKeys(entry.UnmappedExtensions).Should().BeEmpty(
				because: $"any new top-level key on entry '{entry.ComponentType}' must be mapped");
			if (entry.Content is not null) {
				UnmappedKeys(entry.Content.UnmappedExtensions).Should().BeEmpty(
					because: $"any new key under '{entry.ComponentType}'.content.* must be mapped");
			}
		}
	}

	[Test]
	[Description("Detail responses against the live snapshot must merge baseInputs into per-component inputs and global typeDefinitions into per-component typeDefinitions — proving the producer's globals reach AI.")]
	public void Live_Snapshot_Detail_Should_Merge_Global_Content_Into_Inputs_And_TypeDefinitions() {
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, SnapshotRelativePath);
		using FileStream stream = File.OpenRead(snapshotPath);
		ComponentCatalogState state = ComponentInfoCatalog.LoadFromStream(stream);

		state.Lookup.TryGetValue("crt.Button", out ComponentRegistryEntry? button).Should().BeTrue(
			because: "crt.Button is shipped in the pinned live snapshot");

		ComponentInfoResponse detail = ComponentInfoTool.CreateDetailResponse(
			button!,
			resolvedTargetVersion: state.ResolvedVersion,
			resolvedFrom: "latest-fallback",
			documentation: null,
			globalContent: state.GlobalContent);

		// baseInputs (root.content.baseInputs) — keys that should appear on every
		// component's inputs after the merge.
		detail.Inputs.Should().NotBeNull();
		foreach (string inheritedKey in new[] { "classes", "id", "loading", "styles", "tabIndex" }) {
			detail.Inputs!.Should().ContainKey(inheritedKey,
				because: $"'{inheritedKey}' lives under root.content.baseInputs and must inherit onto every component's inputs surface");
		}
		// Per-component input that already existed (sanity-check no regression).
		detail.Inputs!.Should().ContainKey("caption",
			because: "the per-component crt.Button input surface must survive the merge");

		// Global typeDefinitions (root.content.typeDefinitions) — names referenced
		// by per-component output types that AI now needs to resolve.
		detail.Content.Should().NotBeNull();
		detail.Content!.TypeDefinitions.Should().NotBeNull();
		detail.Content.TypeDefinitions!.Should().ContainKey("RequestBindingConfig",
			because: "crt.Button.outputs.clicked.type references 'RequestBindingConfig' — without the global definition AI cannot resolve it");
		// Per-component typeDefinition still surfaces alongside the globals.
		detail.Content.TypeDefinitions.Should().ContainKey("ButtonIcon",
			because: "the per-component definition must survive the merge with the globals");
	}

	[Test]
	[Description("The bundled mobile registry payload (which backs `get-component-info schema-type=mobile` until the producer publishes MobileComponentRegistry.json to academy.creatio.com) must deserialise through the same wrapped envelope as the web payload with no fields landing on an UnmappedExtensions bucket — the snapshot guard is intentionally symmetric across flavors.")]
	public void Bundled_Mobile_Snapshot_Should_Have_No_Unmapped_Fields() {
		// Arrange — the fixture is a verbatim copy of
		// clio/Command/McpServer/Data/MobileComponentRegistry.json checked in at
		// commit time. Refresh procedure documented at the top of the file.
		string snapshotPath = Path.Combine(
			TestContext.CurrentContext.TestDirectory,
			"Command/McpServer/Fixtures/MobileComponentRegistry.bundled-snapshot.json");
		File.Exists(snapshotPath).Should().BeTrue(
			because: $"the bundled mobile fixture must be present at '{snapshotPath}' for this guard to be meaningful");

		// Act
		using FileStream stream = File.OpenRead(snapshotPath);
		ComponentCatalogState state = ComponentInfoCatalog.LoadFromStream(stream);

		// Assert — every component entry must round-trip without leaving fields on
		// the UnmappedExtensions bucket. Mobile entries are in the legacy
		// (properties/category) shape today; the snapshot pins that fact so a
		// future edit cannot accidentally drop a field on the floor.
		foreach (ComponentRegistryEntry entry in state.Entries) {
			UnmappedKeys(entry.UnmappedExtensions).Should().BeEmpty(
				because: $"any new top-level key on mobile entry '{entry.ComponentType}' must be mapped");
			if (entry.Content is not null) {
				UnmappedKeys(entry.Content.UnmappedExtensions).Should().BeEmpty(
					because: $"any new key under mobile '{entry.ComponentType}'.content.* must be mapped");
			}
		}
		state.Entries.Should().NotBeEmpty(
			because: "the bundled mobile catalog must list at least one component");
	}

	private static IEnumerable<string> UnmappedKeys(IDictionary<string, JsonElement>? bucket) =>
		bucket is null ? System.Array.Empty<string>() : bucket.Keys;
}
