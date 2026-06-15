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
		state.GlobalReferences.Should().NotBeNull(
			because: "the live payload now ships a top-level 'references' block (baseInputs + global typeDefinitions)");
		UnmappedKeys(state.GlobalReferences!.UnmappedExtensions).Should().BeEmpty(
			because: "any new key under root.references.* must be mapped or explicitly allowlisted");

		// Assert — per-component entries.
		foreach (ComponentRegistryEntry entry in state.Entries) {
			UnmappedKeys(entry.UnmappedExtensions).Should().BeEmpty(
				because: $"any new top-level key on entry '{entry.ComponentType}' must be mapped");
			if (entry.References is not null) {
				UnmappedKeys(entry.References.UnmappedExtensions).Should().BeEmpty(
					because: $"any new key under '{entry.ComponentType}'.references.* must be mapped");
			}
		}
	}

	[Test]
	[Description("Detail responses against the live snapshot must merge baseInputs into per-component inputs and resolve the transitive closure of typeDefinitions referenced from the component's inputs/outputs — proving AI receives the relevant globals but is not buried under the full ~190-key dictionary.")]
	public void Live_Snapshot_Detail_Should_Resolve_Referenced_References_Into_Inputs_And_TypeDefinitions() {
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
			globalReferences: state.GlobalReferences);

		// baseInputs (root.references.baseInputs) — keys that should appear on every
		// component's inputs after the merge.
		detail.Inputs.Should().NotBeNull();
		foreach (string inheritedKey in new[] { "classes", "id", "loading", "styles", "tabIndex" }) {
			detail.Inputs!.Should().ContainKey(inheritedKey,
				because: $"'{inheritedKey}' lives under root.references.baseInputs and must inherit onto every component's inputs surface");
		}
		// Per-component input that already existed (sanity-check no regression).
		detail.Inputs!.Should().ContainKey("caption",
			because: "the per-component crt.Button input surface must survive the merge");

		// Global typeDefinitions (root.references.typeDefinitions) — names referenced
		// by per-component output types that AI now needs to resolve.
		detail.References.Should().NotBeNull();
		detail.References!.TypeDefinitions.Should().NotBeNull();
		detail.References.TypeDefinitions!.Should().ContainKey("RequestBindingConfig",
			because: "crt.Button.outputs.clicked.type references 'RequestBindingConfig' — without the global definition AI cannot resolve it");
		// Per-component typeDefinitions still surface alongside the relevant globals.
		detail.References.TypeDefinitions.Should().ContainKey("ButtonIcon",
			because: "the per-component definition must survive the transitive-closure resolution");
		detail.References.TypeDefinitions.Should().ContainKey("ButtonAnimatedIcon",
			because: "the per-component definition must survive the transitive-closure resolution");

		// Closure filter — the global bag has ~190 keys, but AI must only receive
		// the ones crt.Button actually needs. Unrelated types must be filtered out;
		// 'ActivityColumnBinding' lives in root.references.typeDefinitions but no
		// crt.Button input/output/per-component typedef references it.
		detail.References.TypeDefinitions.Should().NotContainKey("ActivityColumnBinding",
			because: "the transitive-closure filter must drop globals that no crt.Button binding references");
		detail.References.TypeDefinitions.Count.Should().BeLessThan(20,
			because: "crt.Button only needs a handful of typedefs reachable from its inputs/outputs/per-component typedefs — surfacing the full ~190-key global bag would defeat the closure filter");
	}

	[Test]
	[Description("The live mobile registry payload (https://academy.creatio.com/api/mcp/latest/MobileComponentRegistry.json, mirrored 2026-05-20) must deserialise through the same wrapped envelope as the web payload with no fields landing on an UnmappedExtensions bucket — the snapshot guard is intentionally symmetric across flavors.")]
	public void Live_Mobile_Registry_Snapshot_Should_Have_No_Unmapped_Fields() {
		// Arrange — the fixture is a verbatim pull of the live academy URL above.
		// Refresh procedure: re-run the curl into the fixture path
		// (see commit message of this test's most recent update).
		string snapshotPath = Path.Combine(
			TestContext.CurrentContext.TestDirectory,
			"Command/McpServer/Fixtures/MobileComponentRegistry.live-snapshot.json");
		File.Exists(snapshotPath).Should().BeTrue(
			because: $"the live mobile fixture must be present at '{snapshotPath}' for this guard to be meaningful");

		// Act
		using FileStream stream = File.OpenRead(snapshotPath);
		ComponentCatalogState state = ComponentInfoCatalog.LoadFromStream(stream);

		// Assert — every component entry must round-trip without leaving fields on
		// the UnmappedExtensions bucket. The producer can evolve the mobile shape
		// freely; this guard locks down silent data loss on the consumer side.
		foreach (ComponentRegistryEntry entry in state.Entries) {
			UnmappedKeys(entry.UnmappedExtensions).Should().BeEmpty(
				because: $"any new top-level key on mobile entry '{entry.ComponentType}' must be mapped");
			if (entry.References is not null) {
				UnmappedKeys(entry.References.UnmappedExtensions).Should().BeEmpty(
					because: $"any new key under mobile '{entry.ComponentType}'.references.* must be mapped");
			}
		}
		state.Entries.Should().NotBeEmpty(
			because: "the live mobile catalog must list at least one component");
	}

	/// <summary>
	/// The confusable set whose selection-metadata presence is gated in the A1 slice (ENG-91571).
	/// These are the components the agent routinely mis-picks between (the ENG-91134 Gallery/grid/
	/// file-list failure class). Presence is snapshot-gated here; the prose quality of the metadata is
	/// signed off separately by a named human owner (umbrella ADR Decision 1, "presence ≠ quality").
	/// The full ~200-catalog backfill is the A2 follow-up.
	/// </summary>
	private static readonly string[] ConfusableSet = {
		"crt.Gallery", "crt.DataGrid", "crt.List", "crt.FileList",
		"crt.MultiList", "crt.ImageInput", "crt.Timeline", "crt.CommunicationOptions"
	};

	[Test]
	[Description("Every confusable-set component in the de-truncated live snapshot must carry non-empty selection metadata (synonyms, useCases, whenToUse) and a taxonomy category — Solution A1's presence bar (ENG-91571). Presence is snapshot-gated; prose quality is human-signed-off separately.")]
	public void Live_Registry_Snapshot_Should_Carry_Selection_Metadata_On_Confusable_Set() {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, SnapshotRelativePath);
		using FileStream stream = File.OpenRead(snapshotPath);
		ComponentCatalogState state = ComponentInfoCatalog.LoadFromStream(stream);

		// Assert
		foreach (string componentType in ConfusableSet) {
			state.Lookup.TryGetValue(componentType, out ComponentRegistryEntry? entry).Should().BeTrue(
				because: $"the confusable component '{componentType}' must be present in the de-truncated snapshot to gate its selection metadata");
			entry!.Synonyms.Should().NotBeEmpty(
				because: $"'{componentType}' must carry synonyms so a natural-language prompt can resolve to it (Solution A1 presence bar)");
			entry.UseCases.Should().NotBeEmpty(
				because: $"'{componentType}' must carry use-cases for Solution B's ranked search to weight (A1 presence bar)");
			entry.WhenToUse.Should().NotBeNullOrWhiteSpace(
				because: $"'{componentType}' must carry a 'when to use' guidance line (Solution A1 presence bar)");
			ComponentCategories.IsKnown(entry.Category).Should().BeTrue(
				because: $"'{componentType}'.category '{entry.Category}' must be a member of the controlled taxonomy A owns");
		}
	}

	[Test]
	[Description("Every non-empty category value present in the live snapshot must be a member of the controlled taxonomy ComponentCategories owns (Solution A, ENG-91571) — guards against drift between producer data and the single-source vocabulary.")]
	public void Live_Registry_Snapshot_Category_Values_Should_Be_In_Controlled_Taxonomy() {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, SnapshotRelativePath);
		using FileStream stream = File.OpenRead(snapshotPath);
		ComponentCatalogState state = ComponentInfoCatalog.LoadFromStream(stream);

		// Assert
		foreach (ComponentRegistryEntry entry in state.Entries) {
			if (string.IsNullOrWhiteSpace(entry.Category)) {
				continue;
			}
			ComponentCategories.IsKnown(entry.Category).Should().BeTrue(
				because: $"'{entry.ComponentType}'.category '{entry.Category}' must be one of the controlled ComponentCategories vocabulary, not free-form text");
		}
	}

	[Test]
	[Description("crt.CommunicationOptions must be flagged not-applicable to custom entities with an explanatory coupling note (Solution A, ENG-91571; ENG-91134 comment 453013) so Solution D's fail-fast UX can steer the agent instead of letting it silently substitute.")]
	public void Live_Snapshot_CommunicationOptions_Should_Be_Flagged_Not_Applicable_To_Custom_Entities() {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, SnapshotRelativePath);
		using FileStream stream = File.OpenRead(snapshotPath);
		ComponentCatalogState state = ComponentInfoCatalog.LoadFromStream(stream);

		// Act
		state.Lookup.TryGetValue("crt.CommunicationOptions", out ComponentRegistryEntry? entry).Should().BeTrue(
			because: "crt.CommunicationOptions is part of the confusable/seed set in the de-truncated snapshot");

		// Assert
		entry!.AppliesToCustomEntities.Should().BeFalse(
			because: "crt.CommunicationOptions is bound to the built-in Contact/Account communication model and cannot be built on a custom entity (ENG-91134 comment 453013)");
		entry.EntityCouplingNote.Should().NotBeNullOrWhiteSpace(
			because: "a restrictive applicability flag must ship with a human-readable reason so the agent can relay it to the user");
	}

	private static IEnumerable<string> UnmappedKeys(IDictionary<string, JsonElement>? bucket) =>
		bucket is null ? System.Array.Empty<string>() : bucket.Keys;
}
