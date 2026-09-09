using System.Collections.Generic;
using System.IO;
using System.Text;
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
		EnvelopeUnmappedKeys(snapshotPath).Should().BeEmpty(
			because: "a new key at the ROOT of the payload lands on the envelope's own bucket, which no "
				+ "caller reads and, until this assertion, no test inspected either");
		state.GlobalReferences.Should().NotBeNull(
			because: "the live payload now ships a top-level 'references' block (baseInputs + global typeDefinitions)");
		UnmappedKeys(state.GlobalReferences!.UnmappedExtensions).Should().BeEmpty(
			because: "any new key under root.references.* must be mapped or explicitly allowlisted");

		// Assert — count floor guarding the de-truncated fixture (ENG-91571 DoD).
		// Without it the per-component loop below passes vacuously over any count, so a
		// silent regression of the live fixture back to the old ~5 curated entries would
		// keep every web snapshot test green — re-opening exactly the gap this PR closed.
		state.Entries.Count.Should().BeGreaterThan(150,
			because: "the ~200-component live catalog is the ENG-91571 DoD — a regression to the old ~5-entry curated set must fail this guard");

		// Assert — per-component entries.
		foreach (ComponentRegistryEntry entry in state.Entries) {
			UnmappedKeys(entry.UnmappedExtensions).Should().BeEmpty(
				because: $"any new top-level key on entry '{entry.ComponentType}' must be mapped");
			if (entry.References is not null) {
				UnmappedKeys(entry.References.UnmappedExtensions).Should().BeEmpty(
					because: $"any new key under '{entry.ComponentType}'.references.* must be mapped");
			}
		}

		// Assert — composites (top-level `composites[]`). The guard is symmetric with the
		// per-component check so a producer adding a key under a composite cannot drop it
		// silently. No-op until the live fixture is refreshed to a payload that ships
		// composites, but it locks the contract the moment one appears.
		foreach (CompositeDefinition composite in state.Composites ?? System.Array.Empty<CompositeDefinition>()) {
			UnmappedKeys(composite.UnmappedExtensions).Should().BeEmpty(
				because: $"any new key on composite '{composite.Caption}' must be mapped");
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
		foreach (string inheritedKey in new[] { "classes", "id", "styles", "tabIndex" }) {
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
	[Description("A detail response for a component that publishes Solution A selection-metadata (crt.DataGrid) must surface whenToUse/whenNotToUse/synonyms/useCases/appliesToCustomEntities through CreateDetailResponse — proving the producer's @whenToUse-family JSDoc tags reach the AI consumer instead of being dropped to the UnmappedExtensions bucket.")]
	public void Live_Snapshot_Detail_Should_Surface_Selection_Metadata_When_Producer_Publishes_It() {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, SnapshotRelativePath);
		using FileStream stream = File.OpenRead(snapshotPath);
		ComponentCatalogState state = ComponentInfoCatalog.LoadFromStream(stream);
		state.Lookup.TryGetValue("crt.DataGrid", out ComponentRegistryEntry? dataGrid).Should().BeTrue(
			because: "crt.DataGrid ships Solution A selection-metadata in the pinned live snapshot");

		// Act
		ComponentInfoResponse detail = ComponentInfoTool.CreateDetailResponse(
			dataGrid!,
			resolvedTargetVersion: state.ResolvedVersion,
			resolvedFrom: "latest-fallback",
			documentation: null,
			globalReferences: state.GlobalReferences);

		// Assert
		detail.WhenToUse.Should().NotBeNullOrWhiteSpace(
			because: "the producer publishes @whenToUse on crt.DataGrid and clio must surface it (Solution A, ENG-91571)");
		detail.WhenNotToUse.Should().NotBeNullOrWhiteSpace(
			because: "crt.DataGrid publishes @whenNotToUse to steer the agent away from image/list use-cases");
		detail.Synonyms.Should().NotBeNullOrEmpty(
			because: "crt.DataGrid publishes @synonym tags that must surface so list-mode discovery can match informal names");
		detail.UseCases.Should().NotBeNullOrEmpty(
			because: "crt.DataGrid publishes @useCase tags describing concrete scenarios it fits");
		detail.AppliesToCustomEntities.Should().BeTrue(
			because: "crt.DataGrid is buildable on a custom entity, so the published applicability flag must round-trip");
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
		EnvelopeUnmappedKeys(snapshotPath).Should().BeEmpty(
			because: "the mobile producer stamps provenance at the ROOT of the payload, so that is where "
				+ "an unmapped key appears first");
		// A floor, not NotBeEmpty: the web guard has carried one since ENG-91571 and this one did not,
		// so the fixture could rot from the live 46 down to a handful and stay green. 30 sits below the
		// 35 it pins today, which is itself behind the live catalog: raise it when the fixture is
		// refreshed at cutover.
		state.Entries.Count.Should().BeGreaterThan(30,
			because: "a fixture that quietly shrank would leave every per-entry assertion above passing "
				+ "vacuously, which is the asymmetry this guard had against the web one");
	}

	[Test]
	[Description("A synthetic payload that actually ships a top-level composites[] entry and a compositeOnly component must deserialise with no fields on an UnmappedExtensions bucket. The live snapshot ships no composites yet, so the composite arm of the guard in Live_Registry_Snapshot_... is a no-op today — this test exercises that arm for real so a producer renaming/adding a key under a composite is caught.")]
	public void Synthetic_Composite_Payload_Should_Parse_With_No_Unmapped_Fields() {
		// A minimal wrapped envelope carrying one top-level composite and one compositeOnly
		// component — the composite-bearing shape the live snapshot does not yet contain.
		const string payload = """
		{
		  "components": [
		    { "componentType": "crt.ExpansionPanel", "category": "containers", "description": "Collapsible panel.", "container": true, "properties": {} },
		    { "componentType": "crt.NextSteps", "category": "interactive", "description": "Next steps widget.", "compositeOnly": true, "properties": {} }
		  ],
		  "composites": [
		    { "caption": "Next steps", "description": "Expansion panel wrapping a crt.NextSteps list.", "docs": ["docs/expansion-panel-next-steps.component.md"] }
		  ]
		}
		""";
		using MemoryStream stream = new(Encoding.UTF8.GetBytes(payload));
		ComponentCatalogState state = ComponentInfoCatalog.LoadFromStream(stream);

		// The composite arm of the guard now actually iterates (proving it is not dead).
		state.Composites.Should().NotBeNull(because: "the payload ships a top-level composites[] array");
		state.Composites!.Should().ContainSingle(because: "exactly one composite is declared")
			.Which.Caption.Should().Be("Next steps");
		state.Lookup["crt.NextSteps"].CompositeOnly.Should().BeTrue(
			because: "the compositeOnly component flag must round-trip through deserialisation");
		foreach (CompositeDefinition composite in state.Composites!) {
			UnmappedKeys(composite.UnmappedExtensions).Should().BeEmpty(
				because: $"every key on composite '{composite.Caption}' must be mapped, not dropped to an UnmappedExtensions bucket");
		}
	}

	/// <summary>
	/// The envelope's OWN <c>UnmappedExtensions</c> bucket, which no other assertion can reach.
	/// </summary>
	/// <remarks>
	/// <see cref="ComponentInfoCatalog"/> deserialises the envelope, keeps
	/// <c>Components</c> / <c>References</c> / <c>Composites</c> and drops the envelope object, and
	/// <see cref="ComponentCatalogState"/> has no field for the bucket. So a new ROOT-LEVEL producer key
	/// reached neither a caller nor a test, while the doc comment on the bucket claimed this guard covered
	/// it. Root level is where the next key lands: the mobile producer stamps provenance there.
	/// </remarks>
	private static IEnumerable<string> EnvelopeUnmappedKeys(string snapshotPath) {
		using FileStream stream = File.OpenRead(snapshotPath);
		ComponentRegistryEnvelope? envelope = JsonSerializer.Deserialize<ComponentRegistryEnvelope>(
			stream,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
		envelope.Should().NotBeNull(because: $"'{snapshotPath}' must deserialise as a wrapped envelope");
		return UnmappedKeys(envelope!.UnmappedExtensions);
	}

	private static IEnumerable<string> UnmappedKeys(IDictionary<string, JsonElement>? bucket) =>
		bucket is null ? System.Array.Empty<string>() : bucket.Keys;
}
