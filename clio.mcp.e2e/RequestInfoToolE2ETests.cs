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
/// when-to-use-requests guide and the list-printables probe) — the request-catalog surface
/// that ships unconditionally on every default install. The fixture starts the real clio MCP
/// server with an isolated <c>CLIO_HOME</c> (hermetic settings: a single known environment,
/// autoupdate off, and a fixture-owned directory that also hosts the registry fixture file)
/// and points the requests-flavor registry at that local file
/// (<c>CLIO_REQUEST_REGISTRY_LOCAL_FILE</c>, the Tier-0 override read before cache/CDN)
/// so every scenario is offline-deterministic.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(RequestInfoTool.ToolName)]
[NonParallelizable]
public sealed class RequestInfoToolE2ETests : McpContractFixtureBase {
	private const string ToolName = RequestInfoTool.ToolName;

	// The four always-on guidance resources whose direct resources/read path is pinned below —
	// each must carry its request-catalog pointers.
	private const string RoutingUri = "docs://mcp/guides/routing";
	private const string PageModificationUri = "docs://mcp/guides/page-modification";
	private const string MobilePageUri = "docs://mcp/guides/mobile-page-modification";
	private const string PageSchemaHandlersUri = "docs://mcp/guides/page-schema-handlers";

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

	/// <summary>
	/// Offline MOBILE registry fixture: a deliberately different set from the web fixture so a
	/// schema-type=mobile call is proven to read the mobile registry (via
	/// <c>CLIO_MOBILE_REQUEST_REGISTRY_LOCAL_FILE</c>), not the web one — it drops the web-only
	/// crt.DeleteRecordRequest and carries a crt.RunBusinessProcessRequest whose mobile-only
	/// activeRow parameter has no desktop twin.
	/// </summary>
	private const string MobileRegistryFixtureJson = """
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

	/// <inheritdoc />
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		string clioHome = CreateIsolatedClioHome(
			"""
			{
			  "ActiveEnvironmentKey": "dev",
			  "Autoupdate": false,
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
		// The registry fixtures live inside the isolated home so the base fixture-directory
		// cleanup removes them together.
		string fixturePath = Path.Combine(clioHome, "RequestRegistry.e2e-fixture.json");
		File.WriteAllText(fixturePath, RegistryFixtureJson);
		string mobileFixturePath = Path.Combine(clioHome, "MobileRequestRegistry.e2e-fixture.json");
		File.WriteAllText(mobileFixturePath, MobileRegistryFixtureJson);
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = clioHome;
		settings.ProcessEnvironmentVariables[RegistryFlavor.Requests.LocalFileEnvironmentVariable] = fixturePath;
		settings.ProcessEnvironmentVariables[RegistryFlavor.MobileRequests.LocalFileEnvironmentVariable] = mobileFixturePath;
	}

