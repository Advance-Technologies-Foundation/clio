using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Command.ProcessModel;
using Clio.Common;
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

	// The caller's resource key for the element parameter's caption: captions are not metadata, they live in
	// SysLocalizableValue under name-derived keys.
	private const string OrderIdElementCaptionKey = "BaseElements.SubProcess1.Parameters.OrderId.Caption";

	private const string Resync = """
		[ { "op": "setElement", "elementName": "SubProcess1",
		    "elementUpdate": { "subProcess": { "resync": true } } } ]
		""";

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
	[Description("ENG-100077: after a caption-only change on the called process, ONE resync of the caller stores the called process's CURRENT caption. Read from SysLocalizableValue, not from describe: every read re-synchronizes, so describe can show a caption the database does not hold. Before the fix the package's save left the callee's resource cache holding the pre-edit captions and this row stayed one save behind.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process resync stores the called process's current caption")]
	public async Task ModifyBusinessProcess_Should_StoreTheCalleesCurrentCaption_OnOneResync() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(needsSql: true);
		string calleeName = $"UsrClioBpCapResyncCallee{Guid.NewGuid():N}";
		string callerName = $"UsrClioBpCapResyncCaller{Guid.NewGuid():N}";
		await ArrangeProcessAsync(context, BuildCaptionedCalleeDescriptor(calleeName), "called process");
		await ArrangeProcessAsync(context, BuildCallerDescriptor(callerName, calleeName), "calling process");
		await ArrangeModifyAsync(context, calleeName, SetOrderIdCaption("Order id v2"), "caption-only callee change");

		// Act
		CallToolResult result = await ModifyAsync(context, callerName, Resync);

		// Assert
		McpCommandExecutionParser.Extract(result).ExitCode.Should().Be(0,
			because: "a resync against a readable callee must succeed");
		(await ReadStoredCaptionsAsync(context, callerName, OrderIdElementCaptionKey)).Should()
			.ContainSingle(because: "the caller stores one caption row for the element parameter")
			.Which.Should().Be("Order id v2",
				because: "one resync must store the callee's CURRENT caption - the stale 'Order id v1' was "
					+ "written back here while the callee's resource cache still held its pre-edit captions");
	}

	[Test]
	[Description("ENG-100077: an INCIDENTAL save of the caller - an edit that never mentions the sub-process element - also stores the called process's current caption. Every design-time load re-synchronizes the element and the save persists it, so before the fix such an edit silently wrote the callee's STALE caption, and could revert a correct row.")]
	[AllureTag(ModifyToolName)]
	[AllureName("an incidental caller save stores the called process's current caption")]
	public async Task ModifyBusinessProcess_Should_StoreTheCalleesCurrentCaption_OnAnIncidentalCallerSave() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(needsSql: true);
		string calleeName = $"UsrClioBpCapIncCallee{Guid.NewGuid():N}";
		string callerName = $"UsrClioBpCapIncCaller{Guid.NewGuid():N}";
		await ArrangeProcessAsync(context, BuildCaptionedCalleeDescriptor(calleeName), "called process");
		await ArrangeProcessAsync(context, BuildCallerDescriptor(callerName, calleeName), "calling process");
		await ArrangeModifyAsync(context, calleeName, SetOrderIdCaption("Order id v2"), "caption-only callee change");

		// Act - an edit about something else entirely
		CallToolResult result = await ModifyAsync(context, callerName, """
			[ { "op": "addParameter",
			    "parameter": { "name": "Note", "type": "Text", "direction": "In", "caption": "Note" } } ]
			""");

		// Assert
		McpCommandExecutionParser.Extract(result).ExitCode.Should().Be(0,
			because: "an unrelated edit of the caller must succeed");
		(await ReadStoredCaptionsAsync(context, callerName, OrderIdElementCaptionKey)).Should()
			.ContainSingle(because: "the caller stores one caption row for the element parameter")
			.Which.Should().Be("Order id v2",
				because: "the save persists what the load's re-synchronization copied, so it must be the callee's "
					+ "current caption and never the one from before the callee's last edit");
	}

	[TestCase(true, TestName = "DescribeBusinessProcess_Should_ShowANewCalleeParametersCaption_WhenTheCalleeIsReadFirst")]
	[TestCase(false, TestName = "DescribeBusinessProcess_Should_ShowANewCalleeParametersCaption_WhenACallerIsReadFirst")]
	[Description("ENG-100077: a parameter the called process GAINS carries its caption on every caller, whatever is read first. Before the fix the callee's cached build had no caption for the new key, so a caller built from it showed an empty caption - measured on both read orders.")]
	[AllureTag(DescribeToolName)]
	[AllureName("a new callee parameter shows its caption on every caller")]
	public async Task DescribeBusinessProcess_Should_ShowANewCalleeParametersCaption(bool calleeFirst) {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		string calleeName = $"UsrClioBpCapNewCallee{Guid.NewGuid():N}";
		string firstCallerName = $"UsrClioBpCapNewCallerA{Guid.NewGuid():N}";
		string secondCallerName = $"UsrClioBpCapNewCallerB{Guid.NewGuid():N}";
		await ArrangeProcessAsync(context, BuildCaptionedCalleeDescriptor(calleeName), "called process");
		await ArrangeProcessAsync(context, BuildCallerDescriptor(firstCallerName, calleeName), "first calling process");
		await ArrangeProcessAsync(context, BuildCallerDescriptor(secondCallerName, calleeName), "second calling process");
		await ArrangeModifyAsync(context, calleeName, """
			[ { "op": "addParameter",
			    "parameter": { "name": "Urgent", "type": "Boolean", "direction": "In", "caption": "Urgent flag" } } ]
			""", "new callee parameter");

		// Act
		if (calleeFirst) {
			await DescribeAsync(context, calleeName);
		}
		DescribedElement first = (await DescribeAsync(context, firstCallerName)).Elements
			.Single(candidate => candidate.Name == "SubProcess1");
		DescribedElement second = (await DescribeAsync(context, secondCallerName)).Elements
			.Single(candidate => candidate.Name == "SubProcess1");

		// Assert
		first.Parameters.Single(parameter => parameter.Name == "Urgent").Caption.Should().Be("Urgent flag",
			because: "the caller read first after the callee gained the parameter must carry its caption - it was "
				+ "the one left empty before the fix");
		second.Parameters.Single(parameter => parameter.Name == "Urgent").Caption.Should().Be("Urgent flag",
			because: "every caller carries the new parameter's caption regardless of read order");
	}

	[Test]
	[Description("ENG-100077: a resync that changes a caption SAYS so, naming the parameter and both captions; a second resync with nothing left to change says nothing about captions. The report is measured against the caller's STORED caption, because the load has already converged the in-memory element.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process resync reports a caption change")]
	public async Task ModifyBusinessProcess_Should_ReportACaptionChange_InTheResyncAnswer() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		string calleeName = $"UsrClioBpCapReportCallee{Guid.NewGuid():N}";
		string callerName = $"UsrClioBpCapReportCaller{Guid.NewGuid():N}";
		await ArrangeProcessAsync(context, BuildCaptionedCalleeDescriptor(calleeName), "called process");
		await ArrangeProcessAsync(context, BuildCallerDescriptor(callerName, calleeName), "calling process");
		await ArrangeModifyAsync(context, calleeName, SetOrderIdCaption("Order id v2"), "caption-only callee change");

		// Act
		CallToolResult first = await ModifyAsync(context, callerName, Resync);
		CallToolResult second = await ModifyAsync(context, callerName, Resync);

		// Assert
		McpCommandExecutionParser.Extract(first).ExitCode.Should().Be(0,
			because: "a missing notice must not be the disguise of a failed resync");
		McpCommandExecutionParser.Extract(second).ExitCode.Should().Be(0,
			because: "running the resync again must succeed too");
		WarningsOf(first).Should().Contain(warning => warning.Contains("'OrderId' from 'Order id v1' to 'Order id v2'"),
			because: "the resync replaced a stored caption, and the caller has to be told which one and to what");
		WarningsOf(second).Should().NotContain(warning => warning.Contains("replaced with the called process's")
				|| warning.Contains("had no caption stored under"),
			because: "the first resync stored the current caption, so the second has no caption to report at all");
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

	// The ordinary callee with CAPTIONS, which the ENG-100077 cases change and then read back from the caller.
	private static string BuildCaptionedCalleeDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP SubProcess caption callee E2E",
		  "packageName": "Custom",
		  "parameters": [
		    { "name": "OrderId", "type": "Guid", "direction": "In", "caption": "Order id v1" },
		    { "name": "Approved", "type": "Boolean", "direction": "Out", "caption": "Approved" }
		  ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [ { "source": "StartEvent1", "target": "EndEvent1" } ]
		}
		""";

	private static string SetOrderIdCaption(string caption) =>
		$$"""
		[ { "op": "setParameter", "parameterName": "OrderId",
		    "parameterUpdate": { "caption": "{{caption}}" } } ]
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
	/// <para>It asserts the SUCCESS LINE, not <c>IsError</c>. A refused or failed build comes back as an ordinary
	/// result carrying a non-zero exit code, and nothing on the MCP path turns that into <c>IsError</c> - which is
	/// set for a binding error or a thrown exception. That is why every acting assertion in this fixture pairs the
	/// two; the refusal cases assert the ABSENCE of that line plus the refusal text, and one case asserts the
	/// described result instead. An arrange guard that checked only the flag would have passed the exact failure
	/// it exists to catch.</para>
	/// </summary>
	private static async Task<CallToolResult> ArrangeProcessAsync(ArrangeContext context, string descriptor,
			string what) {
		CallToolResult result = await CreateAsync(context, descriptor);
		result.IsError.Should().NotBeTrue(
			because: $"the {what} is an arrange step - if it did not build, every assertion below is about the "
				+ "wrong failure");
		JsonSerializer.Serialize(result).Should().Contain("created (UId:",
			because: $"only a genuinely successful build logs the created-schema line, and the {what} has to "
				+ "exist before the case below means anything");
		return result;
	}

	private static async Task<CallToolResult> ModifyAsync(ArrangeContext context, string processName,
			string operations) =>
		await CallToolAsync(context, ModifyToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = operations
		});

	/// <summary>A modify used as an ARRANGE step, failed on the spot when it did not succeed.</summary>
	private static async Task ArrangeModifyAsync(ArrangeContext context, string processName, string operations,
			string what) {
		CallToolResult result = await ModifyAsync(context, processName, operations);
		McpCommandExecutionParser.Extract(result).ExitCode.Should().Be(0,
			because: $"the {what} is an arrange step - if it failed, every assertion below is about the wrong failure");
	}

	private static IReadOnlyList<string> WarningsOf(CallToolResult result) =>
		(McpCommandExecutionParser.Extract(result).Output ?? [])
			.Where(message => message.MessageType == LogDecoratorType.Warning && message.Value != null)
			.Select(message => message.Value!)
			.ToList();

	/// <summary>
	/// The caller's STORED rows for one resource key, every culture, read through <c>execute-sql-script</c>.
	/// <para>The database and not <c>describe</c>, on purpose: the platform re-synchronizes on every read, so a
	/// describe shows the called process's caption whether or not it was ever saved. Identifiers are
	/// double-quoted so the statement runs on MSSQL and PostgreSQL alike.</para>
	/// </summary>
	private static async Task<IReadOnlyList<string?>> ReadStoredCaptionsAsync(ArrangeContext context,
			string schemaName, string key) {
		string directory = Path.Combine(Path.GetTempPath(), "clio-e2e-captions", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		try {
			return await ReadStoredCaptionsIntoAsync(context, schemaName, key, Path.Combine(directory, "captions.json"));
		} finally {
			Directory.Delete(directory, recursive: true);
		}
	}

	// Every culture is read and the callers expect ONE row: a server-side write stores the current culture only,
	// and these processes are created and edited through clio alone, so a second row would itself be a finding.
	private static async Task<IReadOnlyList<string?>> ReadStoredCaptionsIntoAsync(ArrangeContext context,
			string schemaName, string key, string destination) {
		string sql = "SELECT v.\"Value\" AS value FROM \"SysLocalizableValue\" v "
			+ "INNER JOIN \"SysSchema\" s ON s.\"Id\" = v.\"SysSchemaId\" "
			+ $"WHERE s.\"Name\" = '{schemaName}' AND v.\"Key\" = '{key}'";
		CallToolResult result = await CallToolAsync(context, ExecuteSqlScriptTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["script"] = sql,
				["view"] = "json",
				["destination-path"] = destination,
				["silent"] = true
			});
		McpCommandExecutionParser.Extract(result).ExitCode.Should().Be(0,
			because: "the caption rows have to be readable for this case to measure anything");
		using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(destination));
		return document.RootElement.EnumerateArray()
			.Select(row => row.GetProperty("value").GetString())
			.ToList();
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

	private static async Task<ArrangeContext> ArrangeAsync(bool needsSql = false) {
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
		if (needsSql) {
			// execute-sql-script runs through cliogate.
			await ClioCliCommandRunner.EnsureCliogateInstalledAsync(settings, environmentName!,
				cancellationTokenSource.Token);
		}
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
