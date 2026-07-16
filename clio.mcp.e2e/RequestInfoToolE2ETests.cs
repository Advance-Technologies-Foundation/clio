using System.Text.RegularExpressions;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the get-request-info MCP tool and its companions (the
/// when-to-use-requests guide and the list-printables probe) with the whole surface ENABLED.
/// The fixture starts the real clio MCP server with an isolated <c>CLIO_HOME</c> whose feature
/// set turns on <c>requests-registry</c> — the gate the request-catalog surface ships behind —
/// so these scenarios exercise the enabled behavior. The OFF (gated-hidden) behavior is covered
/// separately by <see cref="RequestRegistryGatingE2ETests"/>. It also points the requests-flavor
/// registry at a local fixture file (<c>CLIO_REQUEST_REGISTRY_LOCAL_FILE</c>, the Tier-0 override
/// read before cache/CDN) so every scenario is offline-deterministic.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(RequestInfoTool.ToolName)]
[NonParallelizable]
public sealed class RequestInfoToolE2ETests : McpContractFixtureBase {
	private const string ToolName = RequestInfoTool.ToolName;

	/// <summary>
	/// Offline registry fixture: the pilot parameterless request (no docs, so the detail
	/// path never touches the docs CDN) plus a parameterised request for search coverage,
	/// with the base-parameter and wiring-contract globals the detail response surfaces.
	/// </summary>
	private const string RegistryFixtureJson = """
	{
	  "requests": [
	    {
	      "requestType": "crt.ClosePageRequest",
	      "parameters": {},
	      "description": "Closes the currently open page."
	    },
	    {
	      "requestType": "crt.DeleteRecordRequest",
	      "parameters": {
	        "recordId": { "type": "string", "required": true, "description": "Identifier of the record to delete." }
	      },
	      "description": "Deletes a single record from a collection."
	    }
	  ],
	  "references": {
	    "baseParameters": {
	      "$context": { "type": "ViewModelContext", "description": "Platform-injected view-model context." }
	    },
	    "typeDefinitions": {
	      "RequestBindingConfig": {
	        "fields": {
	          "params": { "type": "Record", "keyType": "string", "valueType": "string | boolean | number" },
	          "request": { "type": "string", "required": true }
	        }
	      }
	    }
	  }
	}
	""";

	/// <inheritdoc />
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		string clioHome = CreateIsolatedClioHome(
			"""
			{
			  "ActiveEnvironmentKey": "dev",
			  "Autoupdate": false,
			  "Features": { "requests-registry": true },
			  "Environments": {
			    "dev": {
			      "Uri": "http://localhost",
			      "Login": "Supervisor",
			      "Password": "Supervisor",
			      "IsNetCore": true
			    }
			  }
			}
			""",
			GetType().Name);
		// The registry fixture lives inside the isolated home so the base fixture-directory
		// cleanup removes both together.
		string fixturePath = Path.Combine(clioHome, "RequestRegistry.e2e-fixture.json");
		File.WriteAllText(fixturePath, RegistryFixtureJson);
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = clioHome;
		settings.ProcessEnvironmentVariables[RegistryFlavor.Requests.LocalFileEnvironmentVariable] = fixturePath;
	}

	[Test]
	[Description("get-request-info is registered as a resident tool and reachable on the MCP surface once the requests-registry feature is enabled.")]
	[AllureTag(ToolName)]
	[AllureName("get-request-info is reachable when requests-registry is enabled")]
	[AllureDescription("Starts the real clio MCP server with an isolated CLIO_HOME that enables requests-registry and verifies the tool is reachable.")]
	public async Task RequestInfoTool_Should_Be_Reachable_When_RequestsRegistry_Enabled() {
		// Arrange
		await using var context = Arrange();

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "the request catalog is a core surface gated behind requests-registry and must register once the feature is enabled");
	}

	[Test]
	[Description("The list-printables probe is reachable on the lazy surface (non-resident, like get-process-signature) once the requests-registry feature is enabled — the catalog's valueSource annotations point at it.")]
	[AllureTag(ListPrintablesTool.ToolName)]
	[AllureName("list-printables probe is reachable when requests-registry is enabled")]
	[AllureDescription("Verifies the environment probe companion of the request catalog registers on the lazy MCP surface once requests-registry is enabled.")]
	public async Task ListPrintablesProbe_Should_Be_Reachable_When_RequestsRegistry_Enabled() {
		// Arrange
		await using var context = Arrange();

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ListPrintablesTool.ToolName,
			because: "the probe is gated behind requests-registry and must be reachable through the lazy surface once the feature is enabled");
	}

	[Test]
	[Description("Invokes list-printables over the real MCP wire with an unregistered environment and verifies a structured failure envelope whose error identifies the environment — pinning the args-record wrapping and the environment-name binding end-to-end. The entity-name filter is passed to exercise its binding path (its filtering semantics need a live stand and are pinned by unit tests instead). Mirrors the stand-free get-process-signature invalid-environment contract test.")]
	[AllureTag(ListPrintablesTool.ToolName)]
	[AllureName("list-printables reports invalid environment failures over the wire")]
	[AllureDescription("Calls list-printables with an unknown environment name against the requests-registry-enabled server and verifies the structured Success=false failure identifies the unregistered environment, proving the environment-name binding survived the wire.")]
	public async Task ListPrintables_Should_Report_Invalid_Environment_Failure() {
		// Arrange — an environment name that is guaranteed not registered, so command resolution fails
		// deterministically without any reachable stand (the get-process-signature NoEnvironment pattern).
		string invalidEnvironmentName = $"missing-printables-env-{Guid.NewGuid():N}";
		await using var context = Arrange();

		// Act — list-printables is a non-resident long-tail probe, so the session shim dispatches this
		// through clio-run; the entity-name filter is included so its binding path is exercised too.
		CallToolResult callResult = await context.Session.CallToolAsync(
			ListPrintablesTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["entity-name"] = "Contact",
					["environment-name"] = invalidEnvironmentName
				}
			},
			context.CancellationTokenSource.Token);
		ListPrintablesEnvelope envelope = EntitySchemaStructuredResultParser.Extract<ListPrintablesEnvelope>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an unregistered environment must degrade to a structured in-band failure, not a top-level MCP error envelope");
		envelope.Success.Should().BeFalse(
			because: "list-printables cannot resolve printables for an environment that is not registered");
		envelope.Error.Should().NotBeNullOrWhiteSpace(
			because: "a failed probe must carry a human-readable error instead of an empty envelope");
		envelope.Error!.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found|not registered)",
			because: "the failure must identify the unregistered environment, proving the environment-name argument survived the args-record wrapping and reached command resolution — a broken binding would fall back to the active environment and fail with an unrelated error");
	}

	[Test]
	[Description("List mode over the wire returns the cataloged requests with honest latest-fallback version markers on a bare call.")]
	[AllureTag(ToolName)]
	[AllureName("get-request-info lists the request catalog over the wire")]
	[AllureDescription("Calls get-request-info with no arguments against the local registry fixture and verifies the list envelope and the latest-fallback version markers.")]
	public async Task RequestInfoTool_Should_Return_Catalog_List_When_RequestType_Is_Omitted() {
		// Arrange
		await using var context = Arrange();

		// Act
		RequestInfoResponse response = await CallRequestInfoAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?>());

		// Assert
		response.Success.Should().BeTrue(
			because: "list mode against the local fixture must succeed");
		response.Mode.Should().Be("list",
			because: "omitting request-type selects list mode");
		response.Count.Should().Be(2,
			because: "the fixture registry declares exactly two requests");
		response.Items.Should().NotBeNullOrEmpty(
			because: "list mode returns a flat item list");
		response.Items!.Select(item => item.RequestType).Should().Contain("crt.ClosePageRequest",
			because: "the pilot request must surface in the catalog list");
		response.ResolvedFrom.Should().Be("latest-fallback",
			because: "a bare call carries no environment or version, so the version cannot be determined");
		response.RequiresVersionConfirmation.Should().BeTrue(
			because: "the machine-readable hard stop must accompany latest-fallback over the wire");
	}

	[Test]
	[Description("Detail mode over the wire surfaces the authorable parameters map (explicitly empty for the parameterless pilot request) and keeps the platform-injected baseParameters separate, with the RequestBindingConfig wiring contract inlined.")]
	[AllureTag(ToolName)]
	[AllureName("get-request-info returns a detail with separate baseParameters over the wire")]
	[AllureDescription("Requests crt.ClosePageRequest detail against the local registry fixture and verifies the empty parameters map, the separate baseParameters block, and the inlined wiring type definition.")]
	public async Task RequestInfoTool_Should_Return_Detail_With_Separate_BaseParameters_When_Type_Is_Known() {
		// Arrange
		await using var context = Arrange();

		// Act
		RequestInfoResponse response = await CallRequestInfoAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["request-type"] = "crt.ClosePageRequest" });

		// Assert
		response.Success.Should().BeTrue(
			because: "crt.ClosePageRequest exists in the fixture registry");
		response.Mode.Should().Be("detail",
			because: "a known request type selects detail mode");
		response.RequestType.Should().Be("crt.ClosePageRequest",
			because: "the detail response echoes the requested type");
		response.Parameters.Should().NotBeNull(
			because: "the explicitly empty parameters map must survive the wire — it means 'accepts no parameters'");
		response.Parameters.Should().BeEmpty(
			because: "crt.ClosePageRequest accepts no authorable parameters");
		response.BaseParameters.Should().NotBeNull(
			because: "the registry publishes root references.baseParameters");
		response.BaseParameters!.Should().ContainKey("$context",
			because: "the platform-injected context field is part of the published base surface");
		response.Parameters.Should().NotContainKey("$context",
			because: "baseParameters must never merge into the authorable parameters map");
		response.References.Should().NotBeNull(
			because: "the wiring contract is reachable from the closure seed");
		response.References!.TypeDefinitions.Should().ContainKey("RequestBindingConfig",
			because: "every request is wired through RequestBindingConfig, so the detail inlines its schema");
	}

	[Test]
	[Description("An unknown request type over the wire returns a not-found list envelope with a bounded suggestion shortlist.")]
	[AllureTag(ToolName)]
	[AllureName("get-request-info returns suggestions for an unknown request type")]
	[AllureDescription("Requests a nonexistent request type and verifies the not-found envelope carries closest-match suggestions instead of the full catalog dump.")]
	public async Task RequestInfoTool_Should_Return_Suggestions_When_RequestType_Is_Unknown() {
		// Arrange
		await using var context = Arrange();

		// Act
		RequestInfoResponse response = await CallRequestInfoAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["request-type"] = "crt.ClosePagRequest" });

		// Assert
		response.Success.Should().BeFalse(
			because: "the requested type does not exist in the fixture registry");
		response.Mode.Should().Be("list",
			because: "not-found responses fall back to the list envelope");
		response.Error.Should().Contain("crt.ClosePagRequest",
			because: "the error must echo the unknown type");
		response.Items.Should().NotBeNullOrEmpty(
			because: "a closest-by-distance shortlist helps the agent recover");
		response.Items!.Select(item => item.RequestType).Should().Contain("crt.ClosePageRequest",
			because: "the closest known type must be suggested");
	}

	[Test]
	[Description("Rejects the camelCase selector 'requestType' over the wire with a rename hint to 'request-type', instead of silently dropping it and degrading the request into the full catalog list.")]
	[AllureTag(ToolName)]
	[AllureName("get-request-info rejects the requestType alias with a rename hint")]
	[AllureDescription("Passes the camelCase 'requestType' spelling and verifies it is rejected with a hint pointing at the kebab-case 'request-type'.")]
	public async Task RequestInfoTool_Should_Reject_CamelCase_Alias_Over_The_Wire() {
		// Arrange
		await using var context = Arrange();

		// Act
		RequestInfoResponse response = await CallRequestInfoAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["requestType"] = "crt.ClosePageRequest" });

		// Assert
		response.Success.Should().BeFalse(
			because: "an unbound camelCase field must be rejected, not ignored");
		response.Error.Should().Contain("request-type",
			because: "the rename hint must point at the canonical kebab-case parameter");
		response.Count.Should().Be(0,
			because: "the rejection must not degrade into a full catalog dump");
	}

	[Test]
	[Description("The when-to-use-requests guide is advertised and resolves through get-guidance once requests-registry is enabled, and the feature-aware routing map carries the request-wiring row — the discovery chain is deterministic while the feature is on.")]
	[AllureTag(ToolName)]
	[AllureName("when-to-use-requests guidance and routing row resolve when requests-registry is enabled")]
	[AllureDescription("Calls get-guidance for when-to-use-requests and the routing map against a server with requests-registry enabled and verifies both carry the request-catalog discovery chain.")]
	public async Task WhenToUseRequestsGuide_And_RoutingRow_Should_Resolve_When_RequestsRegistry_Enabled() {
		// Arrange
		await using var context = Arrange();

		// Act
		GuidanceGetResponse guide = await CallGuidanceAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["name"] = "when-to-use-requests" });
		GuidanceGetResponse routing = await CallGuidanceAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["name"] = "routing" });

		// Assert
		guide.Success.Should().BeTrue(
			because: "the guide is gated behind requests-registry and must resolve once the feature is enabled");
		guide.Article.Should().NotBeNull(
			because: "successful guidance lookups return the resolved article over the wire");
		guide.Article!.Uri.Should().Be("docs://mcp/guides/when-to-use-requests",
			because: "the canonical article URI must be stable");
		guide.Article.Text.Should().Contain("get-request-info",
			because: "the guide's core discipline is fetching the request contract from the catalog tool");
		routing.Success.Should().BeTrue(
			because: "the routing map is a core guide");
		routing.Article!.Text.Should().Contain("get-request-info",
			because: "the feature-aware routing map must route button/menu request wiring to the catalog tool once requests-registry is enabled");
	}

	private static async Task<RequestInfoResponse> CallRequestInfoAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		IReadOnlyDictionary<string, object?> arguments) {
		// get-request-info binds a single `args` record (kebab-case fields), like every other
		// clio MCP tool — wrap the per-call fields so the real binding engages instead of
		// dropping them as unknown top-level keys.
		CallToolResult callResult = await session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?>(arguments) },
			cancellationToken);
		callResult.IsError.Should().NotBeTrue(
			because: "get-request-info should return structured responses instead of top-level MCP failures");
		return EntitySchemaStructuredResultParser.Extract<RequestInfoResponse>(callResult);
	}

	internal static async Task<GuidanceGetResponse> CallGuidanceAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		IReadOnlyDictionary<string, object?> arguments) {
		CallToolResult callResult = await session.CallToolAsync(
			GuidanceGetTool.ToolName,
			new Dictionary<string, object?> { ["args"] = arguments },
			cancellationToken);
		callResult.IsError.Should().NotBeTrue(
			because: "get-guidance should return a normal MCP tool result envelope for valid request shapes");
		return EntitySchemaStructuredResultParser.Extract<GuidanceGetResponse>(callResult);
	}
}
