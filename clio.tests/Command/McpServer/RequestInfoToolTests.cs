using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class RequestInfoToolTests {
	private const string CloseDocPath = "request-docs/close-page.request.md";

	private const string TestRegistryJson = """
	{
	  "requests": [
	    {
	      "requestType": "crt.ClosePageRequest",
	      "parameters": {},
	      "description": "Closes the currently open page.",
	      "references": {
	        "docs": ["request-docs/close-page.request.md"]
	      }
	    },
	    {
	      "requestType": "crt.DeleteRecordRequest",
	      "parameters": {
	        "recordId": { "type": "string", "required": true, "description": "Identifier of the record to delete." },
	        "itemsAttributeName": { "type": "string", "description": "Collection attribute holding the record." }
	      },
	      "description": "Deletes a single record from a collection."
	    },
	    {
	      "requestType": "crt.CancelRecordChangesRequest",
	      "description": "Cancels changes for the current record editor."
	    }
	  ],
	  "references": {
	    "baseParameters": {
	      "$context": { "type": "ViewModelContext", "description": "Platform-injected view-model context." },
	      "scopes": { "type": "array", "items": { "type": "string" }, "description": "Platform-populated scope chain." },
	      "type": { "type": "string", "description": "Request type discriminator." }
	    },
	    "typeDefinitions": {
	      "RequestBindingConfig": {
	        "fields": {
	          "params": { "type": "Record", "keyType": "string", "valueType": "string | boolean | number" },
	          "request": { "type": "string", "required": true }
	        }
	      },
	      "UnrelatedType": {
	        "fields": { "noise": { "type": "string" } }
	      }
	    }
	  }
	}
	""";

	/// <summary>
	/// Mobile request registry served to the mobile catalog. Deliberately a DIFFERENT set from
	/// <see cref="TestRegistryJson"/> so a schema-type=mobile call is proven to read the mobile
	/// registry, not the web one: it drops the web-only crt.DeleteRecordRequest and adds a
	/// crt.RunBusinessProcessRequest whose mobile-only activeRow parameter has no desktop twin.
	/// </summary>
	private const string TestMobileRegistryJson = """
	{
	  "requests": [
	    {
	      "requestType": "crt.ClosePageRequest",
	      "parameters": {},
	      "description": "Closes the currently open mobile page."
	    },
	    {
	      "requestType": "crt.RunBusinessProcessRequest",
	      "parameters": {
	        "processName": { "type": "string", "required": true, "description": "Code of the business process to run." },
	        "activeRow": { "type": "string", "description": "Mobile-only: current row context on a mobile list." }
	      },
	      "description": "Runs a business process from a mobile page."
	    }
	  ],
	  "references": {
	    "baseParameters": {
	      "$context": { "type": "ViewModelContext", "description": "Platform-injected view-model context." }
	    },
	    "typeDefinitions": {
	      "RequestBindingConfig": {
	        "fields": {
	          "request": { "type": "string", "required": true }
	        }
	      }
	    }
	  }
	}
	""";

	[Test]
	[Description("Omitting request-type returns list mode with every cataloged request, ordered alphabetically, and honest latest-fallback version markers on a bare call.")]
	public async Task GetRequestInfo_ShouldReturnFullCatalog_WhenRequestTypeIsOmitted() {
		// Arrange
		RequestInfoTool tool = CreateTool();

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(new RequestInfoArgs());

		// Assert
		response.Success.Should().BeTrue(because: "listing a valid catalog must succeed");
		response.Mode.Should().Be("list", because: "omitting request-type selects list mode");
		response.Count.Should().Be(3, because: "the test registry declares exactly three requests");
		response.Items.Should().NotBeNull(because: "list mode always returns an items array, never null");
		response.Items![0].RequestType.Should().Be("crt.CancelRecordChangesRequest",
			because: "list items are ordered alphabetically by request type");
		response.ResolvedFrom.Should().Be("latest-fallback",
			because: "a bare call carries no environment or version, so the version cannot be determined");
		response.ResolvedFromReason.Should().Be("no-active-environment",
			because: "the fallback reason must distinguish the input gap from a probe error");
		response.VersionWarning.Should().NotBeNullOrEmpty(
			because: "latest-fallback responses must warn that the catalog is a superset");
		response.RequiresVersionConfirmation.Should().BeTrue(
			because: "the machine-readable hard-stop flag must accompany latest-fallback");
	}

	[Test]
	[Description("Passing the literal 'list' as request-type behaves exactly like omitting it — both select list mode.")]
	public async Task GetRequestInfo_ShouldReturnFullCatalog_WhenRequestTypeIsListLiteral() {
		// Arrange
		RequestInfoTool tool = CreateTool();

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(new RequestInfoArgs("list"));

		// Assert
		response.Success.Should().BeTrue(because: "the 'list' literal is a documented list-mode selector");
		response.Mode.Should().Be("list", because: "'list' selects list mode, not a lookup of a request named 'list'");
		response.Count.Should().Be(3, because: "list mode returns the full test catalog");
	}

	[Test]
	[Description("The search filter narrows the list by request type, description, and parameter keys.")]
	public async Task GetRequestInfo_ShouldFilterCatalog_WhenSearchIsProvided() {
		// Arrange
		RequestInfoTool tool = CreateTool();

		// Act
		RequestInfoResponse byType = await tool.GetRequestInfo(new RequestInfoArgs(Search: "delete"));
		RequestInfoResponse byParameterKey = await tool.GetRequestInfo(new RequestInfoArgs(Search: "recordId"));

		// Assert
		byType.Count.Should().Be(1, because: "only crt.DeleteRecordRequest matches 'delete'");
		byType.Items![0].RequestType.Should().Be("crt.DeleteRecordRequest",
			because: "the type-name match must survive the filter");
		byParameterKey.Count.Should().Be(1,
			because: "parameter keys are a positive search signal, mirroring the component catalog's input-key search");
		byParameterKey.Items![0].RequestType.Should().Be("crt.DeleteRecordRequest",
			because: "'recordId' is declared only on crt.DeleteRecordRequest");
	}

	[Test]
	[Description("A known request type returns detail mode with the parameters map surfaced verbatim — including an explicitly EMPTY map, which means 'accepts no parameters' rather than 'unknown'.")]
	public async Task GetRequestInfo_ShouldReturnDetailWithEmptyParameters_WhenRequestDeclaresNone() {
		// Arrange
		RequestInfoTool tool = CreateTool();

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(new RequestInfoArgs("crt.ClosePageRequest"));

		// Assert
		response.Success.Should().BeTrue(because: "crt.ClosePageRequest exists in the catalog");
		response.Mode.Should().Be("detail", because: "a known type selects detail mode");
		response.RequestType.Should().Be("crt.ClosePageRequest", because: "the detail response echoes the requested type");
		response.Description.Should().Be("Closes the currently open page.",
			because: "the producer description must surface verbatim");
		response.Parameters.Should().NotBeNull(
			because: "the entry explicitly declares 'parameters': {} and the empty map is a meaningful contract");
		response.Parameters.Should().BeEmpty(
			because: "crt.ClosePageRequest accepts no parameters — an AI consumer must see that explicitly");
	}

	[Test]
	[Description("baseParameters are surfaced as a SEPARATE field and never merged into parameters — $context/scopes/type are platform-injected, and merging would teach an AI consumer to author them via the binding's params block. This is a deliberate divergence from the component catalog's baseInputs merge.")]
	public async Task GetRequestInfo_ShouldSurfaceBaseParametersSeparately_WhenGlobalReferencesArePublished() {
		// Arrange
		RequestInfoTool tool = CreateTool();

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(new RequestInfoArgs("crt.ClosePageRequest"));

		// Assert
		response.BaseParameters.Should().NotBeNull(
			because: "the registry publishes root references.baseParameters");
		response.BaseParameters!.Should().ContainKey("$context",
			because: "the platform-injected context field must be visible for handler-authoring context");
		response.Parameters.Should().NotContainKey("$context",
			because: "baseParameters must NOT merge into parameters — the request accepts no authorable params");
		response.Parameters.Should().NotContainKey("scopes",
			because: "platform-populated fields never belong to the authorable parameters surface");
	}

	[Test]
	[Description("The detail response inlines the RequestBindingConfig wiring contract through the type-definition closure even for a parameterless request, while unrelated global types are filtered out.")]
	public async Task GetRequestInfo_ShouldResolveWiringTypeDefinitions_WhenDetailIsReturned() {
		// Arrange
		RequestInfoTool tool = CreateTool();

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(new RequestInfoArgs("crt.ClosePageRequest"));

		// Assert
		response.References.Should().NotBeNull(
			because: "the registry publishes a global typeDefinitions bag reachable from the wiring seed");
		response.References!.TypeDefinitions.Should().ContainKey("RequestBindingConfig",
			because: "every request is wired through RequestBindingConfig, so the detail response is seeded with it");
		response.References.TypeDefinitions.Should().NotContainKey("UnrelatedType",
			because: "the transitive-closure filter must drop globals the request's surface does not reference");
	}

	[Test]
	[Description("Documentation is fetched through the shared docs pipeline using the resolved catalog version and the raw registry path, then surfaced on the detail response.")]
	public async Task GetRequestInfo_ShouldLoadDocumentation_WhenEntryDeclaresDocs() {
		// Arrange
		FakeDocsClient docsClient = new();
		docsClient.Seed("latest", CloseDocPath, "# How to Wire the Close Page Request");
		RequestInfoTool tool = CreateTool(docsClient);

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(new RequestInfoArgs("crt.ClosePageRequest"));

		// Assert
		response.Documentation.Should().Contain("# How to Wire the Close Page Request",
			because: "the fetched markdown must surface on the detail response");
		response.DocumentationUnavailable.Should().BeNull(
			because: "all declared docs loaded, so no fetch-failure flag is emitted");
		docsClient.Requests.Should().ContainSingle(
				because: "exactly one doc path is declared on the entry")
			.Which.Should().Be(("latest", CloseDocPath),
				because: "the docs pipeline must receive the resolved version and the raw registry path");
	}

	[Test]
	[Description("When the entry declares docs but every fetch fails, the response sets documentationUnavailable so the agent can distinguish a transient fetch failure from a request that ships no docs.")]
	public async Task GetRequestInfo_ShouldFlagDocumentationUnavailable_WhenDeclaredDocsFailToLoad() {
		// Arrange — the fake docs client is not seeded, so every fetch returns null.
		RequestInfoTool tool = CreateTool(new FakeDocsClient());

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(new RequestInfoArgs("crt.ClosePageRequest"));

		// Assert
		response.Success.Should().BeTrue(
			because: "docs are a partial-failure surface — the rest of the detail response stays intact");
		response.Documentation.Should().BeNull(
			because: "no doc block could be fetched");
		response.DocumentationUnavailable.Should().BeTrue(
			because: "the entry declares docs, so their absence is a fetch failure, not a no-docs request");
	}

	[Test]
	[Description("A request without declared docs omits both documentation fields — absence of docs is not an error condition.")]
	public async Task GetRequestInfo_ShouldOmitDocumentationFields_WhenEntryDeclaresNoDocs() {
		// Arrange
		RequestInfoTool tool = CreateTool();

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(new RequestInfoArgs("crt.CancelRecordChangesRequest"));

		// Assert
		response.Success.Should().BeTrue(because: "the request exists in the catalog");
		response.Documentation.Should().BeNull(because: "the entry declares no docs");
		response.DocumentationUnavailable.Should().BeNull(
			because: "no docs were declared, so there is no fetch failure to flag");
	}

	[Test]
	[Description("An unknown request type returns a bounded closest-by-distance suggestion shortlist instead of the full catalog.")]
	public async Task GetRequestInfo_ShouldReturnSuggestions_WhenRequestTypeIsUnknown() {
		// Arrange
		RequestInfoTool tool = CreateTool();

		// Act — a typo that matches nothing by name/description substring.
		RequestInfoResponse response = await tool.GetRequestInfo(new RequestInfoArgs("crt.ClosePagRequest"));

		// Assert
		response.Success.Should().BeFalse(because: "the requested type does not exist in the catalog");
		response.Mode.Should().Be("list", because: "not-found responses fall back to the list envelope");
		response.Error.Should().Contain("crt.ClosePagRequest",
			because: "the error must echo the unknown type so the agent sees what missed");
		response.Items.Should().NotBeEmpty(because: "a closest-by-distance shortlist helps the agent recover");
		response.Items!.Should().Contain(item => item.RequestType == "crt.ClosePageRequest",
			because: "the closest known type by edit distance must be in the shortlist (items are re-sorted alphabetically for display, mirroring the component catalog)");
		response.Items!.Count.Should().BeLessThanOrEqualTo(8,
			because: "the shortlist is capped so a not-found response never echoes the full catalog");
	}

	[Test]
	[Description("An unknown request-type that matches entries by name/description returns those matches as suggestions — the agent typically reaches for a human label rather than the exact type id.")]
	public async Task GetRequestInfo_ShouldReturnNameMatches_WhenUnknownTypeMatchesByKeyword() {
		// Arrange
		RequestInfoTool tool = CreateTool();

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(new RequestInfoArgs("delete record"));

		// Assert
		response.Success.Should().BeFalse(because: "'delete record' is not a request type");
		response.Items.Should().NotBeEmpty(because: "keyword matches must surface as suggestions");
		response.Items!.Should().Contain(item => item.RequestType == "crt.DeleteRecordRequest",
			because: "the label names the delete-record request by description");
	}

	[Test]
	[Description("A camelCase selector spelling is rejected with a precise rename hint instead of silently degrading the detail request into a full catalog dump.")]
	public async Task GetRequestInfo_ShouldRejectLegacyAlias_WhenCamelCaseSelectorIsUsed() {
		// Arrange
		RequestInfoTool tool = CreateTool();
		RequestInfoArgs args = new() {
			ExtensionData = new Dictionary<string, JsonElement> {
				["requestType"] = JsonDocument.Parse("\"crt.ClosePageRequest\"").RootElement.Clone()
			}
		};

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(args);

		// Assert
		response.Success.Should().BeFalse(because: "an unbound camelCase field must be rejected, not ignored");
		response.Error.Should().Contain("request-type",
			because: "the rename hint must point at the canonical kebab-case parameter");
		response.Count.Should().Be(0,
			because: "the rejection must not degrade into a full catalog dump");
	}

	[Test]
	[Description("'version' and 'environment-name' are mutually exclusive — passing both is an argument error, mirroring the component catalog guard.")]
	public async Task GetRequestInfo_ShouldReturnError_WhenVersionAndEnvironmentAreBothProvided() {
		// Arrange
		RequestInfoTool tool = CreateTool();

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(
			new RequestInfoArgs(EnvironmentName: "dev", Version: "8.3.3"));

		// Assert
		response.Success.Should().BeFalse(because: "the two version sources contradict each other");
		response.Error.Should().Contain("mutually exclusive",
			because: "the error must explain the conflict instead of picking a winner silently");
	}

	[Test]
	[Description("A malformed 'version' value is rejected with a semver hint before any catalog work happens.")]
	public async Task GetRequestInfo_ShouldReturnError_WhenVersionIsNotSemver() {
		// Arrange
		RequestInfoTool tool = CreateTool();

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(new RequestInfoArgs(Version: "not-a-version"));

		// Assert
		response.Success.Should().BeFalse(because: "an unparseable version cannot scope the catalog");
		response.Error.Should().Contain("3-part semver",
			because: "the error must tell the agent the expected format");
	}

	[Test]
	[Description("Passing environment-name resolves the platform version through the probe and reports the authoritative 'environment' tier when the catalog matches that version.")]
	public async Task GetRequestInfo_ShouldReportEnvironmentTier_WhenEnvironmentResolvesAndCatalogMatches() {
		// Arrange
		RequestInfoTool tool = CreateTool(environmentVersion: "8.3.4");

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(new RequestInfoArgs(EnvironmentName: "dev"));

		// Assert
		response.Success.Should().BeTrue(because: "the catalog loads for the resolved version");
		response.ResolvedTargetVersion.Should().Be("8.3.4",
			because: "the response must echo the version the probe resolved");
		response.ResolvedFrom.Should().Be("environment",
			because: "the registry served the exact resolved version, so the catalog is authoritative");
		response.VersionWarning.Should().BeNull(
			because: "no caveat applies on the authoritative environment tier");
		response.RequiresVersionConfirmation.Should().BeNull(
			because: "the hard-stop flag is emitted only on latest-fallback");
	}

	[Test]
	[Description("An explicit valid 'version' scopes the catalog without any environment probe and reports the authoritative 'environment' tier when the registry serves that exact version.")]
	public async Task GetRequestInfo_ShouldReportEnvironmentTier_WhenExplicitValidVersionMatchesCatalog() {
		// Arrange — the in-memory client echoes the requested version, so the served catalog matches it exactly.
		RequestInfoTool tool = CreateTool();

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(new RequestInfoArgs(Version: "8.3.4"));

		// Assert
		response.Success.Should().BeTrue(
			because: "an explicit valid version loads the catalog without needing an environment probe");
		response.ResolvedTargetVersion.Should().Be("8.3.4",
			because: "the response echoes the explicitly requested version the registry served");
		response.ResolvedFrom.Should().Be("environment",
			because: "the requested version was known and the registry served that exact version, so the catalog is authoritative");
		response.VersionWarning.Should().BeNull(
			because: "no caveat applies when the served catalog matches the requested version exactly");
		response.RequiresVersionConfirmation.Should().BeNull(
			because: "the hard-stop flag is emitted only on latest-fallback");
		response.ResolvedFromReason.Should().BeNull(
			because: "a fallback reason is emitted only on latest-fallback");
	}

	[Test]
	[Description("When a specific version is requested but only 'latest' is published, the registry serves latest and the tool reports the SOFT 'environment-superset' tier with a caveat — never an error and never the latest-fallback hard stop. This is the real behavior when the CDN publishes RequestRegistry.json only under latest/.")]
	public async Task GetRequestInfo_ShouldReportEnvironmentSupersetTier_WhenRequestedVersionIsNotPublished() {
		// Arrange — the registry falls back to 'latest' regardless of the requested version (CDN 404 -> latest alias).
		RequestInfoTool tool = CreateTool(catalogResolvedVersion: "latest");

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(new RequestInfoArgs(Version: "8.3.4"));

		// Assert
		response.Success.Should().BeTrue(
			because: "a missing per-version catalog degrades softly to latest, not to an error");
		response.ResolvedTargetVersion.Should().Be("latest",
			because: "the registry served the latest alias because the requested version was not published");
		response.ResolvedFrom.Should().Be("environment-superset",
			because: "the requested version was known but the served catalog is the approximate latest superset");
		response.VersionWarning.Should().Be(ComponentInfoResolution.EnvironmentSupersetWarning,
			because: "environment-superset must carry the soft superset caveat so the agent verifies critical types");
		response.RequiresVersionConfirmation.Should().BeNull(
			because: "the version is known, so no hard-stop confirmation gate applies — unlike latest-fallback");
		response.ResolvedFromReason.Should().BeNull(
			because: "a fallback reason is emitted only on latest-fallback, not on environment-superset");
	}

	[Test]
	[Description("A transient platform-version probe failure resolves to the latest-fallback tier and surfaces the machine-readable 'probe-error' reason, letting the agent tell a retryable probe failure apart from a genuinely undeterminable version.")]
	public async Task GetRequestInfo_ShouldReportProbeErrorReason_WhenVersionProbeFails() {
		// Arrange — the resolver reports a transient probe failure (latest-fallback / probe-error).
		RequestInfoTool tool = CreateTool(resolution: new PlatformVersionResolution(
			"latest", VersionResolutionSource.LatestFallback) { Reason = VersionFallbackReason.ProbeError });

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(new RequestInfoArgs(EnvironmentName: "dev"));

		// Assert
		response.Success.Should().BeTrue(
			because: "a probe failure degrades to the latest catalog, not to an error");
		response.ResolvedFrom.Should().Be("latest-fallback",
			because: "an unresolved version falls back to latest");
		response.ResolvedFromReason.Should().Be("probe-error",
			because: "a transient probe failure must be distinguishable from a stable no-active-environment gap");
		response.VersionWarning.Should().Be(ComponentInfoResolution.LatestFallbackWarning,
			because: "latest-fallback must carry the hard-stop caveat");
		response.RequiresVersionConfirmation.Should().BeTrue(
			because: "latest-fallback is the hard stop — the agent must confirm the unknown version before proceeding");
	}

	[Test]
	[Description("Passing environment-name routes the version probe through IToolCommandResolver.Resolve<EnvironmentSettings> (the ENG-93208 credential-passthrough seam every resolver-routed tool shares, mirroring get-component-info) — never through a direct settings-repository lookup.")]
	public async Task GetRequestInfo_ShouldRouteEnvironmentProbeThroughCommandResolver_WhenEnvironmentNameProvided() {
		// Arrange
		RequestInfoCatalog catalog = new(new InMemoryRequestRegistryClient(TestRegistryJson));
		IPlatformVersionResolverFactory factory = Substitute.For<IPlatformVersionResolverFactory>();
		IPlatformVersionResolver resolver = Substitute.For<IPlatformVersionResolver>();
		resolver.ResolveAsync(Arg.Any<CancellationToken>())
			.Returns(new PlatformVersionResolution("8.3.4", VersionResolutionSource.Environment));
		factory.Create(Arg.Any<EnvironmentSettings>()).Returns(resolver);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>())
			.Returns(new EnvironmentSettings { Uri = "http://prod-stand" });
		RequestInfoTool tool = new(catalog, new InMemoryMobileRequestCatalog(TestMobileRegistryJson), new FakeDocsClient(), factory, commandResolver);

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(new RequestInfoArgs(EnvironmentName: "prod-stand"));

		// Assert
		response.ResolvedTargetVersion.Should().Be("8.3.4",
			because: "the version must come from the probe of the environment passed in the call, not from ambient state");
		response.ResolvedFrom.Should().Be("environment",
			because: "an environment-name-driven probe that matches the catalog is the 'environment' tier");
		commandResolver.Received(1).Resolve<EnvironmentSettings>(
			Arg.Is<EnvironmentOptions>(o => o.Environment == "prod-stand"));
		factory.Received(1).Create(Arg.Any<EnvironmentSettings>());
	}

	[Test]
	[Description("Passing uri (with no environment-name) is also a hasEnvironment call — it routes version resolution through IToolCommandResolver.Resolve<EnvironmentSettings>, not just the environment-name spelling, mirroring get-component-info's AC-02 uri-only proof.")]
	public async Task GetRequestInfo_ShouldRouteEnvironmentProbeThroughCommandResolver_WhenUriProvided() {
		// Arrange
		RequestInfoCatalog catalog = new(new InMemoryRequestRegistryClient(TestRegistryJson));
		IPlatformVersionResolverFactory factory = Substitute.For<IPlatformVersionResolverFactory>();
		IPlatformVersionResolver resolver = Substitute.For<IPlatformVersionResolver>();
		resolver.ResolveAsync(Arg.Any<CancellationToken>())
			.Returns(new PlatformVersionResolution("8.3.4", VersionResolutionSource.Environment));
		factory.Create(Arg.Any<EnvironmentSettings>()).Returns(resolver);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>())
			.Returns(new EnvironmentSettings { Uri = "http://explicit-uri" });
		RequestInfoTool tool = new(catalog, new InMemoryMobileRequestCatalog(TestMobileRegistryJson), new FakeDocsClient(), factory, commandResolver);

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(new RequestInfoArgs(Uri: "http://explicit-uri"));

		// Assert
		response.ResolvedTargetVersion.Should().Be("8.3.4",
			because: "an explicit uri (without environment-name) must still probe through the resolver, mirroring the CLI verb's uri fallback");
		response.ResolvedFrom.Should().Be("environment",
			because: "a successful probe from an explicit uri is still the 'environment' tier");
		// The uri-only call must reach IToolCommandResolver.Resolve — the SAME hasEnvironment
		// branch environment-name uses — instead of a settings-repository-only path.
		commandResolver.Received(1).Resolve<EnvironmentSettings>(
			Arg.Is<EnvironmentOptions>(o => o.Uri == "http://explicit-uri" && string.IsNullOrEmpty(o.Environment)));
	}

	[Test]
	[Description("Mixed input (an explicit environment-name alongside an active passthrough header) is rejected by IToolCommandResolver's transport policy BEFORE any named-tenant probe — the platform-version resolver factory is never invoked and the rejection surfaces as a redacted error envelope, mirroring get-component-info.")]
	public async Task GetRequestInfo_ShouldRejectMixedInput_BeforeNamedTenantProbe() {
		// Arrange
		const string rejectionMessage =
			"Explicit credential or environment arguments (uri/login/password/client-id/client-secret/environment) "
			+ "are not accepted when credential passthrough is enabled over HTTP. Supply the target environment "
			+ "and credentials via the X-Integration-Credentials header, not tool arguments.";
		RequestInfoCatalog catalog = new(new InMemoryRequestRegistryClient(TestRegistryJson));
		IPlatformVersionResolverFactory factory = Substitute.For<IPlatformVersionResolverFactory>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<EnvironmentSettings>(
				Arg.Is<EnvironmentOptions>(o => o.Environment == "other-registered-env"))
			.Returns(_ => throw new EnvironmentResolutionException(rejectionMessage));
		RequestInfoTool tool = new(catalog, new InMemoryMobileRequestCatalog(TestMobileRegistryJson), new FakeDocsClient(), factory, commandResolver);

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(
			new RequestInfoArgs(EnvironmentName: "other-registered-env"));

		// Assert
		response.Success.Should().BeFalse(
			because: "mixed header + environment-name input must be rejected instead of silently succeeding against the named tenant");
		response.Error.Should().Contain("X-Integration-Credentials",
			because: "the rejection must teach the caller the correct credential channel, matching the sibling matrix tools' fail-soft error shape");
		factory.DidNotReceiveWithAnyArgs().Create(Arg.Any<EnvironmentSettings>());
	}

	[Test]
	[Description("A header-only call (neither environment-name nor uri) never calls IToolCommandResolver — it stays on the CreateNoActiveEnvironmentFallback path, mirroring get-component-info's compliant no-environment branch.")]
	public async Task GetRequestInfo_ShouldNeverCallCommandResolver_WhenHeaderOnly() {
		// Arrange
		RequestInfoCatalog catalog = new(new InMemoryRequestRegistryClient(TestRegistryJson));
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		RequestInfoTool tool = BuildTool(catalog, commandResolver: commandResolver);

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(new RequestInfoArgs());

		// Assert
		response.ResolvedFrom.Should().Be("latest-fallback",
			because: "the header-only, no-environment branch must keep degrading to the loud latest-fallback marker unchanged");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Description("When the registry chain is exhausted the tool converts ComponentRegistryUnavailableException into a graceful MCP error response that points the operator at the requests-flavor local-override env var.")]
	public async Task GetRequestInfo_ShouldReturnGracefulError_WhenRegistryChainIsExhausted() {
		// Arrange
		IRequestInfoCatalog throwingCatalog = Substitute.For<IRequestInfoCatalog>();
		throwingCatalog.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns<Task<RequestCatalogState>>(_ => throw new ComponentRegistryUnavailableException(
				"latest", "https://academy.creatio.com/api/mcp/", RegistryFlavor.Requests.LocalFileEnvironmentVariable));
		RequestInfoTool tool = BuildTool(throwingCatalog);

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(new RequestInfoArgs());

		// Assert
		response.Success.Should().BeFalse(because: "the catalog could not be served from any tier");
		response.Error.Should().Contain("CLIO_REQUEST_REGISTRY_LOCAL_FILE",
			because: "the operator remedy must name the requests-flavor override, not the web one");
	}

	[Test]
	[Description("When the MOBILE registry chain is exhausted the tool converts ComponentRegistryUnavailableException into a graceful MCP error naming the mobile-requests-flavor override (CLIO_MOBILE_REQUEST_REGISTRY_LOCAL_FILE), not the web one — proving the schema-type=mobile branch routes exhaustion through the mobile flavor's client/env var.")]
	public async Task GetRequestInfo_ShouldReturnGracefulError_WhenMobileRegistryChainIsExhausted() {
		// Arrange
		IMobileRequestInfoCatalog throwingMobileCatalog = Substitute.For<IMobileRequestInfoCatalog>();
		throwingMobileCatalog.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns<Task<RequestCatalogState>>(_ => throw new ComponentRegistryUnavailableException(
				"latest", "https://academy.creatio.com/api/mcp/", RegistryFlavor.MobileRequests.LocalFileEnvironmentVariable));
		RequestInfoTool tool = BuildTool(
			new RequestInfoCatalog(new InMemoryRequestRegistryClient(TestRegistryJson)),
			mobileCatalog: throwingMobileCatalog);

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(new RequestInfoArgs(SchemaType: "mobile"));

		// Assert
		response.Success.Should().BeFalse(because: "the mobile catalog could not be served from any tier");
		response.Error.Should().Contain("CLIO_MOBILE_REQUEST_REGISTRY_LOCAL_FILE",
			because: "the mobile branch's exhaustion must name the mobile-requests-flavor override, not the web one");
	}

	[Test]
	[Description("schema-type=mobile routes list mode to the mobile request catalog: it returns the mobile registry's requests (crt.RunBusinessProcessRequest) and NOT the web-only crt.DeleteRecordRequest, proving the isMobile branch selects mobileCatalog rather than the web catalog. Mirrors get-component-info's schema-type=mobile routing.")]
	public async Task GetRequestInfo_ShouldReturnMobileCatalog_WhenSchemaTypeIsMobile() {
		// Arrange
		RequestInfoTool tool = CreateTool();

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(new RequestInfoArgs(SchemaType: "mobile"));

		// Assert
		response.Success.Should().BeTrue(because: "listing the mobile catalog must succeed");
		response.Mode.Should().Be("list", because: "omitting request-type selects list mode on the mobile flavor too");
		response.Count.Should().Be(2, because: "the mobile test registry declares exactly two requests, distinct from the web set");
		response.Items!.Select(item => item.RequestType).Should().Contain("crt.RunBusinessProcessRequest",
			because: "the mobile-registry request must surface from the mobile catalog");
		response.Items!.Select(item => item.RequestType).Should().NotContain("crt.DeleteRecordRequest",
			because: "crt.DeleteRecordRequest exists only in the web registry — the mobile branch must not read the web catalog");
	}

	[Test]
	[Description("schema-type=mobile detail mode surfaces the mobile request's parameter contract, including the mobile-only activeRow parameter that has no desktop twin — proving parameters come from the mobile registry.")]
	public async Task GetRequestInfo_ShouldReturnMobileDetail_WhenSchemaTypeIsMobileAndTypeIsKnown() {
		// Arrange
		RequestInfoTool tool = CreateTool();

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(
			new RequestInfoArgs("crt.RunBusinessProcessRequest", SchemaType: "mobile"));

		// Assert
		response.Success.Should().BeTrue(because: "crt.RunBusinessProcessRequest exists in the mobile registry");
		response.Mode.Should().Be("detail", because: "a known mobile request type selects detail mode");
		response.RequestType.Should().Be("crt.RunBusinessProcessRequest",
			because: "the detail response echoes the requested mobile request type");
		response.Parameters.Should().ContainKey("activeRow",
			because: "the mobile-only activeRow parameter must surface from the mobile registry, not the desktop one");
	}

	[Test]
	[Description("Requesting a web-only request type from the mobile catalog returns a not-found response — the mobile registry is a separate, mobile-scoped set, so a web-only type must not resolve through schema-type=mobile.")]
	public async Task GetRequestInfo_ShouldReturnNotFound_WhenWebOnlyTypeRequestedFromMobileCatalog() {
		// Arrange
		RequestInfoTool tool = CreateTool();

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(
			new RequestInfoArgs("crt.DeleteRecordRequest", SchemaType: "mobile"));

		// Assert
		response.Success.Should().BeFalse(
			because: "crt.DeleteRecordRequest is a web-only request and must not resolve from the mobile catalog");
		response.Error.Should().Contain("crt.DeleteRecordRequest",
			because: "the not-found error must echo the unknown type");
	}

	[Test]
	[Description("Omitting schema-type defaults to the web request catalog — the default flavor must be web, matching get-component-info.")]
	public async Task GetRequestInfo_ShouldDefaultToWebCatalog_WhenSchemaTypeIsOmitted() {
		// Arrange
		RequestInfoTool tool = CreateTool();

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(new RequestInfoArgs());

		// Assert
		response.Count.Should().Be(3, because: "the web test registry declares three requests");
		response.Items!.Select(item => item.RequestType).Should().Contain("crt.DeleteRecordRequest",
			because: "omitting schema-type must read the web catalog, which contains the web-only request");
	}

	[Test]
	[Description("A camelCase 'schemaType' selector is rejected with a precise rename hint pointing at 'schema-type', instead of silently dropping it and defaulting to the web catalog.")]
	public async Task GetRequestInfo_ShouldRejectLegacyAlias_WhenCamelCaseSchemaTypeIsUsed() {
		// Arrange
		RequestInfoTool tool = CreateTool();
		RequestInfoArgs args = new() {
			ExtensionData = new Dictionary<string, JsonElement> {
				["schemaType"] = JsonDocument.Parse("\"mobile\"").RootElement.Clone()
			}
		};

		// Act
		RequestInfoResponse response = await tool.GetRequestInfo(args);

		// Assert
		response.Success.Should().BeFalse(because: "an unbound camelCase field must be rejected, not ignored");
		response.Error.Should().Contain("schema-type",
			because: "the rename hint must point at the canonical kebab-case parameter");
	}

	private static RequestInfoTool CreateTool(
		IComponentRegistryDocsClient? docsClient = null,
		string? environmentVersion = null,
		string? catalogResolvedVersion = null,
		PlatformVersionResolution? resolution = null,
		IMobileRequestInfoCatalog? mobileCatalog = null) {
		RequestInfoCatalog catalog = new(new InMemoryRequestRegistryClient(TestRegistryJson, catalogResolvedVersion));
		return BuildTool(catalog, docsClient, environmentVersion, resolution, mobileCatalog: mobileCatalog);
	}

	/// <summary>
	/// Builds a <see cref="RequestInfoTool"/> for tests. With every optional arg null the tool resolves
	/// nothing (a bare call → <c>latest-fallback</c> / <c>no-active-environment</c>). Pass
	/// <paramref name="environmentVersion"/> to wire the stub factory so a call carrying
	/// <c>environment-name</c> resolves to that version on the <c>environment</c> source; or pass an
	/// explicit <paramref name="resolution"/> (which takes precedence) to force any resolver outcome —
	/// e.g. a <c>latest-fallback</c> / <c>probe-error</c> tier.
	/// </summary>
	private static RequestInfoTool BuildTool(
		IRequestInfoCatalog catalog,
		IComponentRegistryDocsClient? docsClient = null,
		string? environmentVersion = null,
		PlatformVersionResolution? resolution = null,
		IToolCommandResolver? commandResolver = null,
		IMobileRequestInfoCatalog? mobileCatalog = null) {
		IPlatformVersionResolverFactory factory = Substitute.For<IPlatformVersionResolverFactory>();
		if (resolution is not null || environmentVersion is not null) {
			IPlatformVersionResolver resolver = Substitute.For<IPlatformVersionResolver>();
			resolver.ResolveAsync(Arg.Any<CancellationToken>())
				.Returns(resolution ?? new PlatformVersionResolution(environmentVersion!, VersionResolutionSource.Environment));
			factory.Create(Arg.Any<EnvironmentSettings>()).Returns(resolver);
		}
		if (commandResolver is null) {
			commandResolver = Substitute.For<IToolCommandResolver>();
			commandResolver.Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>())
				.Returns(new EnvironmentSettings { Uri = "http://test-stand" });
		}
		return new RequestInfoTool(
			catalog,
			mobileCatalog ?? new InMemoryMobileRequestCatalog(TestMobileRegistryJson),
			docsClient ?? new FakeDocsClient(),
			factory,
			commandResolver);
	}

	/// <summary>
	/// In-memory requests-flavor registry client: serves the given JSON for every version. By default it
	/// echoes the requested version back as the resolved version (exact match); pass
	/// <paramref name="resolvedVersionOverride"/> to report a fixed resolved version regardless of the
	/// request — modelling the CDN 404 -&gt; <c>latest</c> alias fallback (a per-version file not published).
	/// </summary>
	private sealed class InMemoryRequestRegistryClient(string registryJson, string? resolvedVersionOverride = null) : IRequestRegistryClient {
		private readonly byte[] _payload = Encoding.UTF8.GetBytes(registryJson);

		public Task<ComponentRegistryFetchResult> GetAsync(string requestedVersion, CancellationToken cancellationToken = default) {
			return Task.FromResult(new ComponentRegistryFetchResult(
				new MemoryStream(_payload, writable: false),
				resolvedVersionOverride ?? requestedVersion,
				ComponentRegistrySource.Cdn));
		}

		public Task<bool> RefreshAsync(string version, CancellationToken cancellationToken = default) {
			return Task.FromResult(false);
		}
	}

	/// <summary>
	/// In-memory mobile request catalog: parses a JSON snippet through the shared
	/// <see cref="RequestInfoCatalog.LoadFromStream"/> so the mobile flavor exercises the exact same
	/// envelope parse as the web flavor, with no filesystem or registry client. Echoes the requested
	/// version as the resolved version unless <paramref name="resolvedVersionOverride"/> is set
	/// (models the CDN 404 -> latest alias fallback).
	/// </summary>
	private sealed class InMemoryMobileRequestCatalog(string registryJson, string? resolvedVersionOverride = null) : IMobileRequestInfoCatalog {
		private readonly byte[] _payload = Encoding.UTF8.GetBytes(registryJson);

		public Task<RequestCatalogState> LoadAsync(string requestedVersion, CancellationToken cancellationToken = default) {
			using MemoryStream stream = new(_payload, writable: false);
			return Task.FromResult(RequestInfoCatalog.LoadFromStream(
				stream, resolvedVersionOverride ?? requestedVersion, ComponentRegistrySource.Cdn));
		}
	}

	/// <summary>
	/// Test double for the docs client. Returns a pre-seeded markdown blob for the matching
	/// (version, path) tuple or <see langword="null"/> otherwise — matching the contract the
	/// real client uses to signal "skip this doc".
	/// </summary>
	private sealed class FakeDocsClient : IComponentRegistryDocsClient {
		private readonly Dictionary<(string Version, string DocPath), string> _docs = new();

		public List<(string Version, string DocPath)> Requests { get; } = new();

		public FakeDocsClient Seed(string version, string docPath, string content) {
			_docs[(version, docPath)] = content;
			return this;
		}

		public Task<string?> GetDocAsync(string version, string docPath, CancellationToken cancellationToken = default) {
			Requests.Add((version, docPath));
			return Task.FromResult(_docs.TryGetValue((version, docPath), out string? value) ? value : null);
		}
	}
}
