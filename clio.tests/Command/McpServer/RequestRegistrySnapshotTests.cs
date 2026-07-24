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

	[Test]
	[Description("A detail response against the pinned payload must surface the templateId parameter's environment valueSource verbatim at the DATA layer (parameters['templateId'].valueSource.tool == 'list-printables'), pinning the probe-routing contract as structured data rather than only as a guide-text substring.")]
	public void Pinned_Snapshot_Detail_Should_Surface_TemplateId_EnvironmentValueSource_Probe() {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, SnapshotRelativePath);
		using FileStream stream = File.OpenRead(snapshotPath);
		RequestCatalogState state = RequestInfoCatalog.LoadFromStream(stream);
		state.Lookup.TryGetValue("crt.PrintablesRequest", out RequestRegistryEntry? printables).Should().BeTrue(
			because: "crt.PrintablesRequest is the environment-valued request shipped in the pinned payload");

		// Act
		RequestInfoResponse detail = RequestInfoTool.CreateDetailResponse(
			printables!,
			resolvedTargetVersion: state.ResolvedVersion,
			resolvedFrom: "latest-fallback",
			documentation: null,
			globalReferences: state.GlobalReferences);

		// Assert — the valueSource annotation survives as structured data on the parameter blob.
		detail.Parameters.Should().NotBeNull(
			because: "crt.PrintablesRequest declares authorable parameters");
		detail.Parameters!.Should().ContainKey("templateId",
			because: "templateId is the environment-valued parameter a probe fills");
		JsonElement templateId = detail.Parameters["templateId"];
		templateId.TryGetProperty("valueSource", out JsonElement valueSource).Should().BeTrue(
			because: "an environment-valued parameter must carry a valueSource so the agent routes to a probe instead of inventing the value");
		valueSource.GetProperty("kind").GetString().Should().Be("environment",
			because: "the value lives in the target environment, not the static catalog");
		valueSource.GetProperty("tool").GetString().Should().Be("list-printables",
			because: "templateId must be resolved from the list-printables probe - pinned at the data layer, not as a guide-text substring");
	}

	[Test]
	[Description("A detail response against the pinned payload must inline type definitions referenced ONLY through `keyType`/`valueType` strings — crt.OpenPageRequest's parameters map (valueType: JsonData, transitively JsonObject) and the RequestBindingConfig.params wiring hop (valueType: ...RequestParamBindingConfigValue...) — otherwise the response names types it never defines and stops being self-contained.")]
	public void Pinned_Snapshot_Detail_Should_Inline_ValueType_Referenced_TypeDefinitions() {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, SnapshotRelativePath);
		using FileStream stream = File.OpenRead(snapshotPath);
		RequestCatalogState state = RequestInfoCatalog.LoadFromStream(stream);
		state.Lookup.TryGetValue("crt.OpenPageRequest", out RequestRegistryEntry? openPage).Should().BeTrue(
			because: "crt.OpenPageRequest is the pinned entry whose parameters map declares valueType: JsonData");

		// Act
		RequestInfoResponse detail = RequestInfoTool.CreateDetailResponse(
			openPage!,
			resolvedTargetVersion: state.ResolvedVersion,
			resolvedFrom: "latest-fallback",
			documentation: null,
			globalReferences: state.GlobalReferences);

		// Assert — the parameter-level valueType reference resolves...
		detail.References.Should().NotBeNull(
			because: "crt.OpenPageRequest references named types, so the detail must carry a typeDefinitions block");
		detail.References!.TypeDefinitions.Should().ContainKey("JsonData",
			because: "parameters.valueType names JsonData, and a named type must ship its definition");
		// ...transitively through the resolved type's own union string...
		detail.References.TypeDefinitions.Should().ContainKey("JsonObject",
			because: "JsonData's union references JsonObject, so the closure must pull it through");
		// ...and the wiring chain broken at the same valueType hop heals for every request.
		detail.References.TypeDefinitions.Should().ContainKey("RequestParamBindingConfigValue",
			because: "RequestBindingConfig.params references its value type only through a valueType string");
	}

	private const string MobileSnapshotRelativePath = "Command/McpServer/Fixtures/MobileRequestRegistry.live-snapshot.json";

	[Test]
	[Description("The pinned MOBILE request-registry payload (https://academy.creatio.com/api/mcp/latest/MobileRequestRegistry.json) must deserialise through the same wrapped envelope as the web payload with no fields landing on an UnmappedExtensions bucket — the snapshot guard is intentionally symmetric across the web and mobile request flavors, mirroring the component-registry mobile guard.")]
	public void Pinned_Mobile_Request_Registry_Snapshot_Should_Have_No_Unmapped_Fields() {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, MobileSnapshotRelativePath);
		File.Exists(snapshotPath).Should().BeTrue(
			because: $"the mobile snapshot fixture must be present at '{snapshotPath}' for this guard to be meaningful");

		// Act
		using FileStream stream = File.OpenRead(snapshotPath);
		RequestCatalogState state = RequestInfoCatalog.LoadFromStream(stream);

		// Assert — root-level envelope.
		state.GlobalReferences.Should().NotBeNull(
			because: "the mobile payload ships a top-level 'references' block (baseParameters + global typeDefinitions)");
		UnmappedKeys(state.GlobalReferences!.UnmappedExtensions).Should().BeEmpty(
			because: "any new key under root.references.* on the mobile registry must be mapped or explicitly allowlisted");

		// Assert — per-request entries.
		state.Entries.Should().NotBeEmpty(
			because: "the pinned mobile catalog must list at least one request");
		foreach (RequestRegistryEntry entry in state.Entries) {
			UnmappedKeys(entry.UnmappedExtensions).Should().BeEmpty(
				because: $"any new top-level key on mobile entry '{entry.RequestType}' must be mapped");
			if (entry.References is not null) {
				UnmappedKeys(entry.References.UnmappedExtensions).Should().BeEmpty(
					because: $"any new key under mobile '{entry.RequestType}'.references.* must be mapped");
			}
		}
	}

	[Test]
	[Description("A detail response against the pinned MOBILE payload keeps the platform-injected baseParameters separate from the authorable parameters map and surfaces the mobile-only crt.RunBusinessProcessRequest.activeRow parameter — proving the mobile registry carries a parameter surface distinct from desktop and that it flows through the shared detail factory unchanged.")]
	public void Pinned_Mobile_Snapshot_Detail_Should_Surface_MobileOnly_Parameter_And_Keep_BaseParameters_Separate() {
		// Arrange
		string snapshotPath = Path.Combine(TestContext.CurrentContext.TestDirectory, MobileSnapshotRelativePath);
		using FileStream stream = File.OpenRead(snapshotPath);
		RequestCatalogState state = RequestInfoCatalog.LoadFromStream(stream);
		state.Lookup.TryGetValue("crt.RunBusinessProcessRequest", out RequestRegistryEntry? runProcess).Should().BeTrue(
			because: "crt.RunBusinessProcessRequest is shipped in the pinned mobile payload");

		// Act
		RequestInfoResponse detail = RequestInfoTool.CreateDetailResponse(
			runProcess!,
			resolvedTargetVersion: state.ResolvedVersion,
			resolvedFrom: "latest-fallback",
			documentation: null,
			globalReferences: state.GlobalReferences);

		// Assert — the mobile-only parameter is present on the authorable surface.
		detail.Parameters.Should().NotBeNull(
			because: "crt.RunBusinessProcessRequest declares authorable parameters on mobile");
		detail.Parameters!.Should().ContainKey("activeRow",
			because: "activeRow is a mobile-only parameter with no desktop twin — it must surface from the mobile registry");
		// Assert — platform-injected base fields stay separate, never merged into parameters.
		detail.BaseParameters.Should().NotBeNull(
			because: "root.references.baseParameters must surface as its own field on the mobile flavor too");
		detail.BaseParameters!.Should().ContainKey("$context",
			because: "the platform-injected context is part of the published mobile base surface");
		detail.Parameters.Should().NotContainKey("$context",
			because: "platform-injected fields must never leak into the authorable parameters map");
		// Assert — wiring contract inlined via the closure seed.
		detail.References!.TypeDefinitions.Should().ContainKey("RequestBindingConfig",
			because: "every request is wired through RequestBindingConfig, so the mobile detail inlines its schema");
	}

	private static IEnumerable<string> UnmappedKeys(IDictionary<string, JsonElement>? bucket) =>
		bucket is null ? System.Array.Empty<string>() : bucket.Keys;
}
