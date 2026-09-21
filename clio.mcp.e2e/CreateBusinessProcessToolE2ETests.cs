using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Command.ProcessModel;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for <c>create-business-process</c>. NOT in CI — run manually. The advertised-tool test
/// is hermetic; the functional test builds a real (uniquely named) process and is gated on a reachable
/// environment with the ProcessDesignService package and a writable "Custom" package.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateBusinessProcessTool.CreateBusinessProcessToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class CreateBusinessProcessToolE2ETests {

	private const string ToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;

	// ENG-92127 element-output mapping tests need a user task that (a) ships in CrtBase — always installed on any
	// stand — and (b) exposes a scalar OUTPUT parameter (a valid mapping source). CheckCanExecuteOperationUserTask
	// resolves by name via ProcessUserTaskSchemaManager (the builder's full superset, not the palette subset), and
	// its Boolean output CanExecuteOperation is the source. Swap these two constants if a stand lacks it — e.g. the
	// palette task ActivityUserTask (alias performTask) with the Guid output ActivityResult.
	private const string SourceUserTaskName = "CheckCanExecuteOperationUserTask";
	private const string SourceOutputParameter = "CanExecuteOperation";

	// element->element pairing (AC#1): performTask (ActivityUserTask) exposes the Guid output ActivityResult, which
	// flows into CheckCanExecuteOperationUserTask's Guid input UserId (Guid<->Guid). Both tasks and this mapping are
	// live-verified on the stand. performTask is a built-in alias, so it ships wherever the designer does.
	private const string ElementSourceTaskType = "performTask";
	private const string ElementSourceOutput = "ActivityResult";
	private const string ElementTargetInput = "UserId";

	// Open edit page (ENG-92715). AccountPageV2 is the base product's Classic Account edit page and is registered
	// on the Accounts section, so it is inside the designer's own candidate set on any stand. It also belongs to an
	// UNTYPED object, which is what keeps these tests free of a recordType discriminator. Swap the pair if a stand
	// lacks it - any section-registered edit page of an untyped object works.
	private const string OpenEditPageName = "AccountPageV2";
	private const string OpenEditPageObject = "Account";

	// A lookup column present on Account on every environment, so the results list needs no seeded metadata.
	private const string OpenEditPageResultColumn = "Owner";

	// A platform role present on every environment, so the name-resolution path can be exercised without seeding data.
	private const string OpenEditPagePerformerRole = "All employees";
	// A text column of that object, for the add-mode pre-filled value: a constant is only storable on a TEXT column.
	private const string OpenEditPageTextColumn = "Address";

	[Test]
	[Description("Starts the real clio MCP server and verifies create-business-process is discoverable via the get-tool-contract compact index (hermetic).")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process is discoverable on the lazy surface")]
	public async Task CreateBusinessProcess_Should_Be_Advertised_By_Mcp_Server() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: false);

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: $"the {ToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
	}

	[Test]
	[Description("Over the real MCP path, create-business-process builds a uniquely named process from an inline descriptor.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process builds a process from an inline descriptor")]
	public async Task CreateBusinessProcess_Should_BuildProcess_FromInlineDescriptor() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpE2e{Guid.NewGuid():N}";
		string descriptor = BuildDescriptor(processName);

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = descriptor
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a successful build should return a normal MCP tool result, not a transport error");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain(processName,
			because: "a successful build reports the created schema name (run against an environment with the ProcessDesignService package and a writable Custom package)");
		callResultJson.Should().Contain(CommandExecutionResult.CompileNotRequiredNote,
			because: "a clio-built process is interpreted and needs no compile; the success result carries the compile-not-required note over the real MCP path so an agent does not force compile-creatio (ENG-95706)");

		// Readback: describe the built process and confirm the structure is really there — a server that
		// returned success but built nothing would be caught here, unlike the success-echo assertion above.
		string describeJson = JsonSerializer.Serialize(await DescribeAsync(context, processName));
		describeJson.Should().Contain("task1",
			because: "the read-back graph must contain the user-task element that was actually built");
		describeJson.Should().Contain("buildType",
			because: "describe returns the structured element graph (buildType tokens), confirming a real build rather than an echo");
	}

	[Test]
	[Description("Over the real MCP path, create-business-process accepts the ENG-92127 type-mirror (typeFromElement) parameter, and describe-business-process reads the built process back with each parameter's direction surfaced.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process mirrors an element parameter's type and describe surfaces direction")]
	public async Task CreateBusinessProcess_Should_MirrorElementParameterType_AndSurfaceDirectionOnReadback() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpMapE2e{Guid.NewGuid():N}";
		string descriptor = BuildTypeMirrorDescriptor(processName);

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = descriptor
		});

		// Assert — the tool forwards the extended ENG-92127 contract and the server builds the process
		callResult.IsError.Should().NotBeTrue(
			because: "a descriptor using the typeFromElement type-mirror must build without a transport error");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain(processName,
			because: "a successful build reports the created schema name (run against an environment with the ProcessDesignService package and the ActivityUserTask 'Recommendation' parameter)");

		// Readback: the type-mirror process parameter exists, and describe now surfaces each parameter's direction
		// (the clio DescribedParameter DTO no longer strips direction/isResult — ENG-92127 describe enhancement).
		string describeJson = JsonSerializer.Serialize(await DescribeAsync(context, processName));
		describeJson.Should().Contain("MirroredType",
			because: "the typeFromElement type-mirror created a process parameter cloning the element parameter's exact type");
		// The describe graph is embedded as an escaped JSON string in the tool result, so match a quote-free
		// substring (like the sibling readbacks Contain("task1")/Contain("buildType")) — a quoted "direction"
		// would appear as \"direction\" and never match.
		describeJson.Should().Contain("direction",
			because: "describe-business-process now surfaces each parameter's direction over the real MCP path (the clio DescribedParameter DTO no longer strips it), so a caller can tell an element's outputs from its inputs");
	}

	[Test]
	[Description("Over the real MCP path, create-business-process maps a user-task element OUTPUT into a process parameter via a targetProcessParameter mapping (element->process, ENG-92127 AC#2); describe reads the process parameter and the element output back.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process maps an element output into a process parameter")]
	public async Task CreateBusinessProcess_Should_MapElementOutputIntoProcessParameter() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpOutMapE2e{Guid.NewGuid():N}";
		string descriptor = BuildElementOutputToProcessDescriptor(processName);

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = descriptor
		});

		// Assert — the server accepts the element->process mapping (an invalid one would error) and reports the build
		callResult.IsError.Should().NotBeTrue(
			because: "mapping a compatible element output into a process parameter must build without a transport error");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain(processName,
			because: $"a successful build reports the created schema name (run against an environment with CrtBase's {SourceUserTaskName})");

		// Readback: the target process parameter and the source element output are both present in the graph. The
		// binding value on the process parameter is an element-qualified meta-path built from UIds (not names), so we
		// assert the surfaced names + a successful build rather than substring-matching the meta-path.
		string describeJson = JsonSerializer.Serialize(await DescribeAsync(context, processName));
		describeJson.Should().Contain("ProcResult",
			because: "the target process parameter that receives the element output is present in the read-back graph");
		describeJson.Should().Contain(SourceOutputParameter,
			because: "the element's output parameter (the mapping source) is surfaced by describe because it is a result/output");
	}

	[Test]
	[Description("Over the real MCP path, a build descriptor's mappings[] may carry an 'expression' source, and it is validated, stored and read back as a Script. The formula is checked by the PLATFORM at the pre-save gate, not by clio's package - from CrtProcessBuilder 1.4.0.41 the package does not look at it at all - so what this proves is that a valid formula survives the gate, is stored as a Script and reads back. The modify path had this coverage and create did not, while the floor was raised on both.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process stores a formula mapping that reads back")]
	public async Task CreateBusinessProcess_Should_StoreAndReadBackAFormulaMapping() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpCreateFormulaE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildFormulaMappingDescriptor(processName, "FormulaUtilities.Max(1, 2, 3)")
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "FormulaUtilities.Max is one of the four Creatio formula functions and its result fits a Float parameter, so the build must be accepted on the create path exactly as it is on modify");
		string describeJson = JsonSerializer.Serialize(await DescribeAsync(context, processName));
		describeJson.Should().Contain("FormulaUtilities.Max(1, 2, 3)",
			because: "the formula text must survive the save verbatim - the platform, not clio, decides its meaning");
		describeJson.Should().Contain("Script",
			because: "a formula is stored as a Script source, not a constant; that is how the runtime knows to evaluate it rather than read it");
	}

	[Test]
	[Description("Over the real MCP path, a build descriptor's formula whose result cannot become the target's declared type is refused BY THE SERVER, and the process is not created. Without this the create path would accept what modify refuses, and the difference would only surface at run time.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process refuses a formula the target type cannot hold")]
	public async Task CreateBusinessProcess_Should_RefuseAFormulaTheTargetTypeCannotHold() {
		// Arrange - the target is Integer and the formula is fractional.
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpCreateBadFormulaE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildIntegerTargetFormulaDescriptor(processName, "1.5")
		});

		// Assert
		// NOT on callResult.IsError: measured null on this refusal, exactly as it is on success — every
		// sibling assertion in this fixture is deliberately NotBeTrue for that reason, so the flag carries no
		// signal here. What the description promises is that nothing was CREATED, so that is what is asserted.
		string describeJson = JsonSerializer.Serialize(await DescribeAsync(context, processName));
		describeJson.Should().Contain("was not found",
			because: "the description promises the process is not created, and the refusal now comes from the "
				+ "whole-schema save gate rather than from the operation - so atomicity is newly load-bearing "
				+ "on this path and asserting only the message text would not notice a half-created schema. "
				+ "Asserted POSITIVELY on the not-found answer rather than as NotContain(processName): the "
				+ "not-found message quotes the name it looked for, so the negative form fails on a correct "
				+ "result");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain("Int32",
			because: "the refusal must name the type the result cannot become, so the caller can correct the formula instead of guessing");
		callResultJson.Should().Contain("1.5",
			because: "the refusal must quote the expression, or the caller cannot find it in a multi-mapping "
				+ "descriptor. Deliberately a SUBSTRING and not equality to \"1.5\": the platform quotes the "
				+ "expression as its own converter left it, and the converter suffixes a fractional literal with m "
				+ "- measured, the message reads 'Error while executing expression \"1.5m\"'. An earlier version of "
				+ "this rationale claimed the text is quoted AS WRITTEN, which the same measurement disproves: a "
				+ "parameter reference is shown by the parameter NAME, not by the metapath the caller sent");
	}

	private static string BuildFormulaMappingDescriptor(string processName, string expression) =>
		"{\"name\":\"" + processName + "\",\"caption\":\"Clio BP Create Formula E2E\",\"packageName\":\"Custom\","
		+ "\"elements\":[{\"name\":\"StartEvent1\",\"type\":\"startEvent\"},"
		+ "{\"name\":\"EndEvent1\",\"type\":\"endEvent\"}],"
		+ "\"flows\":[{\"source\":\"StartEvent1\",\"target\":\"EndEvent1\"}],"
		+ "\"parameters\":[{\"name\":\"Sum\",\"type\":\"Float\",\"direction\":\"Variable\"}],"
		+ "\"mappings\":[{\"targetProcessParameter\":\"Sum\",\"expression\":\""
		+ expression.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}]}";

	private static string BuildIntegerTargetFormulaDescriptor(string processName, string expression) =>
		"{\"name\":\"" + processName + "\",\"caption\":\"Clio BP Create Bad Formula E2E\",\"packageName\":\"Custom\","
		+ "\"elements\":[{\"name\":\"StartEvent1\",\"type\":\"startEvent\"},"
		+ "{\"name\":\"EndEvent1\",\"type\":\"endEvent\"}],"
		+ "\"flows\":[{\"source\":\"StartEvent1\",\"target\":\"EndEvent1\"}],"
		+ "\"parameters\":[{\"name\":\"Count\",\"type\":\"Integer\",\"direction\":\"Variable\"}],"
		+ "\"mappings\":[{\"targetProcessParameter\":\"Count\",\"expression\":\""
		+ expression.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}]}";

	[Test]
	[Description("Over the real MCP path, create-business-process REJECTS an element->process mapping whose types are incompatible (a Boolean element output into an Integer process parameter), enforcing the ENG-92127 type-compatibility rule (AC#3).")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process rejects an incompatible-type mapping")]
	public async Task CreateBusinessProcess_Should_RejectMapping_WhenTypesAreIncompatible() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpBadTypeE2e{Guid.NewGuid():N}";
		string descriptor = BuildIncompatibleTypeMappingDescriptor(processName);

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = descriptor
		});

		// Assert — the type-compatibility gate rejects a Boolean source into an Integer target; the server's rejection
		// message (authored in ProcessMappingService) is surfaced through the MCP result (verified live on the stand
		// for the equivalent modify-business-process addMapping: "... incompatible data value types ...").
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain("incompatible",
			because: "mapping a Boolean element output into an Integer process parameter must be rejected by the type-compatibility check");
	}

	[Test]
	[Description("Over the real MCP path, create-business-process REJECTS a self-referential parameter mapping (a process parameter mapped to itself) with the platform's circular-dependency validation — the pre-save interpretation-validation gate, which the per-mapping type check cannot catch.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process rejects a self-referential (circular) parameter mapping")]
	public async Task CreateBusinessProcess_Should_RejectSelfReferentialMapping_WithCircularDependency() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpCycleE2e{Guid.NewGuid():N}";
		string descriptor = BuildSelfReferentialMappingDescriptor(processName);

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = descriptor
		});

		// Assert — the pre-save platform interpretation-validation gate rejects the self-referential mapping.
		// Requires a stand whose CrtProcessBuilder package includes the pre-save validation gate.
		string callResultJson = JsonSerializer.Serialize(callResult);
		// Primary, culture-stable: the clio-authored prefix that ONLY the gate emits (ProcessSchemaValidator) — proves
		// the gate fired regardless of the stand's profile culture (the platform's own message below is localizable).
		callResultJson.Should().Contain("Process validation failed",
			because: "the pre-save gate rejected the schema (clio-authored, culture-independent marker)");
		// Secondary: the specific platform rule. Platform-localized text, so this holds on an English-culture sandbox.
		callResultJson.Should().Contain("circular dependency",
			because: "a process parameter mapped to itself forms a circular dependency the platform rejects on save (a case the per-mapping type check does not detect)");
		// The rejected build must leave NO orphaned schema — describe reports the clio-owned 'was not found' message.
		string describeJson = JsonSerializer.Serialize(await DescribeAsync(context, processName));
		describeJson.Should().Contain("was not found",
			because: "a rejected build is rolled back, leaving no orphaned schema on the stand");
	}

	[Test]
	[Description("Over the real MCP path, create-business-process maps one element's OUTPUT into ANOTHER element's INPUT (element->element, ENG-92127 AC#1): performTask's Guid output ActivityResult into CheckCanExecuteOperationUserTask's Guid input UserId; describe reads the target element's input back bound to the source element's output.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process maps one element's output into another element's input")]
	public async Task CreateBusinessProcess_Should_MapElementOutputIntoAnotherElementInput() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpElemMapE2e{Guid.NewGuid():N}";
		string descriptor = BuildElementToElementDescriptor(processName);

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = descriptor
		});

		// Assert — the server accepts the element->element mapping (an invalid one would error) and reports the build
		callResult.IsError.Should().NotBeTrue(
			because: "mapping one element's output into another element's compatible input must build without a transport error");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain(processName,
			because: "a successful build reports the created schema name");

		// Readback: the target element's input parameter and the source element's output are both present in the
		// graph. The binding on the input is an element-qualified meta-path built from UIds (not names), so we assert
		// the surfaced names + a successful build rather than substring-matching the meta-path.
		string describeJson = JsonSerializer.Serialize(await DescribeAsync(context, processName));
		describeJson.Should().Contain(ElementTargetInput,
			because: "the target element's input parameter that receives the mapping is present in the read-back graph");
		describeJson.Should().Contain(ElementSourceOutput,
			because: "the source element's output parameter (the mapping source) is surfaced by describe because it is a result/output");
	}

	[Test]
	[Description("Over the real MCP path: a parameter built with a constant default value reads back with source ConstValue.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process persists a constant default value on a parameter")]
	public async Task CreateBusinessProcess_Should_PersistConstantDefault_OnParameter() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpDefaultE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptorWithDefault(processName)
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "building a process with a constant default must succeed");
		string describeJson = JsonSerializer.Serialize(await DescribeAsync(context, processName));
		describeJson.Should().Contain("ConstValue",
			because: "the parameter's constant default is persisted and reads back as a ConstValue source");
		describeJson.Should().Contain("Retries",
			because: "the defaulted parameter is present in the read-back graph");
	}

	[Test]
	[Description("Over the real MCP path: a parameter built with a caption and description reads both back via describe.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process persists caption and description on a parameter")]
	public async Task CreateBusinessProcess_Should_PersistCaptionAndDescription_OnParameter() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpDescE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptorWithDescription(processName)
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "building a process with a parameter caption + description must succeed");
		string describeJson = JsonSerializer.Serialize(await DescribeAsync(context, processName));
		describeJson.Should().Contain("Customer note",
			because: "the parameter caption is persisted and read back by describe-business-process");
		describeJson.Should().Contain("Free-text note about the customer",
			because: "the parameter description is persisted and read back by describe-business-process");
	}

	[Test]
	[Description("Over the real MCP path, create-business-process builds a signalStart with a data source filter, and describe-business-process reads the filter back (round-trip).")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process builds a filtered signal start and describe reads the filter back")]
	public async Task CreateBusinessProcess_Should_BuildSignalStartFilter_AndReadItBack() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpFilterE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildFilteredDescriptor(processName)
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a signalStart with a data source filter must build without a transport error");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain(processName,
			because: "a successful build reports the created schema name (run against an environment with the ProcessDesignService package and a writable Custom package)");

		// Readback: describe and confirm the signal-start filter round-tripped. The distinctive constant value proves
		// the filter both serialized on build AND decoded back on describe — not merely that a signalStart exists.
		string describeJson = JsonSerializer.Serialize(await DescribeAsync(context, processName));
		describeJson.Should().Contain("ClioFilterProbe",
			because: "describe-business-process decodes the signalStart EntityFilters back into the filter descriptor, so the distinctive filter value round-trips");
	}

	[Test]
	[Description("Over the real MCP path, create-business-process builds a signalStart filter using a relative-date macro (Today), an integer date-part (Year(CreatedOn) = 2026) and a Time-of-day date-part (HourMinute(CreatedOn) = 14:30), and describe-business-process reads the macro and both date-parts back (round-trip of the extended filter vocabulary).")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process builds a macro + date-part filter and describe reads them back")]
	public async Task CreateBusinessProcess_Should_BuildSignalStartFilterWithMacroAndDatePart_AndReadItBack() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpFilterVocabE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildMacroAndDatePartFilteredDescriptor(processName)
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a signalStart filter using a macro and a date-part must build without a transport error");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain(processName,
			because: "a successful build reports the created schema name (run against an environment with the ProcessDesignService package and a writable Custom package)");

		// Readback: describe and confirm BOTH extended-vocabulary fields round-tripped. A macro or date-part that
		// serialized on build but was dropped by either descriptor DTO on decode would be caught here — the same
		// both-sides drop that silently lost macros before clio's DescribedFilterCondition carried them.
		string describeJson = JsonSerializer.Serialize(await DescribeAsync(context, processName));
		describeJson.Should().Contain("Today",
			because: "the relative-date macro round-trips: describe decodes the right-hand Macros function back to macro=Today");
		describeJson.Should().Contain("datePart",
			because: "the left-hand date-part modifier round-trips: describe decodes the DatePart function and surfaces the datePart field");
		describeJson.Should().Contain("Year",
			because: "the date-part name round-trips on read-back (Year(CreatedOn) = 2026)");
		describeJson.Should().Contain("HourMinute",
			because: "the Time-valued date-part round-trips on read-back (time-of-day comparison HourMinute(CreatedOn) = 14:30)");
	}

	[Test]
	[Description("Over the real MCP path, create-business-process builds a signalStart restricted to a tracked-change column (on:modified, changedColumns:[Name]), and describe-business-process reads the tracked column back (round-trip).")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process builds a column-restricted signal start and describe reads the tracked column back")]
	public async Task CreateBusinessProcess_Should_BuildSignalStartTrackedColumns_AndReadThemBack() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpSignalColsE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildTrackedColumnsDescriptor(processName)
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a signalStart restricted to specific changed columns must build without a transport error");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain(processName,
			because: "a successful build reports the created schema name (run against an environment with the ProcessDesignService package and a writable Custom package)");

		// Readback: describe and confirm the tracked column round-tripped. Build resolves the column NAME to its column
		// UId; describe decodes that UId back to the name — a drop on either side (package or clio DTO) is caught here,
		// not merely that a signalStart exists. Asserted on the typed graph (not a substring of the serialized
		// envelope), so a wrong or extra column cannot slip through on an incidental token match.
		DescribeProcessResult graph = ParseDescribeGraph(await DescribeAsync(context, processName));
		DescribedSignal signal = graph.Elements.Single(element => element.Name == "SignalStart1").Signal;
		signal.Should().NotBeNull(because: "describe reports the signal start's record trigger");
		signal.On.Should().Be("modified", because: "the trigger type round-trips");
		signal.ChangedColumns.Should().BeEquivalentTo(new[] { "Name" },
			because: "exactly the requested tracked column round-trips: build resolves it to its column UId and describe decodes that UId back to the name");
	}

	// A signal-start process whose EntityFilters carry a distinctive constant value, so the describe read-back can
	// prove the filter round-tripped (build serialize -> describe decode) rather than just that a signalStart exists.
	// Contact.Name is a base column present on every stand.
	private static string BuildFilteredDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Filter E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "SignalStart1", "type": "signalStart", "signal": { "entity": "Contact", "on": "added" },
		      "filter": { "object": "Contact", "logicalOperation": "and",
		        "conditions": [ { "column": "Name", "comparison": "contains", "value": "ClioFilterProbe" } ] } },
		    { "name": "task1", "type": "performTask" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "SignalStart1", "target": "task1" },
		    { "source": "task1", "target": "EndEvent1" }
		  ]
		}
		""";

	// A signal-start filter exercising the extended vocabulary over the real MCP path: a relative-date MACRO
	// (CreatedOn = Today, a right-hand Macros function), an integer DATE-PART (Year(CreatedOn) = 2026) and a
	// Time-of-day DATE-PART (HourMinute(CreatedOn) = 14:30, compared against a Time value). CreatedOn is a base
	// DateTime column on every entity, so this runs on any stand. All conditions must survive the
	// build-serialize -> describe-decode round-trip on BOTH the package and clio descriptor DTOs.
	private static string BuildMacroAndDatePartFilteredDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Filter Vocab E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "SignalStart1", "type": "signalStart", "signal": { "entity": "Contact", "on": "modified" },
		      "filter": { "object": "Contact", "logicalOperation": "and",
		        "conditions": [
		          { "column": "CreatedOn", "comparison": "equal", "macro": "Today" },
		          { "column": "CreatedOn", "comparison": "equal", "datePart": "Year", "value": "2026" },
		          { "column": "CreatedOn", "comparison": "equal", "datePart": "HourMinute", "value": "14:30" }
		        ] } },
		    { "name": "task1", "type": "performTask" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "SignalStart1", "target": "task1" },
		    { "source": "task1", "target": "EndEvent1" }
		  ]
		}
		""";

	// ENG-91853's whole build-path contract in one descriptor, over the real MCP path: an exclusiveGateway
	// ELEMENT (a type token the package refused before 1.4.0.58), flows[].kind on all three kinds, and a
	// flows[].condition written as a parameter NAME — which is the only way a caller can write one here,
	// because the parameter's UId is generated by this same call. Nothing else in this suite sends any of
	// these fields, so a binder that silently dropped kind or condition would go unnoticed everywhere.
	//
	// It now also carries flows[].label on all three kinds, for the same reason and with a sharper
	// version of it: a dropped label is the failure the whole field exists to prevent (two identical
	// unlabelled arrows), it is invisible in metadata because a caption lives in the schema RESOURCES,
	// and the unit tests construct these records positionally in C# so the JSON member name is
	// exercised nowhere else.
	private static string BuildGatewayAndDeclaredBranchDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Gateway Branch E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "Start1", "type": "startEvent" },
		    { "name": "Decide", "type": "exclusiveGateway", "caption": "Check the amount" },
		    { "name": "EndApprove", "type": "endEvent", "caption": "Approval path" },
		    { "name": "EndFast", "type": "endEvent", "caption": "Fast-track path" }
		  ],
		  "flows": [
		    { "source": "Start1", "target": "Decide", "label": "Amount known" },
		    { "source": "Decide", "target": "EndApprove", "kind": "conditional",
		      "condition": "[#AmountParameter#] > 100", "label": "Above the threshold" },
		    { "source": "Decide", "target": "EndFast", "kind": "default", "label": "Everything else" }
		  ],
		  "parameters": [
		    { "name": "AmountParameter", "type": "Integer", "direction": "In", "caption": "Amount" }
		  ]
		}
		""";

	[Test]
	[Description("Over the real MCP path, create-business-process builds an exclusiveGateway ELEMENT with a declared conditional branch and a declared default branch, and the condition — written as a parameter NAME — comes back EXPANDED into its UId meta-path. This is the only e2e that sends flows[].kind, flows[].condition or a gateway type token at all; the unit tests construct those records positionally in C#, so the JSON binder is never exercised by them and a renamed or mistyped property would drop the value with the whole suite green.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process builds a gateway with declared branches and expands a named condition")]
	public async Task CreateBusinessProcess_Should_BuildDeclaredBranches_AndExpandTheNamedCondition() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpGatewayE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildGatewayAndDeclaredBranchDescriptor(processName)
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a gateway element with declared branches must build without a transport error");
		JsonSerializer.Serialize(callResult).Should().Contain(processName,
			because: "a successful build reports the created schema name");

		// Readback on the typed graph, because every one of these can fail silently: a dropped `kind` leaves a
		// plain flow that describe still reports, and an unexpanded condition is stored verbatim and only fails
		// later, at run time, on a graph that looked correct at every earlier step.
		DescribeProcessResult graph = ParseDescribeGraph(await DescribeAsync(context, processName));
		DescribedFlow conditional = graph.Flows.Single(f => f.Source == "Decide" && f.Target == "EndApprove");
		DescribedFlow fallback = graph.Flows.Single(f => f.Source == "Decide" && f.Target == "EndFast");

		conditional.Kind.Should().Be("conditional",
			because: "flows[].kind has to survive the JSON binder, the build and the save - dropped, this reads "
				+ "back as 'sequence' and the branch silently never branches");
		fallback.Kind.Should().Be("default",
			because: "the default marker is a separate field from the condition and is dropped independently");
		conditional.Condition.Should().Contain("[Parameter:",
			because: "the NAME the caller wrote must be expanded into the UId meta-path the runtime evaluates - "
				+ "this is the only route by which a build-path condition can reference a parameter at all");
		conditional.Condition.Should().NotContain("AmountParameter",
			because: "a condition still carrying the bare name was never expanded, and the platform would refuse "
				+ "it at run time on a process that built and described cleanly");
		conditional.Condition.Should().EndWith("> 100",
			because: "only the token is rewritten - the operator and the literal around it must survive verbatim");

		// The labels, on the same read-back. A dropped one is silent in the worst way: the process builds,
		// describes and RUNS correctly, and the only symptom is a diagram a human cannot read.
		conditional.Label.Should().Be("Above the threshold",
			because: "flows[].label has to survive the JSON binder, the build, the resource extraction and the "
				+ "read-back - and it is stored in the schema RESOURCES, not in metadata, so no metadata check "
				+ "anywhere else in this suite would notice it missing");
		fallback.Label.Should().Be("Everything else",
			because: "a default branch carries a label as readily as a conditional one, and this is the pair "
				+ "whose two arrows are otherwise indistinguishable");
		graph.Flows.Single(f => f.Source == "Start1" && f.Target == "Decide").Label.Should()
			.Be("Amount known",
				because: "a plain sequence flow takes a label through the same field - it is unusual in the "
					+ "shipped corpus, not unsupported");
	}

	[Test]
	[Description("Over the real MCP path, create-business-process builds a signalStart with a DELETE trigger (on:deleted) and describe-business-process reads the trigger back as 'deleted' (round-trip of the third record-event type).")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process builds a delete-trigger signal start and describe reads it back")]
	public async Task CreateBusinessProcess_Should_BuildSignalStartDeleteTrigger_AndReadItBack() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpSignalDelE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDeleteTriggerDescriptor(processName)
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a signalStart with a record-deleted trigger must build without a transport error");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain(processName,
			because: "a successful build reports the created schema name (run against an environment with the ProcessDesignService package and a writable Custom package)");

		// Readback: the delete trigger must survive save->reload and decode back to the canonical token. A change type
		// dropped or coerced to the 'modified' default on either side is caught here.
		// Asserted on the typed graph so the trigger token is checked exactly, not matched incidentally anywhere in the
		// serialized envelope.
		DescribeProcessResult graph = ParseDescribeGraph(await DescribeAsync(context, processName));
		DescribedSignal signal = graph.Elements.Single(element => element.Name == "SignalStart1").Signal;
		signal.Should().NotBeNull(because: "describe reports the signal start's record trigger");
		signal.On.Should().Be("deleted",
			because: "the record-deleted trigger round-trips: it persists on save, is decoded back to the 'deleted' token, and is not coerced to the default 'modified'");
		signal.ChangedColumns.Should().BeNull(because: "a delete trigger carries no tracked columns");
	}

	[Test]
	[Description("Over the real MCP path, create-business-process honours the element-level useBackgroundMode override — false on a signalStart (whose kind default is true) and true on a plain userTask — and describe-business-process reports the flag per element.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process applies useBackgroundMode per element and describe reads it back")]
	public async Task CreateBusinessProcess_Should_ApplyElementBackgroundMode_AndReadItBack() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpBgModeE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildBackgroundModeDescriptor(processName)
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an element-level useBackgroundMode override must build without a transport error");
		JsonSerializer.Serialize(callResult).Should().Contain(processName,
			because: "a successful build reports the created schema name");

		// Readback: the flag must persist per element — the signalStart override to false (against its own default of
		// true) is the meaningful assertion; a dropped override would read back as true.
		DescribeProcessResult graph = ParseDescribeGraph(await DescribeAsync(context, processName));
		graph.Elements.Single(element => element.Name == "SignalStart1").UseBackgroundMode.Should().BeFalse(
			because: "an explicit useBackgroundMode:false must override the signal start's own background-mode default and survive the save");
		graph.Elements.Single(element => element.Name == "task1").UseBackgroundMode.Should().BeTrue(
			because: "the flag is element-level: an explicit true on a plain user task must persist too");
	}

	[Test]
	[Description("Over the real MCP path, create-business-process builds a sendEmail element with a custom-message HTML body, and describe-business-process reads the body back (round-trip): the element resolves to the sendEmail build type and its Body parameter carries the distinctive HTML verbatim.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process builds a sendEmail HTML body and describe reads it back")]
	public async Task CreateBusinessProcess_Should_BuildSendEmailHtmlBody_AndReadItBack() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpSendEmailE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildSendEmailDescriptor(processName)
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a sendEmail element with a custom-message HTML body must build without a transport error");
		string callResultJson = JsonSerializer.Serialize(callResult);
		// The success LINE, not merely the name: the command logs "Building process '<name>'..." before it calls the
		// server, so a name match alone also passes when the build then FAILS - which lets a rejected descriptor reach
		// the describe parser and surface as an unrelated parse error instead of the real server message.
		callResultJson.Should().Contain("created (UId:",
			because: "only a genuinely successful build logs the created-schema line (run against an environment with the ProcessDesignService package that supports the sendEmail element)");

		// Readback proves the FULL macro round-trip on a real server: the author wrote [[param:ClioProbeParam]] in the
		// body, the server RESOLVED it into a platform <img data-value="[#…#]"> token on build, and describe DECODES it
		// back into the same [[param:…]] author form on read. describe echoes the decoded HTML on the email block's own
		// `body` field (not just the hasBody flag, and not only via the value-bearing Body parameter) — so this asserts
		// the [Description]/IProcessDescriber contract that `body` round-trips, which no unit test can verify end to end.
		DescribeProcessResult graph = ParseDescribeGraph(await DescribeAsync(context, processName));
		DescribedElement sendEmail = graph.Elements.Single(element => element.Name == "SendEmail1");
		sendEmail.BuildType.Should().Be("sendemail",
			because: "a Send email element round-trips to the dedicated sendEmail build token, not the generic userTask");
		sendEmail.Email.Should().NotBeNull(
			because: "describe surfaces a Send email element's configuration in its own email block");
		sendEmail.Email.HasBody.Should().BeTrue(
			because: "hasBody is the email block's lightweight presence flag for a custom-message body");
		sendEmail.Email.Body.Should().NotBeNullOrWhiteSpace(
			because: "describe now echoes the decoded body HTML on the email block, not only the hasBody flag");
		sendEmail.Email.Body.Should().Contain("ClioSendEmailProbe",
			because: "the custom-message HTML body round-trips through build and describe");
		sendEmail.Email.Body.Should().Contain("[[param:ClioProbeParam]]",
			because: "the body macro was resolved into a platform token on build and DECODED back into its "
				+ "[[param:…]] author form on describe — the full encode/decode round-trip on a real server");
		sendEmail.Email.Body.Should().NotContain("data-value",
			because: "a resolved body is decoded back to author form, so the raw <img data-value=\"[#…#]\"> token "
				+ "must NOT leak into the read-back body");
	}

	[Test]
	[Description("Over the real MCP path, a descriptor built to the FULL documented email contract - mode, subject, body/bodyFormat, all three recipient value sources, importance, ignoreErrors and a manual-mode performer - is accepted and every field reads back, so drift between the tool's [Description] prose and what the server actually accepts cannot ship undetected.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process accepts the full documented email contract and describe reads every field back")]
	public async Task CreateBusinessProcess_Should_AcceptTheFullEmailContract_AndReadEveryFieldBack() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpEmailContractE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildFullEmailContractDescriptor(processName)
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "every field the tool's email contract advertises must be accepted by the server it targets");
		// Asserting the success LINE, not just the process name: the command logs "Building process '<name>'..."
		// before it calls the server, so a name match alone passes even when the build then fails - which sent a
		// rejected descriptor into the describe parser and surfaced as an unrelated "no matching element" error.
		JsonSerializer.Serialize(callResult).Should().Contain("created (UId:",
			because: "only a genuinely successful build logs the created-schema line, so this is what proves the "
				+ "whole email contract was accepted rather than merely transported");

		DescribeProcessResult graph = ParseDescribeGraph(await DescribeAsync(context, processName));
		DescribedElement element = graph.Elements.Single(e => e.Name == "SendEmail1");
		DescribedEmail email = element.Email!;
		email.Should().NotBeNull(because: "the element must round-trip as a configured Send email element");
		email.Mode.Should().Be("manual", because: "the send mode is part of the advertised contract");
		email.Subject.Should().Be("Clio full-contract probe",
			because: "a plain constant subject must survive verbatim");
		email.HasBody.Should().BeTrue(because: "bodyFormat html + body must register as a custom message");
		email.Importance.Should().Be("high", because: "the importance token must round-trip, not be normalised away");
		email.IgnoreErrors.Should().BeTrue(because: "the ignore-errors flag is part of the contract");
		email.Performer.Should().NotBeNull(
			because: "a manual-mode performer is advertised and must be readable back");
		email.Performer!.Type.Should().Be("user", because: "the performer assignment type must survive");

		// All three recipient value sources, on the three separate address lines they were sent on. This is the part
		// the prose could silently drift on: the value-source triple is documented in a [Description] string with no
		// type-checked DTO behind it, so only a round trip proves the shapes the server accepts still match.
		email.To.Should().NotBeNull().And.HaveCount(2,
			because: "the To line carried a constant address AND a process-parameter recipient");
		email.To!.Select(r => r.Source).Should().Contain("ConstValue",
			because: "the constant address is stored inline on the element");
		email.Cc.Should().NotBeNull().And.HaveCount(1, because: "the Cc line carried one constant address");
		email.Bcc.Should().NotBeNull().And.HaveCount(1, because: "the Bcc line carried one formula recipient");
		email.Bcc!.Single().Source.Should().Be("Script",
			because: "an 'expression' recipient is stored as a formula, which is what makes it resolve at send time");
	}

	[Test]
	[Description("Closes the accessRights guard's load-bearing assumption end to end. AccessRightsBlockExpectation decides whether to warn by looking for the block on DescribedElement.AdditionalData, and every unit test around it CONSTRUCTS that dictionary by hand - so if the real server never surfaces the block there, the block-presence check returns false on a SUCCESSFUL write and clio tells the caller the permissions were not changed when they were. Requires a sandbox whose deployed CrtProcessBuilder understands the element: one that predates it rejects the changeAccessRights element TYPE outright, before any block-level check, so that environment is Ignored rather than failed.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process: the accessRights drop warning agrees with what actually landed")]
	public async Task CreateBusinessProcess_Should_KeepTheAccessRightsDropWarning_ConsistentWithWhatLanded() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpAccessRightsE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildAccessRightsDescriptor(processName)
		});

		// Gate BEFORE asserting: a CrtProcessBuilder that predates the element rejects the element TYPE, which is
		// a different failure from the block-level discard this test is about. Such a sandbox cannot exercise
		// either direction, so Ignore it instead of reporting a red that means "environment too old".
		//
		// Matched on the package's OWN refusal text (ProcessElementFactory: "Element type '<type>' is not
		// supported yet"), not on "the create failed somehow". A bare IsError gate swallows every other create
		// failure too - a malformed descriptor, an auth failure, a genuine regression in this very block - and
		// reports each of them as "environment too old", so the test could never go red for the thing it exists
		// to catch.
		string payload = JsonSerializer.Serialize(callResult);
		bool elementTypeRejected = callResult.IsError is true
			&& payload.Contains("is not supported yet")
			&& payload.Contains("changeAccessRights");
		if (elementTypeRejected) {
			Assert.Ignore(
				"The sandbox's deployed CrtProcessBuilder does not accept a 'changeAccessRights' element type, so "
				+ "the accessRights block never reaches the read-back guard. This is expected until the "
				+ "bundled archive is rebuilt from a source tree that CONTAINS the element - note the floors are "
				+ "already at 1.4.0.40 and the bundled archive already reports that version, so the version "
				+ "precondition passes while the block is still discarded. Until then this test is Ignored, "
				+ "NOT passing, and the create path is unverified end to end.");
		}

		callResult.IsError.Should().NotBeTrue(
			because: "once the element type is accepted, the build must succeed whether or not the deployed "
				+ "package understands the BLOCK - a server that cannot deserialize it discards it and still "
				+ "answers success, which is the whole reason the read-back guard exists");
		payload.Should().Contain("created (UId:",
			because: "only a genuinely successful build logs the created-schema line");

		DescribeProcessResult graph = ParseDescribeGraph(await DescribeAsync(context, processName));
		DescribedElement element = graph.Elements.Single(e => e.Name == "GrantRights1");

		// The exact predicate AccessRightsBlockExpectation applies internally, run against a REAL read-back
		// instead of a hand-built dictionary.
		bool blockLanded = element.AdditionalData is not null
			&& element.AdditionalData.Any(entry =>
				string.Equals(entry.Key, "accessRights", StringComparison.OrdinalIgnoreCase)
				&& entry.Value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null));
		bool warnedItWasDiscarded = payload.Contains("does not implement IExtensibleDataObject");

		warnedItWasDiscarded.Should().Be(!blockLanded,
			because: "the warning must track reality in BOTH directions. Warning when the block DID land tells a "
				+ "caller to treat a real permission change as not applied; staying silent when it did NOT land "
				+ "is the silent discard the guard was written to catch. Either way round the caller acts on a "
				+ "false belief about who can reach the records.");

		if (blockLanded) {
			// Read from AdditionalData deliberately: DescribedElement has no typed AccessRights member, so the
			// extension bag is not a convenience here — it is the ONLY channel the block travels on, which is
			// what makes the guard's dependency on that bag structural rather than incidental.
			JsonElement block = element.AdditionalData!.First(entry =>
				string.Equals(entry.Key, "accessRights", StringComparison.OrdinalIgnoreCase)).Value;
			block.GetProperty("object").GetString().Should().Be("Contact",
				because: "the target object must round-trip through build and describe");
			block.GetProperty("add").GetArrayLength().Should().Be(1,
				because: "the single add entry that was sent must read back, so a landed block means a landed "
					+ "CONFIGURATION and not merely a present key");
		}
	}

	// A minimal but COMPLETE changeAccessRights element: an object that uses record permissions, one add entry with
	// an explicit level and a role grantee, and a record filter targeting a single record through a process
	// parameter. The filter matters even though this process is never run — without one the element is a documented
	// silent no-op, and clio emits a second, different warning that would muddy the assertion this test makes.
	private static string BuildAccessRightsDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Access Rights E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "GrantRights1", "type": "changeAccessRights", "caption": "Grant read",
		      "accessRights": {
		        "object": "Contact",
		        "add": [
		          { "operations": [ "read" ], "level": "permit",
		            "grantee": { "type": "role", "role": "All employees" } }
		        ]
		      },
		      "filter": {
		        "object": "Contact",
		        "conditions": [
		          { "column": "Id", "comparison": "equal", "processParameter": "ContactIdParameter" }
		        ]
		      } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "GrantRights1" },
		    { "source": "GrantRights1", "target": "EndEvent1" }
		  ],
		  "parameters": [
		    { "name": "ContactIdParameter", "type": "Guid", "direction": "In", "caption": "Contact Id" }
		  ]
		}
		""";

	// Exercises EVERY field the create-business-process email contract advertises, in one element, so the write path
	// has executable verification rather than only prose. `sender` is deliberately omitted: it needs a mailbox record
	// (or an address configured on that specific environment), which would make this test depend on stand data rather
	// than on the contract. The recipient triple is the point - a constant, a process parameter and a raw formula.
	private static string BuildFullEmailContractDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Email Contract E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "SendEmail1", "type": "sendEmail", "caption": "Send email",
		      "email": {
		        "mode": "manual",
		        "subject": "Clio full-contract probe",
		        "body": "<html><body><p>ClioEmailContractProbe</p></body></html>",
		        "bodyFormat": "html",
		        "to": [ { "value": "to-const@example.com" }, { "processParameter": "RecipientAddress" } ],
		        "cc": [ { "value": "cc-const@example.com" } ],
		        "bcc": [ { "expression": "[#SysVariable.CurrentUserContact#]", "referenceSchema": "Contact" } ],
		        "importance": "high",
		        "ignoreErrors": true,
		        "performer": { "type": "user", "showPage": true }
		      } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "SendEmail1" },
		    { "source": "SendEmail1", "target": "EndEvent1" }
		  ],
		  "parameters": [
		    { "name": "RecipientAddress", "type": "Text", "direction": "In", "caption": "Recipient address" }
		  ]
		}
		""";

	[Test]
	[Description("Over the real MCP path, create-business-process builds a sendEmail element in TEMPLATE mode (ENG-95986) - an object-bound stock template with its macro source bound to a Lookup process parameter - and describe-business-process reads the template id, its display name, the macro-source object and the templateEntity binding back, with no body reported. Depends on the stock template 'Case feedback request notification' (authored against Case) being present on the environment.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process builds a template-mode sendEmail and describe reads the template back")]
	public async Task CreateBusinessProcess_Should_BuildTemplateModeSendEmail_AndReadTemplateBack() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpEmailTplE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildTemplateModeEmailDescriptor(processName)
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a template-mode sendEmail element must build without a transport error");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain("created (UId:",
			because: "only a genuinely successful build logs the created-schema line (run against an environment whose CrtProcessBuilder supports template mode and which carries the stock 'Case feedback request notification' template)");
		callResultJson.Should().NotContain(EmailBlockExpectation.TemplateWarningMarker,
			because: "the template-landed check must stay silent when the deployed package stored the template");

		DescribeProcessResult graph = ParseDescribeGraph(await DescribeAsync(context, processName));
		DescribedElement sendEmail = graph.Elements.Single(element => element.Name == "AskFeedback");
		sendEmail.Email.Should().NotBeNull(because: "describe surfaces the element's configuration in its email block");
		sendEmail.Email.MessageSource.Should().Be("template",
			because: "BodyTemplateType '0' decodes to the template message source");
		sendEmail.Email.Template.Should().NotBeNullOrWhiteSpace(
			because: "the resolved template's record id is stored on EmailTemplateId and reads back for re-application");
		sendEmail.Email.TemplateDisplay.Should().Be("Case feedback request notification",
			because: "the template NAME is stored as the lookup's display value, the shape the designer shows");
		sendEmail.Email.TemplateObject.Should().Be("Case",
			because: "the macro-source parameter's reference is set to the template's object, and describe names it");
		sendEmail.Email.TemplateEntity.Should().NotBeNull(
			because: "the templateEntity binding to the CaseId process parameter must read back");
		sendEmail.Email.TemplateEntity.ReferenceSchema.Should().Be("Case",
			because: "the macro-source parameter is typed as a Lookup to the template's object");
		sendEmail.Email.HasBody.Should().BeFalse(
			because: "a template element reports no body, so the block re-applies without a template-beside-body refusal");
	}

	[Test]
	[Description("Over the real MCP path, a sendEmail element carrying BOTH a template and a body is refused BY THE SERVER, naming the element and the two fields, and no process is created (ENG-95986: template and body/bodyFormat are mutually exclusive). Without this the create contract's refusal text would rest on a manual stand run alone.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process refuses a template beside a body and creates nothing")]
	public async Task CreateBusinessProcess_Should_RefuseATemplateBesideABody_AndCreateNothing() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpEmailTplBodyE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildRefusedTemplateEmailDescriptor(processName,
				"\"template\": \"Case closure notification\", \"body\": \"<p>ClioTemplateBesideBodyProbe</p>\"")
		});

		// Assert
		// NOT on callResult.IsError: measured null on a server refusal, exactly as on success (see the bad-formula
		// test above). What the description promises is the diagnostic and that nothing was CREATED.
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain("mutually exclusive",
			because: "the refusal must say WHY - one element, one message - so the caller drops one of the two rather than guessing");
		callResultJson.Should().Contain("AskFeedback",
			because: "the refusal must name the element, or the caller cannot find it in a multi-element descriptor");
		JsonSerializer.Serialize(await DescribeAsync(context, processName)).Should().Contain("was not found",
			because: "a refused descriptor must leave no process behind - asserted positively on the not-found answer, "
				+ "because the not-found message quotes the name it looked for");
	}

	[Test]
	[Description("Over the real MCP path, a sendEmail element naming a template that does not exist is refused BY THE SERVER at build time, the refusal names the template, and no process is created (ENG-95986 AC: an unresolved reference fails at build with a message naming the template). A template that slipped through would fail only at run time with 'Localizable template not found', which the element's ignore-errors default then swallows.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process refuses an unknown template, naming it, and creates nothing")]
	public async Task CreateBusinessProcess_Should_RefuseAnUnknownTemplate_NamingIt_AndCreateNothing() {
		// Arrange - a name no environment carries
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpEmailTplUnknownE2e{Guid.NewGuid():N}";
		string templateName = $"ClioNoSuchTemplate{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildRefusedTemplateEmailDescriptor(processName, $"\"template\": \"{templateName}\"")
		});

		// Assert
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain(templateName,
			because: "the refusal must name the template the caller passed, so a typo is visible in the message");
		callResultJson.Should().Contain("no email template is named",
			because: "the refusal must say the name resolved to NOTHING of the email type, distinguishing it from an ambiguous name");
		JsonSerializer.Serialize(await DescribeAsync(context, processName)).Should().Contain("was not found",
			because: "an unresolved template must fail the BUILD, leaving no process behind for a run-time failure to hide in");
	}

	// A sendEmail element whose email block is supplied by the caller, for the refusal tests: the surrounding process
	// is valid, so the only thing the server can object to is the email block itself.
	private static string BuildRefusedTemplateEmailDescriptor(string processName, string emailFields) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Email Template Refusal E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "AskFeedback", "type": "sendEmail", "caption": "Ask for feedback",
		      "email": { {{emailFields}}, "mode": "manual",
		        "to": [ { "value": "probe@example.com" } ] } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "AskFeedback" },
		    { "source": "AskFeedback", "target": "EndEvent1" }
		  ]
		}
		""";

	// A sendEmail element in TEMPLATE mode against an object-bound stock template, with the macro source bound to a
	// Lookup process parameter of that object. `sender` is omitted for the same reason as in the full-contract
	// descriptor; the template NAME is stock content shipped by the case packages, not stand data.
	private static string BuildTemplateModeEmailDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Email Template E2E",
		  "packageName": "Custom",
		  "parameters": [
		    { "name": "CaseId", "type": "Lookup", "referenceSchema": "Case", "direction": "In", "caption": "Case" }
		  ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "AskFeedback", "type": "sendEmail", "caption": "Ask for feedback",
		      "email": {
		        "messageSource": "template",
		        "template": "Case feedback request notification",
		        "templateEntity": { "processParameter": "CaseId" },
		        "mode": "auto",
		        "to": [ { "expression": "[#SysVariable.CurrentUserContact#]", "referenceSchema": "Contact" } ]
		      } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "AskFeedback" },
		    { "source": "AskFeedback", "target": "EndEvent1" }
		  ]
		}
		""";

	// A sendEmail element carrying a custom-message HTML body with a distinctive probe token, so the describe read-back
	// proves the body round-tripped (build stores it as a ConstValue on the Body parameter, describe decodes it) rather
	// than just that a sendEmail element exists. StartEvent1 -> SendEmail1 -> EndEvent1 is a minimal valid graph.

	[Test]
	[Description("Over the real MCP path, create-business-process builds an openEditPage element in ADD mode with a pre-filled column value, and describe-business-process reads the whole block back. This is the ONLY place the page-candidate query is exercised end to end: the unit tests substitute that reader, so the SysModule/SysModuleEntity/SysModuleEdit join, the SysSchema name resolution and the derivation of the object from the page are proven here and nowhere else.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process builds an openEditPage add-mode element and describe reads it back")]
	public async Task CreateBusinessProcess_Should_BuildOpenEditPageAddMode_AndReadItBack() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpOpenEditPageAddE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildOpenEditPageAddDescriptor(processName)
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an openEditPage element with a page and pre-filled values must build without a transport error");
		// The success LINE, not merely the name: the command logs "Building process '<name>'..." BEFORE it calls the
		// server, so a name match alone also passes when the build then fails - and a rejected descriptor would then
		// surface as an unrelated describe parse error instead of the real server message.
		JsonSerializer.Serialize(callResult).Should().Contain("created (UId:",
			because: "only a genuinely successful build logs the created-schema line (run against an environment whose "
				+ "ProcessDesignService package supports the openEditPage element)");

		DescribeProcessResult graph = ParseDescribeGraph(await DescribeAsync(context, processName));
		DescribedElement element = graph.Elements.Single(node => node.Name == "OpenPage1");
		element.BuildType.Should().Be("openeditpage",
			because: "an Open edit page element round-trips to the dedicated openEditPage build token, not the generic "
				+ "userTask - which is what the handler registration order guarantees");
		element.OpenEditPage.Should().NotBeNull(
			because: "describe surfaces the element's configuration in its own openEditPage block");
		element.OpenEditPage!.Page.Should().Be(OpenEditPageName,
			because: "the stored page UId resolves back to the name the descriptor asked for - the SysSchema read");
		element.OpenEditPage.Object.Should().Be(OpenEditPageObject,
			because: "the object is DERIVED from the page by the candidate query, never supplied by the caller, so "
				+ "reading it back is what proves that query ran against the real tables");
		element.OpenEditPage.EditMode.Should().Be("add",
			because: "the stored RecordEditMode maps back to the token the caller used");
		element.OpenEditPage.PageTypeUId.Should().BeNull(
			because: "the target object is untyped, so no record type is stored - the same result all three designer "
				+ "captures show");
		element.OpenEditPage.CompletionMode.Should().Be("onSave",
			because: "the completion mode is written explicitly at create, so describe can report it rather than "
				+ "answering 'no mode' for an element that completes on save");
		element.OpenEditPage.Recommendation.Should().Be("Fill in the account details",
			because: "the recommendation is stored as a single-line constant and round-trips");
		element.OpenEditPage.DefaultValues.Should().NotBeNullOrEmpty(
			because: "the pre-filled column value must read back - it is stored through the platform's own packer, so "
				+ "an empty read here would mean the designer card cannot show it either");
		JsonSerializer.Serialize(element.OpenEditPage.DefaultValues).Should().Contain(OpenEditPageTextColumn,
			because: "the read-back names the column that was pre-filled");
	}

	[Test]
	[Description("Over the real MCP path, create-business-process builds an openEditPage element in EDIT mode whose record comes from a signalStart element's RecordId output, and describe reads the block back. This pairs the element with the trigger it is used with in practice, and exercises the element-output value source rather than a fixed record id.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process builds an openEditPage edit-mode element from a trigger record")]
	public async Task CreateBusinessProcess_Should_BuildOpenEditPageEditMode_FromTriggerRecord() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpOpenEditPageEditE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildOpenEditPageEditDescriptor(processName)
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an edit-mode element taking its record from an earlier element's output must build cleanly");
		JsonSerializer.Serialize(callResult).Should().Contain("created (UId:",
			because: "only a successful build logs the created-schema line");

		DescribeProcessResult graph = ParseDescribeGraph(await DescribeAsync(context, processName));
		DescribedElement element = graph.Elements.Single(node => node.Name == "OpenPage1");
		element.OpenEditPage.Should().NotBeNull(because: "the element is configured, so it reports its block");
		element.OpenEditPage!.EditMode.Should().Be("edit",
			because: "edit mode is what makes the element open an EXISTING record");
		element.OpenEditPage.RecordId.Should().NotBeNull(
			because: "edit mode requires a record, so the read-back must report which one it opens");
		element.OpenEditPage.DefaultValues.Should().BeNullOrEmpty(
			because: "pre-filled values belong to add mode only and the write path refuses them here, so an edit-mode "
				+ "element must come back without any");
	}

	[Test]
	[Description("Over the real MCP path, create-business-process turns on an openEditPage element's results-by-column list against a real LOOKUP column, and describe resolves the stored UId back to the column name. Only a stand proves this end: the column is stored as a UId resolved through the platform's own metadata, so a wrong encoding would read back as 'no column' rather than failing the build.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process configures an openEditPage results-by-column list")]
	public async Task CreateBusinessProcess_Should_ConfigureOpenEditPageResultsByColumn() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpOpenEditPageResultsE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildOpenEditPageResultsByColumnDescriptor(processName)
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "Owner is a lookup column on Account on every environment, so the list must configure cleanly");
		JsonSerializer.Serialize(callResult).Should().Contain("created (UId:",
			because: "IsError stays null on a refusal, so without the success line a refused build surfaces as an "
				+ "unrelated failure inside the describe step instead of as the server's refusal");
		DescribeProcessResult graph = ParseDescribeGraph(await DescribeAsync(context, processName));
		DescribedOpenEditPageResultsByColumn results = graph.Elements
			.Single(node => node.Name == "OpenPage1").OpenEditPage!.ResultsByColumn;
		results.Should().NotBeNull(because: "describe is how a caller sees which results a step offers");
		results!.Enabled.Should().BeTrue(because: "naming a column turns the list on");
		results.Column.Should().Be(OpenEditPageResultColumn,
			because: "the stored UId resolved back to the column name, which is what proves the write used the "
				+ "platform's own column identifier rather than the name");
		results.ColumnUId.Should().NotBeNullOrWhiteSpace(
			because: "an empty UId here would mean the column never landed, however successful the build looked");
	}

	[Test]
	[Description("Over the real MCP path, create-business-process configures an openEditPage element's Log activity block and describe reads every scheduling PAIR back with its unit. Only a real server proves the pairing: each interval is two independent platform parameters, and a stand is the only place that shows both members actually landed - a number stored without its period would read back with a null unit here rather than failing anywhere.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process configures an openEditPage element's Log activity block")]
	public async Task CreateBusinessProcess_Should_ConfigureOpenEditPageLogActivity() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpOpenEditPageActivityE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildOpenEditPageLogActivityDescriptor(processName)
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "a logged activity with paired intervals must build cleanly");
		JsonSerializer.Serialize(callResult).Should().Contain("created (UId:",
			because: "only a successful build logs the created-schema line");

		DescribeProcessResult graph = ParseDescribeGraph(await DescribeAsync(context, processName));
		DescribedOpenEditPageLogActivity activity = graph.Elements
			.Single(node => node.Name == "OpenPage1").OpenEditPage!.LogActivity;
		activity.Should().NotBeNull(
			because: "the element stores the block, and describe is how a caller confirms an activity is logged at all");
		activity!.Enabled.Should().BeTrue(
			because: "supplying the block turns the gate on - without it the platform creates no activity and every "
				+ "scheduling field below is inert");
		activity.Duration!.Value.Should().Be(20, because: "the amount reached the server as written");
		activity.Duration.Unit.Should().Be("minutes",
			because: "the PERIOD member landed too and decoded back - a unit of null here would mean the number was "
				+ "stored alone, measured in whatever the schema default happened to be");
		activity.RemindIn!.Unit.Should().Be("hours", because: "each interval carries its own unit independently");
		activity.ShowInCalendar.Should().BeTrue(because: "the calendar flag persists as written");
	}

	[Test]
	[Description("Over the real MCP path, create-business-process assigns an openEditPage element to a ROLE named by name, and describe reads the assignment back. Only a real environment can prove this end of it: the role name is resolved against SysAdminUnit, and the assignment lives on a RoleId parameter the user-task schema does not declare - the element has to grow it, exactly as the designer does, or the designer card shows no performer at all.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process assigns an openEditPage element to a role by name")]
	public async Task CreateBusinessProcess_Should_AssignOpenEditPagePerformer_ByRoleName() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpOpenEditPagePerformerE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildOpenEditPagePerformerDescriptor(processName)
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a role named by name must resolve on a real environment - All employees is a platform role "
				+ "present on every stand");
		JsonSerializer.Serialize(callResult).Should().Contain("created (UId:",
			because: "only a successful build logs the created-schema line");

		DescribeProcessResult graph = ParseDescribeGraph(await DescribeAsync(context, processName));
		DescribedPerformer performer = graph.Elements
			.Single(node => node.Name == "OpenPage1").OpenEditPage!.Performer;
		performer.Should().NotBeNull(
			because: "the assignment is stored on the element's own options, so a null here would mean the designer "
				+ "card renders an unassigned step whatever the parameters say");
		performer!.Type.Should().Be("role",
			because: "the stored assignment type is what the read-back derives the kind from");
		performer.Role.Should().NotBeNullOrWhiteSpace(
			because: "the role macro must be written on the dynamically CREATED RoleId parameter - an empty read here "
				+ "would mean the parameter was never added, which no schema-declared lookup would reveal");
		performer.RoleDisplay.Should().Be(OpenEditPagePerformerRole,
			because: "the name the caller used is kept as the display value, so a human opening the element sees a "
				+ "role name rather than a GUID");
		performer.ShowPage.Should().BeFalse(
			because: "the flag is written explicitly at create — so describe can report it rather than leaving it to an "
				+ "unreportable schema default — but its VALUE follows the performer, and a ROLE performer cannot have "
				+ "the page open by itself: the platform opens it only for the user a step is assigned to, and the "
				+ "designer disables that checkbox for role/manager");
	}

	[Test]
	[Description("Over the real MCP path, a page the process designer does NOT offer is REFUSED rather than stored. This is the protective half of the page rule: the designer resolves a stored page against its own candidate list, so an unlisted one would render 'Which page to open?' empty and lose the element's configuration on the next human save.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process refuses a page outside the designer's candidate set")]
	public async Task CreateBusinessProcess_Should_RefuseOpenEditPage_WhenPageIsNotOffered() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpOpenEditPageRefuseE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildOpenEditPageUnlistedPageDescriptor(processName)
		});

		// Assert
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().NotContain("created (UId:",
			because: "the build must be refused, not saved - storing an unshowable page is the silent data-loss case "
				+ "this rule exists to prevent");
		callResultJson.Should().Contain("not a page the process designer can open",
			because: "the refusal has to say WHY and point at how to find a valid page, or the caller cannot act on it");
	}

	private static string BuildOpenEditPageAddDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Open Edit Page Add E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "OpenPage1", "type": "openEditPage", "caption": "Open the account page",
		      "openEditPage": {
		        "page": "{{OpenEditPageName}}",
		        "editMode": "add",
		        "recommendation": "Fill in the account details",
		        "hint": "Confirm the billing address with the customer",
		        "defaultValues": [ { "column": "{{OpenEditPageTextColumn}}", "value": "ClioOpenEditPageProbe" } ],
		        "completion": { "mode": "onSave" }
		      } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "OpenPage1" },
		    { "source": "OpenPage1", "target": "EndEvent1" }
		  ]
		}
		""";

	// A results-by-column list on a lookup column that exists on Account everywhere, so the test needs no seeding.
	private static string BuildOpenEditPageResultsByColumnDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Open Edit Page Results By Column E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "OpenPage1", "type": "openEditPage", "caption": "Fill in the account",
		      "openEditPage": {
		        "page": "{{OpenEditPageName}}",
		        "editMode": "add",
		        "recommendation": "Fill in the account details",
		        "resultsByColumn": { "column": "{{OpenEditPageResultColumn}}" }
		      } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "OpenPage1" },
		    { "source": "OpenPage1", "target": "EndEvent1" }
		  ]
		}
		""";

	// A logged activity with two of the three scheduling pairs set to DIFFERENT units, so a server that mixed the
	// period parameters up would be caught by the read-back rather than passing on symmetry.
	private static string BuildOpenEditPageLogActivityDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Open Edit Page Log Activity E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "OpenPage1", "type": "openEditPage", "caption": "Fill in the account",
		      "openEditPage": {
		        "page": "{{OpenEditPageName}}",
		        "editMode": "add",
		        "recommendation": "Fill in the account details",
		        "logActivity": {
		          "duration": { "value": 20, "unit": "minutes" },
		          "remindIn": { "value": 2, "unit": "hours" },
		          "showInCalendar": true
		        }
		      } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "OpenPage1" },
		    { "source": "OpenPage1", "target": "EndEvent1" }
		  ]
		}
		""";

	// A performer assigned by ROLE NAME. The role is resolved server-side against SysAdminUnit, so this descriptor is
	// also what proves the name-resolution path against real data rather than a substituted reader.
	private static string BuildOpenEditPagePerformerDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Open Edit Page Performer E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "OpenPage1", "type": "openEditPage", "caption": "Open the account page",
		      "openEditPage": {
		        "page": "{{OpenEditPageName}}",
		        "editMode": "add",
		        "recommendation": "Fill in the account details",
		        "performer": { "type": "role", "role": "{{OpenEditPagePerformerRole}}" }
		      } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "OpenPage1" },
		    { "source": "OpenPage1", "target": "EndEvent1" }
		  ]
		}
		""";

	// Edit mode taking its record from the signal that STARTED the process - the shape this element is actually used
	// in, and the one that exercises the element-output value source instead of a fixed record id.
	private static string BuildOpenEditPageEditDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Open Edit Page Edit E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "Start1", "type": "signalStart",
		      "signal": { "entity": "{{OpenEditPageObject}}", "on": "added" } },
		    { "name": "OpenPage1", "type": "openEditPage", "caption": "Review the new account",
		      "openEditPage": {
		        "page": "{{OpenEditPageName}}",
		        "editMode": "edit",
		        "recommendation": "Review the account that was just created",
		        "recordId": { "sourceElement": "Start1", "sourceElementParameter": "RecordId" }
		      } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "Start1", "target": "OpenPage1" },
		    { "source": "OpenPage1", "target": "EndEvent1" }
		  ]
		}
		""";

	// A real schema that is NOT a section-registered edit page. ProcessSchemaManager is a configuration schema that
	// exists on every stand, so the refusal proves the CANDIDATE-SET check rather than a name-resolution failure.
	private static string BuildOpenEditPageUnlistedPageDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Open Edit Page Refusal E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "OpenPage1", "type": "openEditPage",
		      "openEditPage": { "page": "BaseUserTaskPropertiesPage", "editMode": "add" } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "OpenPage1" },
		    { "source": "OpenPage1", "target": "EndEvent1" }
		  ]
		}
		""";

	private static string BuildSendEmailDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Send Email E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "SendEmail1", "type": "sendEmail",
		      "email": { "bodyFormat": "html", "body": "<html><body><p>ClioSendEmailProbe for [[param:ClioProbeParam]]</p></body></html>" } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "SendEmail1" },
		    { "source": "SendEmail1", "target": "EndEvent1" }
		  ],
		  "parameters": [
		    { "name": "ClioProbeParam", "type": "ShortText", "direction": "In" }
		  ]
		}
		""";

	// Deserializes the described graph (the Info log-message value inside the clio command envelope) into the typed
	// DescribeProcessResult, so a test can assert element fields directly instead of substring-matching the escaped
	// envelope.
	private static DescribeProcessResult ParseDescribeGraph(CallToolResult describeResult) {
		CommandExecutionEnvelope envelope = McpCommandExecutionParser.Extract(describeResult);
		string graphJson = envelope.Output!
			.Select(message => message.Value)
			.First(value => !string.IsNullOrWhiteSpace(value) && value!.TrimStart().StartsWith("{", StringComparison.Ordinal))!;
		return JsonSerializer.Deserialize<DescribeProcessResult>(graphJson,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
	}

	// Exercises the element-level flag on TWO different element kinds at once: the signalStart is forced OFF (its kind
	// default is background mode, so this proves the override wins) and the user task is forced ON.
	private static string BuildBackgroundModeDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Background Mode E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "SignalStart1", "type": "signalStart", "useBackgroundMode": false,
		      "signal": { "entity": "Contact", "on": "modified" } },
		    { "name": "task1", "type": "performTask", "useBackgroundMode": true },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "SignalStart1", "target": "task1" },
		    { "source": "task1", "target": "EndEvent1" }
		  ]
		}
		""";

	// A signal-start firing on record DELETION — the one record-event type the other e2e descriptors never exercise
	// (they use added / modified / save). No changedColumns: tracked columns are rejected for a delete trigger.
	private static string BuildDeleteTriggerDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Signal Delete E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "SignalStart1", "type": "signalStart",
		      "signal": { "entity": "Contact", "on": "deleted" } },
		    { "name": "task1", "type": "performTask" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "SignalStart1", "target": "task1" },
		    { "source": "task1", "target": "EndEvent1" }
		  ]
		}
		""";

	// A signal-start restricted to a tracked-change column (on:modified, changedColumns:[Name]). Contact.Name is a base
	// column on every stand, so build resolves the name->UId and describe decodes the UId->name — proving the tracked
	// column round-trips through BOTH the package and the clio DTO, not merely that a signalStart exists.
	private static string BuildTrackedColumnsDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Signal Cols E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "SignalStart1", "type": "signalStart",
		      "signal": { "entity": "Contact", "on": "modified", "changedColumns": ["Name"] } },
		    { "name": "task1", "type": "performTask" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "SignalStart1", "target": "task1" },
		    { "source": "task1", "target": "EndEvent1" }
		  ]
		}
		""";

	private static string BuildDescriptorWithDescription(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Desc E2E",
		  "packageName": "Custom",
		  "parameters": [ { "name": "Note", "type": "Text", "direction": "In", "caption": "Customer note", "description": "Free-text note about the customer" } ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "EndEvent1" }
		  ]
		}
		""";

	private static string BuildDescriptorWithDefault(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Default E2E",
		  "packageName": "Custom",
		  "parameters": [ { "name": "Retries", "type": "Integer", "direction": "In", "value": "3" } ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "EndEvent1" }
		  ]
		}
		""";

	// Reads the built process back as a structured graph via describe-business-process (for build readback).
	private static async Task<CallToolResult> DescribeAsync(ArrangeContext context, string processCode) =>
		await context.Session.CallToolAsync(
			DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["process-name"] = processCode
				}
			},
			context.CancellationTokenSource.Token);

	private static string BuildDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP E2E",
		  "packageName": "Custom",
		  "parameters": [ { "name": "MyText", "type": "Text", "direction": "In" } ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "task1", "type": "performTask" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "task1" },
		    { "source": "task1", "target": "EndEvent1" }
		  ],
		  "mappings": [
		    { "elementName": "task1", "elementParameter": "Recommendation", "processParameter": "MyText" }
		  ]
		}
		""";

	// ENG-92127: a process parameter whose type mirrors task1's "Recommendation" element parameter via
	// typeFromElement/typeFromElementParameter (the type is copied verbatim, no conversion), alongside a
	// process-parameter -> element-input mapping. Exercises the extended create-business-process contract.
	private static string BuildTypeMirrorDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Mapping E2E",
		  "packageName": "Custom",
		  "parameters": [
		    { "name": "MyText", "type": "Text", "direction": "In" },
		    { "name": "MirroredType", "typeFromElement": "task1", "typeFromElementParameter": "Recommendation", "direction": "Out" }
		  ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "task1", "type": "performTask" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "task1" },
		    { "source": "task1", "target": "EndEvent1" }
		  ],
		  "mappings": [
		    { "elementName": "task1", "elementParameter": "Recommendation", "processParameter": "MyText" }
		  ]
		}
		""";

	// ENG-92127 (AC#2): a Boolean process parameter (ProcResult) fed by the user-task element's OUTPUT via a
	// targetProcessParameter mapping — the element->process shape. Source/target share the Boolean type group.
	private static string BuildElementOutputToProcessDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Output Mapping E2E",
		  "packageName": "Custom",
		  "parameters": [
		    { "name": "ProcResult", "type": "Boolean", "direction": "Out" }
		  ],
		  "elements": [
		    { "name": "Start1", "type": "startEvent" },
		    { "name": "check", "type": "userTask", "userTaskName": "{{SourceUserTaskName}}" },
		    { "name": "End1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "Start1", "target": "check" },
		    { "source": "check", "target": "End1" }
		  ],
		  "mappings": [
		    { "targetProcessParameter": "ProcResult", "sourceElement": "check", "sourceElementParameter": "{{SourceOutputParameter}}" }
		  ]
		}
		""";

	// ENG-92127 (AC#3): an INCOMPATIBLE element->process mapping — the Boolean element output into an Integer
	// process parameter — which the type-compatibility gate must reject (Boolean and Number are different kinds).
	private static string BuildIncompatibleTypeMappingDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Bad-Type Mapping E2E",
		  "packageName": "Custom",
		  "parameters": [
		    { "name": "BadNum", "type": "Integer", "direction": "Out" }
		  ],
		  "elements": [
		    { "name": "Start1", "type": "startEvent" },
		    { "name": "check", "type": "userTask", "userTaskName": "{{SourceUserTaskName}}" },
		    { "name": "End1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "Start1", "target": "check" },
		    { "source": "check", "target": "End1" }
		  ],
		  "mappings": [
		    { "targetProcessParameter": "BadNum", "sourceElement": "check", "sourceElementParameter": "{{SourceOutputParameter}}" }
		  ]
		}
		""";

	// A self-referential parameter mapping — a process parameter mapped to ITSELF — forms a
	// circular dependency the platform interpretation validator rejects on save; the pre-save gate
	// (ProcessSchemaValidator -> GetProcessValidationResult) surfaces that rejection instead of persisting it.
	private static string BuildSelfReferentialMappingDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Self-Map E2E",
		  "packageName": "Custom",
		  "parameters": [
		    { "name": "SelfRef", "type": "Text", "direction": "Variable" }
		  ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "EndEvent1" }
		  ],
		  "mappings": [
		    { "targetProcessParameter": "SelfRef", "processParameter": "SelfRef" }
		  ]
		}
		""";

	// ENG-92127 (AC#1): one element's OUTPUT into ANOTHER element's INPUT. performTask (ActivityUserTask) exposes
	// the Guid output ActivityResult, which flows into CheckCanExecuteOperationUserTask's Guid input UserId
	// (Guid<->Guid). The source element precedes the target in the flow so its output exists first.
	private static string BuildElementToElementDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Element-to-Element E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "Start1", "type": "startEvent" },
		    { "name": "task1", "type": "{{ElementSourceTaskType}}" },
		    { "name": "check", "type": "userTask", "userTaskName": "{{SourceUserTaskName}}" },
		    { "name": "End1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "Start1", "target": "task1" },
		    { "source": "task1", "target": "check" },
		    { "source": "check", "target": "End1" }
		  ],
		  "mappings": [
		    { "elementName": "check", "elementParameter": "{{ElementTargetInput}}", "sourceElement": "task1", "sourceElementParameter": "{{ElementSourceOutput}}" }
		  ]
		}
		""";

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext context, Dictionary<string, object?> args) {
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		toolNames.Should().Contain(ToolName,
			because: "the create-business-process tool must be discoverable via the get-tool-contract compact index before the end-to-end call");
		return await context.Session.CallToolAsync(
			ToolName, new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
	}

	private static async Task<ArrangeContext> ArrangeAsync(bool requireReachableEnvironment) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (requireReachableEnvironment) {
			if (string.IsNullOrWhiteSpace(environmentName)) {
				Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName (with the ProcessDesignService package) to run create-business-process MCP E2E.");
			}
			if (!await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName!)) {
				Assert.Ignore($"create-business-process MCP E2E requires a reachable configured sandbox environment. '{environmentName}' was not reachable.");
			}
		}
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
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
}
