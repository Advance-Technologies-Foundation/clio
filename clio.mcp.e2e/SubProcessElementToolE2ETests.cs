using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Command.ProcessModel;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the Sub-process element (ENG-92707) over the real MCP path. NOT in CI - run
/// manually, gated on the <c>process-designer</c> feature and a reachable environment carrying a
/// CrtProcessBuilder that supports the <c>subProcess</c> block.
/// <para>The called process is BUILT by the fixture rather than assumed to exist on the stand. It is an
/// ordinary process - start, end, two parameters - because that is all a callee has to be, and because a
/// fixture that depended on a particular process being present would fail for a reason that has nothing to do
/// with this element.</para>
/// <para>What only a live server can prove here: the platform's own parameter synchronization. Every unit
/// test drives it against a substituted schema manager; this is the only place the real
/// <c>ProcessSchemaActivity</c> diff runs against a real saved schema, and the only place the callee's
/// parameters actually arrive on the element through a save-and-read round trip.</para>
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateBusinessProcessTool.CreateBusinessProcessToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class SubProcessElementToolE2ETests {

	private const string ToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;
	private const string ModifyToolName = ModifyBusinessProcessTool.ModifyBusinessProcessToolName;
	private const string DescribeToolName = DescribeProcessTool.ToolName;

	#region Methods: Tests

	[Test]
	[Description("Over the real MCP path, create-business-process builds a sub-process element pointed at a process this fixture just created, and describe-business-process reads the callee back: the element resolves to the dedicated subprocess build type, its subProcess block names the called process, and the CALLED process's parameters have arrived on the element - which is the platform's synchronization running for real rather than against a substitute.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process builds a sub-process element and describe reads the callee back")]
	public async Task CreateBusinessProcess_Should_BuildSubProcessElement_AndReadTheCalleeBack() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		string calleeName = $"UsrClioBpSubCallee{Guid.NewGuid():N}";
		string callerName = $"UsrClioBpSubCaller{Guid.NewGuid():N}";
		await ArrangeProcessAsync(context, BuildCalleeDescriptor(calleeName), "called process");

		// Act
		CallToolResult callResult = await CreateAsync(context, BuildCallerDescriptor(callerName, calleeName));

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a sub-process element naming an existing, callable process must build without a transport error");
		// The success LINE, not merely the name: the command logs "Building process '<name>'..." BEFORE it calls
		// the server, so a name match alone also passes when the build then fails.
		JsonSerializer.Serialize(callResult).Should().Contain("created (UId:",
			because: "only a genuinely successful build logs the created-schema line (run against an environment "
				+ "whose CrtProcessBuilder supports the sub-process element)");

		DescribedElement element = (await DescribeAsync(context, callerName)).Elements
			.Single(candidate => candidate.Name == "SubProcess1");
		element.BuildType.Should().Be("subprocess",
			because: "a sub-process element round-trips to its dedicated build token; a null here is what a "
				+ "shipped element reported before this handler existed, because no handler claimed it");
		element.SubProcess.Should().NotBeNull(
			because: "describe surfaces the called process in its own block; a null on a successful build is the "
				+ "silent-drop signature of a server that predates the element");
		element.SubProcess!.Process.Should().Be(calleeName,
			because: "the block reports the callee as a resubmittable schema NAME, not only its stored UId - "
				+ "before this element the read-back carried no reference to the called process at all");
		element.SubProcess.ProcessUId.Should().NotBeNullOrWhiteSpace(
			because: "the stored UId is reported alongside the name, and it is what a caller feeds straight back");
		element.SubProcess.MultiInstance.Should().Be(false,
			because: "a plain call activity is single-instance. Asserted against FALSE rather than falsy, so a "
				+ "server that reported no such field at all fails here instead of reading as single-instance");
		element.SubProcess.InSync.Should().BeTrue(
			because: "the element mirrors the called process's parameters after the save-and-read round trip; a "
				+ "null here would mean the callee could not be read at all");
		element.Parameters.Select(parameter => parameter.Name).Should()
			.Contain(new[] { "OrderId", "Approved" },
				because: "THE acceptance criterion: selecting the called process is what makes the PLATFORM copy "
					+ "that process's parameters onto the element - nothing in the descriptor declares them, and "
					+ "this is the only test in the suite where the real diff runs against a real saved schema");
	}

	[Test]
	[Description("Over the real MCP path, a value mapped onto the element's input parameter survives the save and the read - the regression the two provenance stamps exist to prevent, and one that cannot be observed below a live server because the platform re-synchronizes the element on every design-time read.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process keeps a value mapped onto a sub-process input")]
	public async Task CreateBusinessProcess_Should_KeepAValueMappedOntoASubProcessInput() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		string calleeName = $"UsrClioBpSubMapCallee{Guid.NewGuid():N}";
		string callerName = $"UsrClioBpSubMapCaller{Guid.NewGuid():N}";
		await ArrangeProcessAsync(context, BuildCalleeDescriptor(calleeName), "called process");

		// Act
		CallToolResult callResult = await CreateAsync(context, BuildMappingCallerDescriptor(callerName, calleeName));

		// Assert
		JsonSerializer.Serialize(callResult).Should().Contain("created (UId:",
			because: "a mapping onto a synced input parameter is the ordinary route and must build");

		DescribedElement element = (await DescribeAsync(context, callerName)).Elements
			.Single(candidate => candidate.Name == "SubProcess1");
		DescribedParameter orderId = element.Parameters.Single(parameter => parameter.Name == "OrderId");
		orderId.Value.Should().NotBeNullOrWhiteSpace(
			because: "the value survives only while the parameter's CreatedInSchemaUId (the CALLED process) "
				+ "differs from the value's ModifiedInSchemaUId (the CALLER) - re-stamping the first, which is "
				+ "what the Pre-configured page element correctly does for its own rule, erases it on the next "
				+ "read with no error anywhere");
		orderId.Direction.Should().Be("In",
			because: "only an In or Variable parameter can hold a caller-written value at all; the platform "
				+ "clears every other one on every synchronization");
	}

	[Test]
	[Description("Over the real MCP path, setElement with resync:true re-synchronizes the element against the process it already calls and reports the drift - the acceptance criterion that cannot be checked in-memory, because the platform converges the element on every design-time read and leaves no stale 'before' to diff against.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process re-synchronizes a sub-process element and reports the drift")]
	public async Task ModifyBusinessProcess_Should_ResyncASubProcessElement() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		string calleeName = $"UsrClioBpSubResyncCallee{Guid.NewGuid():N}";
		string callerName = $"UsrClioBpSubResyncCaller{Guid.NewGuid():N}";
		await ArrangeProcessAsync(context, BuildCalleeDescriptor(calleeName), "called process");
		await ArrangeProcessAsync(context, BuildCallerDescriptor(callerName, calleeName), "calling process");
		// The called process gains a parameter after the caller was built. This is the state the whole re-sync
		// exists for: at run time a value aimed at a name the other side does not carry is skipped with no
		// exception and no log line, so the element keeps running and quietly delivers nothing.
		await CallToolAsync(context, ModifyToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = calleeName,
			["operations"] = """
				[ { "op": "addParameter",
				    "parameter": { "name": "Comment", "type": "Text", "direction": "In" } } ]
				"""
		});

		// Act
		CallToolResult callResult = await CallToolAsync(context, ModifyToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = callerName,
			["operations"] = """
				[ { "op": "setElement", "elementName": "SubProcess1",
				    "elementUpdate": { "subProcess": { "resync": true } } } ]
				"""
		});

		// A SECOND re-synchronization, because idempotency is the half of AC-3 that a single run cannot show.
		CallToolResult second = await CallToolAsync(context, ModifyToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = callerName,
			["operations"] = """
				[ { "op": "setElement", "elementName": "SubProcess1",
				    "elementUpdate": { "subProcess": { "resync": true } } } ]
				"""
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a pure re-synchronization must complete without a transport error - and setElement refuses "
				+ "an update that names no field, so this is the only way to ask for one");
		second.IsError.Should().NotBeTrue(
			because: "running it twice must not fail the second time; AC-3 asks for an idempotent operation");

		DescribedElement element = (await DescribeAsync(context, callerName)).Elements
			.Single(candidate => candidate.Name == "SubProcess1");
		element.SubProcess.Should().NotBeNull(
			because: "a null here is the silent-drop signature, and dereferencing it below would report a "
				+ "NullReferenceException instead of the assertion this test was written for");
		element.Parameters.Select(parameter => parameter.Name).Should().Contain("Comment",
			because: "the parameter the called process gained has to be on the element. READ THIS ASSERTION "
				+ "HONESTLY: the platform re-synchronizes every sub-process element on every design-time read, so "
				+ "the describe that produced this answer also converged it in memory - what this shows is that "
				+ "the pair is consistent, NOT that the re-synchronization persisted anything. Only the stored "
				+ "schema metadata can show that, which is a stand-level check and lives in the manual cases");
		element.SubProcess!.InSync.Should().BeTrue(
			because: "the element mirrors the called process - for the same read-time reason, which is why "
				+ "inSync's informative value is false and not true");
	}

	[Test]
	[Description("Over the real MCP path, a process cannot call ITSELF: the platform accepts that write and then synchronizes nothing, so without the refusal the process saves green with an element that carries no parameters and no complaint.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process refuses a sub-process element that calls its own process")]
	public async Task CreateBusinessProcess_Should_RefuseASelfReferencingSubProcess() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		string processName = $"UsrClioBpSubSelf{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CreateAsync(context, BuildCallerDescriptor(processName, processName));

		// Assert
		JsonSerializer.Serialize(callResult).Should().NotContain("created (UId:",
			because: "the build has to be refused rather than saved - a self-referencing element is the platform's "
				+ "one silent no-op, and the process would look healthy afterwards");
		JsonSerializer.Serialize(callResult).Should().Contain("cannot call itself",
			because: "the refusal has to name what is wrong, or the caller cannot tell it from any other failure");
	}

	[Test]
	[Description("Over the real MCP path, a subProcess block riding on another element type is REFUSED rather than dropped. This is the measurement the whole no-version-floor decision rests on: an unknown BLOCK on a known type is discarded in silence with a success answer, while the unknown TYPE token is refused loudly - so binding the block to its token is what makes an older server tell the caller.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process refuses a subProcess block on another element type")]
	public async Task CreateBusinessProcess_Should_RefuseASubProcessBlockOnAnotherElementType() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		string calleeName = $"UsrClioBpSubMisplacedCallee{Guid.NewGuid():N}";
		string processName = $"UsrClioBpSubMisplaced{Guid.NewGuid():N}";
		await ArrangeProcessAsync(context, BuildCalleeDescriptor(calleeName), "called process");

		// Act
		CallToolResult callResult = await CreateAsync(context,
			BuildMisplacedSubProcessDescriptor(processName, calleeName));

		// Assert
		JsonSerializer.Serialize(callResult).Should().NotContain("created (UId:",
			because: "a misplaced block must abort the build; saving the process with the block dropped is the "
				+ "exact silent path this binding exists to close");
		JsonSerializer.Serialize(callResult).Should().Contain("subProcess",
			because: "the refusal names the block, so the caller can see which field to move or remove");
	}

	#endregion

	#region Methods: Descriptors

	// An ORDINARY process, which is all a callee has to be: a Simple start event (rule R16 - a process that
	// starts only on a signal, a timer or a message has no entry point a call can use), an end, and the two
	// parameters the caller's element will mirror.
	private static string BuildCalleeDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP SubProcess callee E2E",
		  "packageName": "Custom",
		  "parameters": [
		    { "name": "OrderId", "type": "Guid", "direction": "In" },
		    { "name": "Approved", "type": "Boolean", "direction": "Out" }
		  ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [ { "source": "StartEvent1", "target": "EndEvent1" } ]
		}
		""";

	private static string BuildCallerDescriptor(string processName, string calleeName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP SubProcess caller E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "SubProcess1", "type": "subProcess", "caption": "Call the order process",
		      "subProcess": { "processName": "{{calleeName}}" } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "SubProcess1" },
		    { "source": "SubProcess1", "target": "EndEvent1" }
		  ]
		}
		""";

	// The same caller with a value mapped into the element's synced INPUT. Nothing declares that parameter:
	// it exists only because selecting the called process copied it onto the element, which is why this
	// descriptor is also the proof that the element's parameters are addressable by name straight away.
	private static string BuildMappingCallerDescriptor(string processName, string calleeName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP SubProcess mapping E2E",
		  "packageName": "Custom",
		  "parameters": [ { "name": "OrderToCheck", "type": "Guid", "direction": "In" } ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "SubProcess1", "type": "subProcess", "caption": "Call the order process",
		      "subProcess": { "processName": "{{calleeName}}" } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "SubProcess1" },
		    { "source": "SubProcess1", "target": "EndEvent1" }
		  ],
		  "mappings": [
		    { "elementName": "SubProcess1", "elementParameter": "OrderId",
		      "processParameter": "OrderToCheck" }
		  ]
		}
		""";

	private static string BuildMisplacedSubProcessDescriptor(string processName, string calleeName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP SubProcess misplaced E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent",
		      "subProcess": { "processName": "{{calleeName}}" } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [ { "source": "StartEvent1", "target": "EndEvent1" } ]
		}
		""";

	#endregion

	#region Methods: Arrange

	/// <summary>
	/// Builds a process for an ARRANGE step and fails the test on the spot if it did not build.
	/// <para>Discarding this result is how a broken arrange reaches the assertions disguised as the thing under
	/// test: the callee fails to build, the caller then cannot resolve it, and the failure reads as a defect in
	/// the sub-process element rather than in the two lines above it.</para>
	/// </summary>
	private static async Task<CallToolResult> ArrangeProcessAsync(ArrangeContext context, string descriptor,
			string what) {
		CallToolResult result = await CreateAsync(context, descriptor);
		result.IsError.Should().NotBeTrue(
			because: $"the {what} is an arrange step - if it did not build, every assertion below is about the "
				+ "wrong failure");
		return result;
	}

	private static async Task<CallToolResult> CreateAsync(ArrangeContext context, string descriptor) =>
		await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = descriptor
		});

	private static async Task<DescribeProcessResult> DescribeAsync(ArrangeContext context, string processName) =>
		ParseDescribeGraph(await CallToolAsync(context, DescribeToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName
		}));

	// Deserializes the described graph (the Info log-message value inside the clio command envelope) into the
	// typed model, the same way the Approval fixture does.
	private static DescribeProcessResult ParseDescribeGraph(CallToolResult describeResult) {
		CommandExecutionEnvelope envelope = McpCommandExecutionParser.Extract(describeResult);
		string graphJson = envelope.Output!
			.Select(message => message.Value)
			.First(value => !string.IsNullOrWhiteSpace(value)
				&& value!.TrimStart().StartsWith("{", StringComparison.Ordinal))!;
		return JsonSerializer.Deserialize<DescribeProcessResult>(graphJson,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
	}

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext context, string toolName,
			Dictionary<string, object?> args) {
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		toolNames.Should().Contain(toolName,
			because: "the tool must be discoverable before the end-to-end call");
		return await context.Session.CallToolAsync(
			toolName, new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
	}

	private static async Task<ArrangeContext> ArrangeAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore(
				"Configure McpE2E:Sandbox:EnvironmentName (with a CrtProcessBuilder that supports the sub-process "
				+ "element) to run the Sub-process MCP E2E tests.");
		}

		if (!await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName!)) {
			Assert.Ignore(
				$"Sub-process MCP E2E requires a reachable configured sandbox environment. '{environmentName}' was not reachable.");
		}

		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(5));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource, environmentName);
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string? EnvironmentName) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}

	#endregion

}
