using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Hermetic (NoEnvironment) end-to-end coverage for the set-logo MCP tool: the real clio MCP server
/// advertises it on the lazy surface, binds the args wrapper, validates the required fields with
/// structured failures, and rejects a camelCase alias with a rename hint — none of which needs a live
/// Creatio environment. The live apply-and-bind (which writes Binary sys settings and package data)
/// is a sandbox-environment concern, mirroring SetBackgroundImageToolE2ETests.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("set-logo")]
[NonParallelizable]
public sealed class SetLogoToolE2ETests : McpContractFixtureBase {
	[Test]
	[AllureTag(SetLogoTool.ToolName)]
	[AllureName("set-logo tool is discoverable on the lazy surface")]
	[AllureDescription("Starts the real clio MCP server and verifies set-logo is discoverable via the get-tool-contract compact index on the lazy tool surface.")]
	[Description("Starts the real clio MCP server and verifies set-logo is discoverable via the get-tool-contract compact index on the lazy tool surface.")]
	public async Task SetLogo_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(SetLogoTool.ToolName,
			because: "the set-logo MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
	}

	[Test]
	[AllureTag(SetLogoTool.ToolName)]
	[AllureName("set-logo binds the args wrapper and returns a structured validation failure")]
	[AllureDescription("Calls set-logo through the real clio MCP server with an empty args object and verifies the structured { success=false, error } result names environment-name — proving the args wrapper binds without a live Creatio environment.")]
	[Description("Calls set-logo through the real clio MCP server with an empty args object and verifies the structured { success=false, error } result names environment-name — proving the args wrapper binds without a live Creatio environment.")]
	public async Task SetLogo_Should_Return_Structured_Validation_Failure_When_Args_Are_Empty() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			SetLogoTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?>()
			},
			context.CancellationTokenSource.Token);
		SetLogoToolResult result =
			EntitySchemaStructuredResultParser.Extract<SetLogoToolResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "a missing environment name is an expected, caller-actionable validation error");
		result.Error.Should().Contain("environment-name is required",
			because: "the failure must name the exact kebab-case field the caller has to add");
	}

	[Test]
	[AllureTag(SetLogoTool.ToolName)]
	[AllureName("set-logo requires at least one logo slot before any environment work")]
	[AllureDescription("Calls set-logo with only environment-name and verifies the structured failure names the accepted slot fields — the validation runs before environment resolution, so no live Creatio environment is needed.")]
	[Description("Calls set-logo with only environment-name and verifies the structured failure names the accepted slot fields — the validation runs before environment resolution, so no live Creatio environment is needed.")]
	public async Task SetLogo_Should_Return_Structured_Validation_Failure_When_No_Slot_Is_Passed() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			SetLogoTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = "docker_fix2"
				}
			},
			context.CancellationTokenSource.Token);
		SetLogoToolResult result =
			EntitySchemaStructuredResultParser.Extract<SetLogoToolResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "a request with no logo file has nothing to apply");
		result.Error.Should().Contain("dark-logo",
			because: "the failure must name the accepted slot fields so the caller can pick one");
		result.Error.Should().Contain("favicon",
			because: "favicon is accepted on its own, so an agent reading the failure must see it among the fields rather than conclude a logo file is mandatory");
	}

	[Test]
	[AllureTag(SetLogoTool.ToolName)]
	[AllureName("set-logo rejects a camelCase alias with a structured rename hint over the wire")]
	[AllureDescription("Calls set-logo through the real clio MCP server with a camelCase menuLogo field and verifies the structured rename hint — proving the args wrapper binds and unknown keys reach the ExtensionData bag through the real MCP serializer, without a live Creatio environment.")]
	[Description("Calls set-logo through the real clio MCP server with a camelCase menuLogo field and verifies the structured rename hint — proving the args wrapper binds and unknown keys reach the ExtensionData bag through the real MCP serializer, without a live Creatio environment.")]
	public async Task SetLogo_Should_Return_RenameHint_When_CamelCase_Alias_Is_Passed_Over_The_Wire() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			SetLogoTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = "docker_fix2",
					["menuLogo"] = "C:/brand/menu.svg"
				}
			},
			context.CancellationTokenSource.Token);
		SetLogoToolResult result =
			EntitySchemaStructuredResultParser.Extract<SetLogoToolResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "a camelCase alias must be rejected, not silently dropped");
		result.Error.Should().Contain("'menuLogo' -> 'menu-logo'",
			because: "the failure must tell the caller the exact rename that fixes the call");
	}

	[Test]
	[AllureTag(SetLogoTool.ToolName)]
	[AllureName("set-logo rejects a snake_case login_logo with a rename hint naming the canonical login-logo field")]
	[AllureDescription("Calls set-logo through the real clio MCP server with a snake_case login_logo field and verifies the rename hint names login-logo — proving the dedicated login slot is advertised under its canonical kebab-case name over the wire, distinct from the all-slots logo field, without a live Creatio environment.")]
	[Description("Calls set-logo through the real clio MCP server with a snake_case login_logo field and verifies the rename hint names login-logo — proving the dedicated login slot is advertised under its canonical kebab-case name over the wire, distinct from the all-slots logo field, without a live Creatio environment.")]
	public async Task SetLogo_Should_Return_RenameHint_Naming_LoginLogo_When_SnakeCase_Is_Passed_Over_The_Wire() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			SetLogoTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = "docker_fix2",
					["login_logo"] = "C:/brand/logo.svg"
				}
			},
			context.CancellationTokenSource.Token);
		SetLogoToolResult result =
			EntitySchemaStructuredResultParser.Extract<SetLogoToolResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "a snake_case field must be rejected, not silently dropped into the overflow bag");
		result.Error.Should().Contain("'login_logo' -> 'login-logo'",
			because: "login-logo brands the login page alone while logo brands every slot, so the caller must be pointed at the exact field rather than left to guess that logo is the same thing");
	}

	[Test]
	[AllureTag(SetLogoTool.ToolName)]
	[AllureName("set-logo omits the warnings field entirely when a validation failure raised none")]
	[AllureDescription("Calls set-logo through the real clio MCP server with an empty args object and verifies the structured result carries no warnings field — the delivery-gap channel must be absent rather than an empty array, so an agent never reads an empty list as a gap it has to relay.")]
	[Description("Calls set-logo through the real clio MCP server with an empty args object and verifies the structured result carries no warnings field — the delivery-gap channel must be absent rather than an empty array, so an agent never reads an empty list as a gap it has to relay.")]
	public async Task SetLogo_Should_Omit_Warnings_When_The_Failure_Raised_None() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			SetLogoTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?>()
			},
			context.CancellationTokenSource.Token);
		SetLogoToolResult result =
			EntitySchemaStructuredResultParser.Extract<SetLogoToolResult>(callResult);

		// Assert
		result.Warnings.Should().BeNull(
			because: "warnings is the only delivery-gap channel the agent is told to relay, so a run that produced none must omit the field over the wire instead of emitting an empty array the agent has to special-case");
	}

	[Test]
	[AllureTag(SetLogoTool.ToolName)]
	[AllureName("set-logo omits the package field entirely when the run resolved no delivery target")]
	[AllureDescription("Calls set-logo through the real clio MCP server with an empty args object and verifies the structured result carries no package field — package is populated on a partial failure (the accepted slots were bound into it), so a failure that never reached binding must omit it rather than emit an empty value an agent could read as a package it must not re-run against.")]
	[Description("Calls set-logo through the real clio MCP server with an empty args object and verifies the structured result carries no package field — package is populated on a partial failure (the accepted slots were bound into it), so a failure that never reached binding must omit it rather than emit an empty value an agent could read as a package it must not re-run against.")]
	public async Task SetLogo_Should_Omit_Package_When_No_Delivery_Target_Was_Resolved() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			SetLogoTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?>()
			},
			context.CancellationTokenSource.Token);
		SetLogoToolResult result =
			EntitySchemaStructuredResultParser.Extract<SetLogoToolResult>(callResult);

		// Assert
		result.Success.Should().BeFalse(
			because: "an empty args object cannot name an environment, so the call must fail before any delivery");
		result.Package.Should().BeNull(
			because: "package names where the data that landed went, so a run that bound nothing must omit the field over the wire instead of pointing the agent at a package it never touched");
		result.Bound.Should().BeNull(
			because: "bound is the field an agent reads to tell what the package now carries, so a run that bound nothing must omit it over the wire instead of emitting an empty array the agent has to special-case");
	}

	[Test]
	[AllureTag(SetLogoTool.ToolName)]
	[AllureName("set-logo advertises favicon as its own canonical field over the wire")]
	[AllureDescription("Calls set-logo through the real clio MCP server with a camelCase faviconImage field and verifies the rename hint names favicon — proving the browser-tab icon is advertised under its own kebab-case field over the wire, distinct from the logo slots, without a live Creatio environment.")]
	[Description("Calls set-logo through the real clio MCP server with a camelCase faviconImage field and verifies the rename hint names favicon — proving the browser-tab icon is advertised under its own kebab-case field over the wire, distinct from the logo slots, without a live Creatio environment.")]
	public async Task SetLogo_Should_Return_RenameHint_When_CamelCase_Favicon_Alias_Is_Passed_Over_The_Wire() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			SetLogoTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = "docker_fix2",
					["faviconImage"] = "C:/brand/icon.svg"
				}
			},
			context.CancellationTokenSource.Token);
		SetLogoToolResult result =
			EntitySchemaStructuredResultParser.Extract<SetLogoToolResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(
			because: "a field named after the system setting must be rejected, not silently dropped — a dropped favicon would leave the browser tab unbranded while the run reported success");
		result.Error.Should().Contain("'faviconImage' -> 'favicon'",
			because: "the setting code is the name an agent is most likely to guess from the branding guidance, so the hint must map it to the canonical field");
	}
}