	[Test]
	[Description("get-request-info is registered as a resident core tool and reachable on the MCP surface of a default install.")]
	[AllureTag(ToolName)]
	[AllureName("get-request-info is reachable on the default MCP surface")]
	[AllureDescription("Starts the real clio MCP server and verifies get-request-info is reachable.")]
	public async Task RequestInfoTool_Should_Be_Reachable() {
		// Arrange
		await using var context = Arrange();

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "the request catalog is a resident core tool and must always register on the MCP surface");
	}

	[Test]
	[Description("The list-printables probe is reachable on the lazy surface (non-resident, like get-process-signature) — the catalog's valueSource annotations point at it.")]
	[AllureTag(ListPrintablesTool.ToolName)]
	[AllureName("list-printables probe is reachable on the lazy surface")]
	[AllureDescription("Verifies the environment probe companion of the request catalog registers on the lazy MCP surface of a default install.")]
	public async Task ListPrintablesProbe_Should_Be_Reachable() {
		// Arrange
		await using var context = Arrange();

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ListPrintablesTool.ToolName,
			because: "the probe is non-resident by design and must be reachable through the lazy clio-run surface");
	}

	[Test]
	[Description("Invokes list-printables over the real MCP wire with an unregistered environment and verifies a structured failure envelope whose error identifies the environment — pinning the args-record wrapping and the environment-name binding end-to-end. The entity-name filter is passed to exercise its binding path (its filtering semantics need a live stand and are pinned by unit tests instead). Mirrors the stand-free get-process-signature invalid-environment contract test.")]
	[AllureTag(ListPrintablesTool.ToolName)]
	[AllureName("list-printables reports invalid environment failures over the wire")]
	[AllureDescription("Calls list-printables with an unknown environment name and verifies the structured Success=false failure identifies the unregistered environment, proving the environment-name binding survived the wire.")]
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
	[Description("Drives list-printables over the real mcp-server against an unresolvable target so the command-produced TRANSPORT failure (ListPrintablesCommand.TryGetPrintables catch) crosses the MCP boundary. The structured failure envelope must carry no scheme-qualified request URI — the tool redacts the raw exception message at the boundary. The exact input->[redacted-uri] transform is unit-pinned in ListPrintablesToolTests; this test proves the redaction is wired on the real command-produced-failure dispatch path, not only on the resolution-failure path.")]
	[AllureTag(ListPrintablesTool.ToolName)]
	[AllureName("list-printables redacts a transport-failure error over the wire")]
	[AllureDescription("Calls list-printables with a direct uri pointing at a reserved .invalid host (guaranteed unresolvable on every machine) and verifies the structured failure envelope leaks no scheme-qualified URI.")]
	public async Task ListPrintables_Should_Redact_Transport_Failure_Over_The_Wire() {
		// Arrange — a reserved .invalid host never resolves (RFC 6761), so the probe fails deterministically
		// at the transport layer on every machine, independent of what may be listening on localhost.
		await using var context = Arrange();

		// Act — non-resident probe dispatched through clio-run; the direct uri fallback drives the command
		// past resolution into TryGetPrintables, whose transport exception message the tool must redact.
		CallToolResult callResult = await context.Session.CallToolAsync(
			ListPrintablesTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["uri"] = "http://list-printables-redaction-e2e.invalid",
					["login"] = "Supervisor",
					["password"] = "Supervisor"
				}
			},
			context.CancellationTokenSource.Token);
		ListPrintablesEnvelope envelope = EntitySchemaStructuredResultParser.Extract<ListPrintablesEnvelope>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a transport failure must degrade to a structured in-band failure, not a top-level MCP error envelope");
		envelope.Success.Should().BeFalse(
			because: "the unresolvable host cannot be reached, so the probe fails");
		envelope.Error.Should().NotBeNullOrWhiteSpace(
			because: "a failed probe must carry a human-readable error instead of an empty envelope");
		envelope.Error!.Should().NotContain("http://",
			because: "the MCP boundary must strip any scheme-qualified request URI from the transport-error message before it reaches the client transcript");
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
	[Description("schema-type=mobile over the wire routes to the MOBILE request registry (via CLIO_MOBILE_REQUEST_REGISTRY_LOCAL_FILE): list mode returns the mobile-only set (crt.RunBusinessProcessRequest, NOT the web-only crt.DeleteRecordRequest), proving the schema-type argument survives the args-record wrapping and selects the mobile catalog end-to-end.")]
	[AllureTag(ToolName)]
	[AllureName("get-request-info routes schema-type=mobile to the mobile registry over the wire")]
	[AllureDescription("Calls get-request-info with schema-type=mobile against the local mobile registry fixture and verifies the mobile-scoped catalog is returned rather than the web one.")]
	public async Task RequestInfoTool_Should_Return_Mobile_Catalog_When_SchemaType_Is_Mobile() {
		// Arrange
		await using var context = Arrange();

		// Act
		RequestInfoResponse response = await CallRequestInfoAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> { ["schema-type"] = "mobile" });

		// Assert
		response.Success.Should().BeTrue(
			because: "list mode against the local mobile fixture must succeed");
		response.Mode.Should().Be("list",
			because: "omitting request-type selects list mode on the mobile flavor too");
		response.Count.Should().Be(2,
			because: "the mobile fixture registry declares exactly two requests, distinct from the web set");
		response.Items!.Select(item => item.RequestType).Should().Contain("crt.RunBusinessProcessRequest",
			because: "the mobile-registry request must surface when schema-type=mobile routes to the mobile catalog");
		response.Items!.Select(item => item.RequestType).Should().NotContain("crt.DeleteRecordRequest",
			because: "crt.DeleteRecordRequest exists only in the web fixture — schema-type=mobile must not read the web catalog");
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
	[Description("The when-to-use-requests guide is advertised and resolves through get-guidance, and the routing map carries the request-wiring row — the request-catalog discovery chain is always available on a default install.")]
	[AllureTag(ToolName)]
	[AllureName("when-to-use-requests guidance and routing row resolve on the default surface")]
	[AllureDescription("Calls get-guidance for when-to-use-requests and the routing map and verifies both carry the request-catalog discovery chain.")]
	public async Task WhenToUseRequestsGuide_And_RoutingRow_Should_Resolve() {
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
			because: "when-to-use-requests is a registered guidance name and must always resolve");
		guide.Article.Should().NotBeNull(
			because: "successful guidance lookups return the resolved article over the wire");
		guide.Article!.Uri.Should().Be("docs://mcp/guides/when-to-use-requests",
			because: "the canonical article URI must be stable");
		guide.Article.Text.Should().Contain("get-request-info",
			because: "the guide's core discipline is fetching the request contract from the catalog tool");
		routing.Success.Should().BeTrue(
			because: "the routing map is a core guide");
		routing.Article!.Text.Should().Contain("get-request-info",
			because: "the routing map must route button/menu request wiring to the catalog tool");
	}

	[Test]
	[Description("The four always-on guidance resources, read over the DIRECT resources/read MCP path, carry their request-catalog pointers - pinning that the pointers are served on resources/read, not only through get-guidance.")]
	[AllureTag(ToolName)]
	[AllureName("guides include request-catalog pointers over resources/read")]
	[AllureDescription("Reads routing, page-modification, mobile-page-modification and page-schema-handlers over resources/read and verifies each carries its request-catalog pointers.")]
	public async Task GuidanceResources_Should_Include_RequestCatalog_Pointers_Over_ResourcesRead() {
		// Arrange
		await using var context = Arrange();
		CancellationToken token = context.CancellationTokenSource.Token;

		// Act + Assert
		TextResourceContents routing = await ReadGuideAsync(context.Session, RoutingUri, token);
		routing.Text.Should().Contain("get-request-info",
			because: "the routing map advertises the request catalog over resources/read");
		routing.Text.Should().Contain("when-to-use-requests",
			because: "the routing map advertises the request-wiring guide over resources/read");

		TextResourceContents page = await ReadGuideAsync(context.Session, PageModificationUri, token);
		page.Text.Should().Contain("when-to-use-requests",
			because: "the page-modification GATE row mandates the request-wiring guide over resources/read");
		page.Text.Should().Contain("get-request-info",
			because: "the run-process GATE row names the request catalog over resources/read");

		TextResourceContents mobile = await ReadGuideAsync(context.Session, MobilePageUri, token);
		mobile.Text.Should().Contain("get-request-info",
			because: "the mobile run-process entry points at the request catalog over resources/read");

		TextResourceContents handlers = await ReadGuideAsync(context.Session, PageSchemaHandlersUri, token);
		handlers.Text.Should().Contain("get-request-info",
			because: "the handler parameter catalog names the request catalog over resources/read");
		handlers.Text.Should().Contain("when-to-use-requests",
			because: "the handler catalog intro points at the request-wiring guide over resources/read");
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

	private static async Task<GuidanceGetResponse> CallGuidanceAsync(
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

	/// <summary>
	/// Reads one guidance resource over the raw resources/read MCP path (not the lazy tools/list -> clio-run
	/// route) and returns its single plain-text article, asserting the URI round-trips.
	/// </summary>
	private static async Task<TextResourceContents> ReadGuideAsync(
		McpServerSession session,
		string uri,
		CancellationToken cancellationToken) {
		ReadResourceResult result = await session.ReadResourceAsync(uri, cancellationToken);
		TextResourceContents article = result.Contents.Single().Should().BeOfType<TextResourceContents>(
			because: "a guidance resource resolves to a single plain-text article over resources/read").Subject;
		article.Uri.Should().Be(uri,
			because: "resources/read must preserve the stable guide URI");
		return article;
	}
}
