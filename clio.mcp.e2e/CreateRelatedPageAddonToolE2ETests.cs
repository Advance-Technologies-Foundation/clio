using System.Text.RegularExpressions;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the create-related-page-addon MCP tool, driven through the real clio MCP server.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(CreateRelatedPageAddonTool.ToolName)]
[NonParallelizable]
public sealed class CreateRelatedPageAddonToolE2ETests {
	private const string ToolName = CreateRelatedPageAddonTool.ToolName;

	[Test]
	[Description("Exposes create-related-page-addon through the get-tool-contract compact index on the lazy MCP surface.")]
	[AllureTag(ToolName)]
	[AllureName("create-related-page-addon MCP tool is discoverable on the lazy surface")]
	[AllureDescription("Starts the real clio MCP server and verifies create-related-page-addon is discoverable via the get-tool-contract compact index even though long-tail tools are not resident in tools/list.")]
	public async Task CreateRelatedPageAddon_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames = await arrangeContext.Session.ListReachableToolNamesAsync(
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "create-related-page-addon must be discoverable through get-tool-contract even though it is not resident in tools/list");
	}

	[Test]
	[Description("Binds a pages payload through the real MCP server and reports an invalid environment failure inside the structured response.")]
	[AllureTag(ToolName)]
	[AllureName("create-related-page-addon MCP tool binds a pages payload and reports invalid environment")]
	[AllureDescription("Starts the real clio MCP server, calls create-related-page-addon with a default+add pages payload and an intentionally missing environment, then verifies the nested pages array binds and the structured response reports the unresolved environment instead of an MCP binding error.")]
	public async Task CreateRelatedPageAddon_Should_Bind_Pages_Payload_And_Report_Invalid_Environment() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-related-page-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "Custom",
					["entity-schema-name"] = "UsrOrder",
					["pages"] = new object[] {
						new Dictionary<string, object?> {
							["page-schema-name"] = "UsrOrderFormPage",
							["is-default"] = true
						},
						new Dictionary<string, object?> {
							["page-schema-name"] = "UsrOrderFormPage",
							["is-add"] = true
						}
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CreateRelatedPageAddonResponse response =
			EntitySchemaStructuredResultParser.Extract<CreateRelatedPageAddonResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid pages payload should bind and stay inside the structured tool response");
		response.Success.Should().BeFalse(
			because: "the intentionally missing environment should fail before the add-on round-trip");
		response.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "the failure should come from resolving the requested environment, not from deserializing the pages payload");
	}

	[Test]
	[Description("Binds a typed pages payload (type-column-uid + per-page type-column-value) through the real MCP server and reports an invalid environment failure inside the structured response.")]
	[AllureTag(ToolName)]
	[AllureName("create-related-page-addon MCP tool binds a typed pages payload")]
	[AllureDescription("Starts the real clio MCP server, calls create-related-page-addon with a type-column-uid and an untyped default plus a typed page entry and an intentionally missing environment, then verifies the typed payload binds and the structured response reports the unresolved environment.")]
	public async Task CreateRelatedPageAddon_Should_Bind_Typed_Pages_Payload_And_Report_Invalid_Environment() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-typed-related-page-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "Custom",
					["entity-schema-name"] = "Case",
					["type-column-uid"] = "af280321-e749-41dd-98e5-383906747e29",
					["pages"] = new object[] {
						new Dictionary<string, object?> {
							["page-schema-name"] = "Cases_FormPage",
							["is-default"] = true
						},
						new Dictionary<string, object?> {
							["page-schema-name"] = "Cases_FormPage",
							["is-default"] = true,
							["type-column-value"] = "1b0bc159-150a-e111-a31b-00155d04c01d"
						}
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CreateRelatedPageAddonResponse response =
			EntitySchemaStructuredResultParser.Extract<CreateRelatedPageAddonResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid typed payload should bind and stay inside the structured tool response");
		response.Success.Should().BeFalse(
			because: "the intentionally missing environment should fail before the add-on round-trip");
		response.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "the failure should come from resolving the environment, not from deserializing the typed payload");
	}

	[Test]
	[Description("Binds a portal (role-name) pages payload through the real MCP server and reports an invalid environment failure inside the structured response.")]
	[AllureTag(ToolName)]
	[AllureName("create-related-page-addon MCP tool binds a portal role-name payload")]
	[AllureDescription("Starts the real clio MCP server, calls create-related-page-addon with a base default plus a portal 'All external users' entry (via role-name) and an intentionally missing environment, then verifies the role-name payload binds and the structured response reports the unresolved environment.")]
	public async Task CreateRelatedPageAddon_Should_Bind_Portal_RoleName_Payload_And_Report_Invalid_Environment() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-portal-related-page-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "Custom",
					["entity-schema-name"] = "Case",
					["pages"] = new object[] {
						new Dictionary<string, object?> {
							["page-schema-name"] = "Cases_FormPage",
							["is-default"] = true,
							["role-name"] = "All employees"
						},
						new Dictionary<string, object?> {
							["page-schema-name"] = "Cases_FormPage",
							["is-default"] = true,
							["role-name"] = "All external users"
						}
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CreateRelatedPageAddonResponse response =
			EntitySchemaStructuredResultParser.Extract<CreateRelatedPageAddonResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid portal role-name payload should bind and stay inside the structured tool response");
		response.Success.Should().BeFalse(
			because: "the intentionally missing environment should fail before the add-on round-trip");
		response.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "the failure should come from resolving the environment, not from deserializing the role-name payload");
	}

	[Test]
	[Description("Accepts an empty pages array (reset to inline / clear all bindings) via create-related-page-addon: the empty payload binds and the call fails only on the unresolved environment, not on a pages rejection.")]
	[AllureTag(ToolName)]
	[AllureName("create-related-page-addon MCP tool accepts an empty pages array as reset-to-inline")]
	[AllureDescription("Starts the real clio MCP server, calls create-related-page-addon with an empty pages array (the reset-to-inline / clear-all-bindings operation) and an intentionally missing environment, then verifies the empty payload binds and the structured response reports the unresolved environment instead of rejecting the empty pages set.")]
	public async Task CreateRelatedPageAddon_Should_Accept_Empty_Pages_AsResetToInline() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-empty-reset-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "Custom",
					["entity-schema-name"] = "UsrOrder",
					["pages"] = Array.Empty<object>()
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CreateRelatedPageAddonResponse response =
			EntitySchemaStructuredResultParser.Extract<CreateRelatedPageAddonResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an empty pages array (reset to inline) should bind and stay inside the structured tool response");
		response.Success.Should().BeFalse(
			because: "the intentionally missing environment should fail before the add-on round-trip");
		response.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "an empty pages array is accepted as reset-to-inline, so the failure must come from resolving the "
				+ "environment — NOT from rejecting the empty pages set");
	}

	[Test]
	[Description("Binds schema-type=mobile with a pages payload through the real MCP server and reports an invalid environment inside the structured response, proving the mobile MobileRelatedPage write path is on the tool contract.")]
	[AllureTag(ToolName)]
	[AllureName("create-related-page-addon MCP tool binds schema-type=mobile")]
	[AllureDescription("Starts the real clio MCP server, calls create-related-page-addon with schema-type=mobile plus a default pages payload and an intentionally missing environment, then verifies the mobile schema-type binds and the structured response reports the unresolved environment instead of an MCP binding error.")]
	public async Task CreateRelatedPageAddon_Should_Bind_Mobile_SchemaType_And_Report_Invalid_Environment() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-create-mobile-related-page-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "Custom",
					["entity-schema-name"] = "UsrOrder",
					["schema-type"] = "mobile",
					["pages"] = new object[] {
						new Dictionary<string, object?> {
							["page-schema-name"] = "UsrOrderMobileFormPage",
							["is-default"] = true
						}
					}
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CreateRelatedPageAddonResponse response =
			EntitySchemaStructuredResultParser.Extract<CreateRelatedPageAddonResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "schema-type=mobile plus a valid pages payload should bind and stay inside the structured tool response");
		response.Success.Should().BeFalse(
			because: "the intentionally missing environment should fail before the mobile add-on round-trip");
		response.Error.Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found)",
			because: "the failure should come from resolving the environment, not from rejecting the mobile schema-type or the payload");
	}

	[Test]
	[Description("Rejects a pages array containing a null entry via create-related-page-addon before any remote calls.")]
	[AllureTag(ToolName)]
	[AllureName("create-related-page-addon MCP tool rejects a null pages entry")]
	[AllureDescription("Starts the real clio MCP server, calls create-related-page-addon with a pages array whose only entry is null, and verifies the structured response reports the invalid entry without an MCP binding error or a mapping NRE.")]
	public async Task CreateRelatedPageAddon_Should_Reject_Null_Pages_Entry() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = $"noop-{Guid.NewGuid():N}",
					["package-name"] = "Custom",
					["entity-schema-name"] = "UsrOrder",
					["pages"] = new object?[] { null }
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CreateRelatedPageAddonResponse response =
			EntitySchemaStructuredResultParser.Extract<CreateRelatedPageAddonResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a null pages entry should be reported inside the structured response, not as an MCP binding error");
		response.Success.Should().BeFalse(
			because: "a null pages entry fails validation, so the structured response reports failure rather than binding");
		response.Error.Should().Contain("pages",
			because: "the structured response should explain that a null pages entry is not allowed");
	}

	private static async Task<ArrangeContext> ArrangeAsync(TimeSpan timeout) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource);
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
