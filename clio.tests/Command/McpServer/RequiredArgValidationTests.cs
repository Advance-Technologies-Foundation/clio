using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Clio;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// FR-13 (ENG-93208): characterizes whether an MCP tool call missing a <c>required</c> argument is
/// rejected UP FRONT — before the tool method body (and therefore command dispatch/resolution) runs —
/// or whether it reaches execution as a late/opaque failure. Tools are built through the real SDK path
/// (<see cref="McpServerTool.Create(System.Reflection.MethodInfo, object, McpServerToolCreateOptions)"/>
/// with the production serializer options) and invoked exactly as the executor invokes them, so this
/// pins the actual SDK 1.4.0 binding behavior rather than a mock of it.
/// </summary>
/// <remarks>
/// Findings pinned by this fixture (see the story-10 Dev Agent Record):
/// <list type="bullet">
/// <item>The SDK enforces the REQUIRED TOP-LEVEL parameter up front: a call missing it throws from
/// <see cref="McpServerTool.InvokeAsync"/> (via <c>AIFunctionFactory</c>) BEFORE the tool body runs, so
/// the command is never dispatched. In the live pipeline that throw is converted to a structured
/// <c>IsError</c> result by <c>McpToolErrorFilter</c> (see <c>McpToolErrorFilterTests</c>).</item>
/// <item>NESTED <c>[Required]</c> fields (the individual arguments a client sends inside the wrapped
/// <c>args</c> object) are ADVERTISED in the input schema's <c>required</c> array but are NOT
/// runtime-enforced by the SDK: System.Text.Json does not honor DataAnnotations <c>[Required]</c> on a
/// record constructor parameter, so a request that omits one deserializes it to null and reaches the
/// tool body. Closing that server-side gap generically is an FR-13 design decision (out of this story's
/// scope) — this fixture documents the current behavior so a future change is detected.</item>
/// </list>
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class RequiredArgValidationTests {

	// A spy: the tool method flips this to true when its body runs, so a test can assert the command
	// was NOT dispatched when a required argument was missing (AC-04: no execution on a missing arg).
	private static bool _probeInvoked;

	[SetUp]
	public void SetUp() => _probeInvoked = false;

	/// <summary>Arguments record with a single required field, mirroring the clio tool arg shape.</summary>
	public sealed record ProbeArgs(
		[property: JsonPropertyName("environment-name")]
		[property: System.ComponentModel.Description("A required identifier")]
		[property: Required]
		string EnvironmentName);

	[McpServerToolType]
	private static class ProbeToolType {
		internal const string ToolName = "required-arg-probe";

		[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false)]
		[System.ComponentModel.Description("Probe tool used to characterize required-arg validation.")]
		public static string Probe([System.ComponentModel.Description("probe parameters")] [Required] ProbeArgs args) {
			_probeInvoked = true;
			return $"invoked:{args?.EnvironmentName}";
		}
	}

	private static McpServerTool BuildProbeTool() =>
		McpServerTool.Create(
			typeof(ProbeToolType).GetMethod(nameof(ProbeToolType.Probe))!,
			target: null,
			new McpServerToolCreateOptions { SerializerOptions = BindingsModule.CreateMcpSerializerOptions() });

	// RequestContext's constructor rejects a null server, so build an uninitialized instance and set only
	// Params/MatchedPrimitive before InvokeAsync — the same shape the ClioRun executor uses in its tests.
	private static async Task<CallToolResult> InvokeAsync(McpServerTool tool, Dictionary<string, JsonElement> arguments) {
		RequestContext<CallToolRequestParams> context =
			(RequestContext<CallToolRequestParams>)System.Runtime.CompilerServices.RuntimeHelpers
				.GetUninitializedObject(typeof(RequestContext<CallToolRequestParams>));
		context.Params = new CallToolRequestParams { Name = ProbeToolType.ToolName, Arguments = arguments };
		context.MatchedPrimitive = tool;
		return await tool.InvokeAsync(context, CancellationToken.None);
	}

	[Test]
	[Category("Unit")]
	[Description("The SDK-emitted input schema advertises the required top-level args parameter, so a compliant client is told the argument is mandatory before it ever calls.")]
	public void InputSchema_ShouldAdvertiseRequiredTopLevelArgsParameter_WhenToolHasRequiredParameter() {
		// Arrange
		McpServerTool tool = BuildProbeTool();

		// Act
		JsonElement schema = tool.ProtocolTool.InputSchema;

		// Assert
		schema.TryGetProperty("required", out JsonElement required).Should().BeTrue(
			because: "a tool with a [Required] parameter must advertise a 'required' array in its input schema so clients validate up front");
		required.EnumerateArray().Select(item => item.GetString()).Should().Contain("args",
			because: "the required top-level 'args' parameter must be named in the schema's required list");
	}

	[Test]
	[Category("Unit")]
	[Description("The SDK-emitted input schema also advertises the nested required field inside the args object, so a schema-compliant client rejects a call omitting it before sending.")]
	public void InputSchema_ShouldAdvertiseNestedRequiredField_WhenArgsRecordHasRequiredProperty() {
		// Arrange
		McpServerTool tool = BuildProbeTool();

		// Act
		JsonElement schema = tool.ProtocolTool.InputSchema;
		JsonElement argsSchema = schema.GetProperty("properties").GetProperty("args");

		// Assert
		argsSchema.TryGetProperty("required", out JsonElement nestedRequired).Should().BeTrue(
			because: "the wrapped args object schema must carry its own 'required' array so clients see the mandatory inner fields");
		nestedRequired.EnumerateArray().Select(item => item.GetString()).Should().Contain("environment-name",
			because: "the required inner field must be advertised under its kebab-case wire name so a compliant client validates it before sending");
	}

	[Test]
	[Category("Unit")]
	[Description("A call with the whole required args object absent is rejected by the SDK before the tool body runs (throws from InvokeAsync), so the command is never dispatched (AC-04, top-level parameter granularity).")]
	public async Task Invoke_ShouldRejectBeforeDispatch_WhenRequiredArgsObjectIsMissing() {
		// Arrange
		McpServerTool tool = BuildProbeTool();

		// Act — no 'args' key at all: the required top-level parameter is missing.
		Func<Task> act = async () => await InvokeAsync(tool, []);

		// Assert
		await act.Should().ThrowAsync<ArgumentException>(
			because: "the SDK validates the required top-level parameter up front and refuses the call before the tool body runs");
		_probeInvoked.Should().BeFalse(
			because: "AC-04: a missing required argument must be rejected before the tool body (and command dispatch) runs — in the live pipeline McpToolErrorFilter turns this throw into a structured IsError result");
	}

	[Test]
	[Category("Unit")]
	[Description("Documents the current SDK limitation: a present args object missing its nested required field is NOT runtime-enforced — it deserializes to null and reaches the tool body. Pins the behavior so the FR-13 nested-enforcement decision is auditable.")]
	public async Task Invoke_ShouldReachBody_DocumentsNestedRequiredNotRuntimeEnforced_WhenInnerRequiredFieldMissing() {
		// Arrange
		McpServerTool tool = BuildProbeTool();
		Dictionary<string, JsonElement> arguments = new() {
			["args"] = JsonDocument.Parse("{}").RootElement
		};

		// Act — 'args' present but its required inner field 'environment-name' is absent.
		CallToolResult result = await InvokeAsync(tool, arguments);

		// Assert
		result.IsError.Should().NotBe(true,
			because: "System.Text.Json does not honor DataAnnotations [Required] on a record constructor parameter, so the SDK binds a null field and the body runs (no runtime rejection)");
		_probeInvoked.Should().BeTrue(
			because: "this pins the CURRENT gap — nested required fields are schema-advertised but not runtime-enforced; closing it server-side generically is the FR-13 design decision deferred to the architect");
	}
}
