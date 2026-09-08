using System;
using System.IO;
using System.Reflection;
using Clio.Command;
using Clio.Command.McpServer.Prompts.ProcessDesigner;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Prompts.ProcessDesigner;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Command.ProcessModel;
using Clio.Common;
using System.Linq;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class ModifyBusinessProcessToolTests {
	private const string SampleOperations =
		"[{\"op\":\"removeElement\",\"elementName\":\"StartEvent1\"}]";

	private static readonly string RepositoryRoot = Path.GetFullPath(
		Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

	/// <summary>
	/// Reads a tool method's shipped <see cref="System.ComponentModel.DescriptionAttribute"/> text - the string an
	/// agent actually receives from <c>tools/list</c>, rather than a copy of it kept in the test.
	/// </summary>
	private static string ReadToolDescription(Type toolType, string methodName) =>
		toolType.GetMethod(methodName)!
			.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;

	[Test]
	[Category("Unit")]
	[Description("Pins the destructive classification of modify-business-process. This annotation - not the description prose - is what an MCP host reads to decide whether a call needs human approval, so a silent flip back to false would let a host auto-run it.")]
	public void ModifyBusinessProcess_Should_Be_Marked_As_Destructive() {
		// Arrange
		System.Reflection.MethodInfo method = typeof(ModifyBusinessProcessTool).GetMethod(nameof(ModifyBusinessProcessTool.ModifyBusinessProcess))!;
		McpServerToolAttribute attribute = method
			.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false)
			.Cast<McpServerToolAttribute>()
			.Single();

		// Act
		bool destructive = attribute.Destructive;

		// Assert
		destructive.Should().BeTrue(because: "modify-business-process edits an existing process in place and can revoke record permissions through an accessRights block");
	}

	[Test]
	[Description("Resolves the modify-business-process MCP tool for the requested environment and forwards the identity and operations into command options.")]
	[Category("Unit")]
	public void ModifyBusinessProcess_Should_Resolve_Command_And_Forward_Operations() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeModifyBusinessProcessCommand defaultCommand = new();
		FakeModifyBusinessProcessCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ModifyBusinessProcessCommand>(Arg.Any<ModifyBusinessProcessOptions>())
			.Returns(resolvedCommand);
		ModifyBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyBusinessProcess(
			new ModifyBusinessProcessArgs("docker_fix2", SampleOperations, "UsrSampleProcess", null));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the modify-business-process tool should forward a valid command payload for the requested environment");
		commandResolver.Received(1).Resolve<ModifyBusinessProcessCommand>(Arg.Is<ModifyBusinessProcessOptions>(options =>
			options.Environment == "docker_fix2" &&
			options.ProcessName == "UsrSampleProcess" &&
			options.OperationsJson == SampleOperations));
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the environment-aware tool path should use the resolved command instance, not the startup one");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the forwarded modify-business-process options");
		resolvedCommand.CapturedOptions!.OperationsJson.Should().Be(SampleOperations,
			because: "the inline operations must be carried through to the command without modification");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("modify-business-process emits the deterministic compile-not-required note after a successful edit, so an agent does not mistake 'edited' for 'must be compiled to run' and force compile-creatio (ENG-95706).")]
	public void ModifyBusinessProcess_Should_Emit_CompileNotRequiredNote_On_Success() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeModifyBusinessProcessCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ModifyBusinessProcessCommand>(Arg.Any<ModifyBusinessProcessOptions>())
			.Returns(resolvedCommand);
		ModifyBusinessProcessTool tool = new(new FakeModifyBusinessProcessCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyBusinessProcess(
			new ModifyBusinessProcessArgs("docker_fix2", SampleOperations, "UsrSampleProcess", null));

		// Assert
		result.ExitCode.Should().Be(0, because: "the fake command reports a successful edit");
		result.Note.Should().Be(CommandExecutionResult.CompileNotRequiredNote,
			because: "a clio-edited process stays interpreted and needs no compile; the response note is the one channel the agent cannot skip (ENG-95706)");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("modify-business-process suppresses the compile-not-required note when the edit FAILS — a failed mutation must not be told 'no compile needed' (ENG-95706).")]
	public void ModifyBusinessProcess_Should_Not_Emit_CompileNotRequiredNote_On_Failure() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeModifyBusinessProcessCommand resolvedCommand = new(exitCode: 1);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ModifyBusinessProcessCommand>(Arg.Any<ModifyBusinessProcessOptions>())
			.Returns(resolvedCommand);
		ModifyBusinessProcessTool tool = new(new FakeModifyBusinessProcessCommand(exitCode: 1), ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyBusinessProcess(
			new ModifyBusinessProcessArgs("docker_fix2", SampleOperations, "UsrSampleProcess", null));

		// Assert
		result.ExitCode.Should().NotBe(0, because: "the fake command reports a failed edit");
		result.Note.Should().NotBe(CommandExecutionResult.CompileNotRequiredNote,
			because: "a success-only signal must not ride a failed mutation");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Forwards an addElement operation that adds a sendEmail element with its full email block verbatim — the tool is an opaque pass-through, so the new element type and every email field (mode, sender, To recipients, subject, HTML body, importance, ignoreErrors, manual-mode performer) ride through to the command without modification.")]
	[Category("Unit")]
	public void ModifyBusinessProcess_Should_Forward_SendEmail_AddElement_Verbatim() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		const string sendEmailOps =
			"[{\"op\":\"addElement\",\"element\":{\"name\":\"SendEmail1\",\"type\":\"sendEmail\","
			+ "\"email\":{\"mode\":\"manual\",\"sender\":\"sales@example.com\",\"subject\":\"Order update\","
			+ "\"body\":\"<p>Hello</p>\",\"bodyFormat\":\"html\",\"to\":[{\"value\":\"to@example.com\"}],"
			+ "\"importance\":\"high\",\"ignoreErrors\":true,"
			+ "\"performer\":{\"type\":\"role\",\"role\":\"All employees\",\"showPage\":true}}}}]";
		FakeModifyBusinessProcessCommand defaultCommand = new();
		FakeModifyBusinessProcessCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ModifyBusinessProcessCommand>(Arg.Any<ModifyBusinessProcessOptions>())
			.Returns(resolvedCommand);
		ModifyBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyBusinessProcess(
			new ModifyBusinessProcessArgs("docker_fix2", sendEmailOps, "UsrSampleProcess", null));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a valid sendEmail addElement operation must be forwarded for the requested environment");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the forwarded operations");
		resolvedCommand.CapturedOptions!.OperationsJson.Should().Be(sendEmailOps,
			because: "the sendEmail addElement operation and its whole email block must pass through unchanged (opaque pass-through)");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Forwards a setElement operation carrying a changeData block verbatim — the tool is an opaque pass-through, so the target source, every value-source kind (constant, processParameter, sourceElement pair, expression) and the whole block ride through to the command unmodified. Guards the one thing this repo CAN assert about the changeData contract: that clio does not reshape it on the way to the server.")]
	[Category("Unit")]
	public void ModifyBusinessProcess_Should_Forward_ChangeData_SetElement_Verbatim() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		const string changeDataOps =
			"[{\"op\":\"setElement\",\"elementName\":\"UpdateContact\",\"elementUpdate\":{\"changeData\":{"
			+ "\"source\":\"Contact\",\"values\":["
			+ "{\"column\":\"JobTitle\",\"value\":\"Manager\"},"
			+ "{\"column\":\"Notes\",\"processParameter\":\"NoteTextParameter\"},"
			+ "{\"column\":\"AccountId\",\"sourceElement\":\"RecordModifiedSignal\","
			+ "\"sourceElementParameter\":\"RecordId\"},"
			+ "{\"column\":\"DueDate\",\"expression\":\"[#DateValue.2026-09-01#]\"}]}}}]";
		FakeModifyBusinessProcessCommand defaultCommand = new();
		FakeModifyBusinessProcessCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ModifyBusinessProcessCommand>(Arg.Any<ModifyBusinessProcessOptions>())
			.Returns(resolvedCommand);
		ModifyBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyBusinessProcess(
			new ModifyBusinessProcessArgs("docker_fix2", changeDataOps, "UsrSampleProcess", null));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a valid changeData setElement operation must be forwarded for the requested environment");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the forwarded operations");
		resolvedCommand.CapturedOptions!.OperationsJson.Should().Be(changeDataOps,
			because: "the changeData block and every value-source kind in it must pass through unchanged — the "
				+ "server owns the semantics, so any reshaping here would silently alter what the caller asked for");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Forwards a setElement operation carrying an openEditPage block verbatim - the tool is an opaque pass-through, so the destructive mode switch and its replacement payload ride through to the command unmodified. The mode switch is chosen deliberately: it is the operation whose refusal rules live entirely server-side, so a tool that reshaped the block would change which refusals the caller sees while every clio-side test still passed.")]
	[Category("Unit")]
	public void ModifyBusinessProcess_Should_Forward_OpenEditPage_SetElement_Verbatim() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		const string openEditPageOps =
			"[{\"op\":\"setElement\",\"elementName\":\"OpenPage1\",\"elementUpdate\":{\"openEditPage\":{"
			+ "\"editMode\":\"edit\",\"recordId\":{\"processParameter\":\"AccountIdParameter\"},"
			+ "\"performer\":{\"type\":\"user\",\"showPage\":true},"
			+ "\"logActivity\":{\"enabled\":true,\"duration\":{\"value\":5,\"unit\":\"minutes\"}},"
			+ "\"completion\":{\"mode\":\"onSave\"}}}}]";
		FakeModifyBusinessProcessCommand defaultCommand = new();
		FakeModifyBusinessProcessCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ModifyBusinessProcessCommand>(Arg.Any<ModifyBusinessProcessOptions>())
			.Returns(resolvedCommand);
		ModifyBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyBusinessProcess(
			new ModifyBusinessProcessArgs("docker_fix2", openEditPageOps, "UsrSampleProcess", null));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a valid openEditPage setElement operation must be forwarded for the requested environment");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the forwarded operations");
		resolvedCommand.CapturedOptions!.OperationsJson.Should().Be(openEditPageOps,
			because: "the openEditPage block must pass through unchanged - the mode switch, its record payload and "
				+ "the nested completion block are all judged server-side, so reshaping any of them here would "
				+ "alter the request without changing a single clio-side assertion");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("The rendered modify prompt carries no line duplicated verbatim and no clause left without its object. This is a MERGE guard, not a style check: the text is one 30-line sentence assembled from per-element fragments, so a merge that lands the same fragment twice, or truncates one mid-clause, produces a string that still compiles, still ships, and is fed verbatim to an LLM on every invocation - degrading instruction-following with nothing to notice it. That damage reached this file once already.")]
	[Category("Unit")]
	public void RenderedPrompt_Should_CarryNoDuplicatedLine_NorAClauseWithoutItsObject() {
		// Arrange
		string prompt = ModifyBusinessProcessPrompt.PromptByProcess("docker_fix2", "UsrSampleProcess");
		string[] lines = prompt.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();

		// Act
		string[] duplicatedNeighbours = lines
			.Zip(lines.Skip(1), (first, second) => first == second ? first : null)
			.Where(line => line != null)
			.ToArray()!;
		int connectionsClause = Regex.Matches(prompt,
			Regex.Escape("`setConnections` binds the \"Connected to\" links of the")).Count;

		// Assert
		duplicatedNeighbours.Should().BeEmpty(
			because: "a fragment landing twice is what a bad merge produces here, and a duplicated noun phrase "
				+ "inside one long sentence reads as emphasis to a model rather than as damage");
		connectionsClause.Should().Be(1,
			because: "the clause is what introduces setConnections; a second copy means one of them was cut off "
				+ "from the object it introduces, leaving an enumeration item that never says what it binds");
	}

	[Test]
	[Description("Returns a failed result without resolving any command when the environment name is empty.")]
	[Category("Unit")]
	public void ModifyBusinessProcess_Should_Fail_When_Environment_Is_Empty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeModifyBusinessProcessCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ModifyBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyBusinessProcess(
			new ModifyBusinessProcessArgs("   ", SampleOperations, "UsrSampleProcess", null));

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "an empty environment name is a validation error that must not reach command resolution");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<ModifyBusinessProcessCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a failed result without resolving any command when no process identity is provided.")]
	[Category("Unit")]
	public void ModifyBusinessProcess_Should_Fail_When_No_Identity_Provided() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeModifyBusinessProcessCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ModifyBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyBusinessProcess(
			new ModifyBusinessProcessArgs("docker_fix2", SampleOperations, null, null));

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "a missing process identity is a validation error that must not reach command resolution");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<ModifyBusinessProcessCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a failed result without resolving any command when both a process name and uid are provided.")]
	[Category("Unit")]
	public void ModifyBusinessProcess_Should_Fail_When_Both_Name_And_Uid_Provided() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeModifyBusinessProcessCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ModifyBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyBusinessProcess(new ModifyBusinessProcessArgs(
			"docker_fix2", SampleOperations, "UsrSampleProcess", "5c58c4c4-134b-4744-9c67-96d9c69c9d55"));

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "an ambiguous identity (both name and uid) is a validation error that must not reach command resolution");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<ModifyBusinessProcessCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a failed result without resolving any command when the operations are empty.")]
	[Category("Unit")]
	public void ModifyBusinessProcess_Should_Fail_When_Operations_Are_Empty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeModifyBusinessProcessCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ModifyBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyBusinessProcess(
			new ModifyBusinessProcessArgs("docker_fix2", "   ", "UsrSampleProcess", null));

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "empty operations is a validation error that must not reach command resolution");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<ModifyBusinessProcessCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Forwards a setElement operation carrying an accessRights block verbatim — the tool is an opaque pass-through, so a replaced add collection, a remove collection cleared with an empty array, and the object retarget that clears the stored record filter all ride through unchanged, together with the setFilter re-issued in the same array.")]
	[Category("Unit")]
	public void ModifyBusinessProcess_Should_Forward_AccessRights_SetElement_Verbatim() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		const string accessRightsOps =
			"[{\"op\":\"setElement\",\"elementName\":\"GrantRights\",\"elementUpdate\":{\"accessRights\":{"
			+ "\"object\":\"Contact\",\"considerTimeInFilter\":false,"
			+ "\"add\":[{\"operations\":[\"read\"],\"level\":\"permit\","
			+ "\"grantee\":{\"type\":\"role\",\"role\":\"System administrators\"}}],"
			+ "\"remove\":[]}}},"
			+ "{\"op\":\"setFilter\",\"elementName\":\"GrantRights\",\"filter\":{\"object\":\"Contact\","
			+ "\"conditions\":[{\"column\":\"Id\",\"comparison\":\"equal\",\"processParameter\":\"ContactId\"}]}}]";
		FakeModifyBusinessProcessCommand defaultCommand = new();
		FakeModifyBusinessProcessCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ModifyBusinessProcessCommand>(Arg.Any<ModifyBusinessProcessOptions>())
			.Returns(resolvedCommand);
		ModifyBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyBusinessProcess(
			new ModifyBusinessProcessArgs("docker_fix2", accessRightsOps, "UsrSampleProcess", null));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a valid accessRights setElement operation must be forwarded for the requested environment");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the forwarded operations");
		resolvedCommand.CapturedOptions!.OperationsJson.Should().Be(accessRightsOps,
			because: "replace-and-clear semantics live in the exact arrays the caller sent — an empty "
				+ "remove array means CLEAR, so dropping or rewriting it here would silently keep permissions "
				+ "the caller asked to stop revoking, and the paired setFilter must survive in the same order");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeModifyBusinessProcessCommand : ModifyBusinessProcessCommand {
		private readonly int _exitCode;

		public ModifyBusinessProcessOptions? CapturedOptions { get; private set; }

		public FakeModifyBusinessProcessCommand(int exitCode = 0)
			: base(Substitute.For<IModifyBusinessProcessService>(), Substitute.For<IProcessDescriber>(),
				Substitute.For<ILogger>()) {
			_exitCode = exitCode;
		}

		public override int Execute(ModifyBusinessProcessOptions options) {
			CapturedOptions = options;
			return _exitCode;
		}
	}
	[Test]
	[Category("Unit")]
	[Description("Pins the DIRECTION of the record-filter consequence in the always-loaded tool description and in the prompt. An ABSENT record filter is the WIDENING state - the runtime gates on a non-empty filter, so the query runs unfiltered with record permissions disabled - while a PRESENT-but-conditionless one is the inert state. The shipped text had these two swapped, on every surface at once, which told callers that the widest permission change the feature can produce was harmless. Prose is the whole contract here: the element has no output parameters, so nothing at run time contradicts a wrong description.")]
	public void ModifyBusinessProcessTool_ShouldStateTheRecordFilterConsequence_InTheWideningDirection() {
		// Arrange
		string[] surfaces = [
			typeof(ModifyBusinessProcessTool).GetMethod(nameof(ModifyBusinessProcessTool.ModifyBusinessProcess))!
				.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description,
			ModifyBusinessProcessPrompt.PromptByProcess("sandbox", "UsrSampleProcess")
		];

		// Act & Assert
		foreach (string surface in surfaces) {
			surface.Should().Contain("EVERY record",
				because: "a Change access rights element with no record filter applies the change to every row of "
					+ "its object, and this is the only place a caller is ever told so");
			surface.Should().NotContain("matches no records",
				because: "that is the inverted claim this feature shipped with - it describes the "
					+ "present-but-conditionless state, and applying it to an absent filter presents the widest "
					+ "possible configuration as a no-op");
			surface.Should().NotContain("match no records",
				because: "the same inversion in the future tense - both phrasings reached shipped text before");
		}
	}


	[Test]
	[Category("Unit")]
	[Description("Neither write tool promises the character index for the whole 'Formula value error:' family, because the index is a PARSE artifact and half the measured family carries none. Three classes carry it - a syntax fault, 'Expression expected', and 'No applicable method'. Three do not - an unknown identifier ('Parameter \"X\" not found'), a type conversion ('Cannot convert type A to B'), and a newline. The last two are the commonest ways to get a formula wrong, so a caller told to expect an index goes looking for a missing one on exactly the messages that already say what to fix.")]
	public void FormulaRefusalDescriptions_ShouldScopeTheCharacterIndexToParseFaults() {
		// Arrange
		string modifyDescription = ReadToolDescription(typeof(ModifyBusinessProcessTool),
			nameof(ModifyBusinessProcessTool.ModifyBusinessProcess));
		string createDescription = ReadToolDescription(typeof(CreateBusinessProcessTool),
			nameof(CreateBusinessProcessTool.CreateBusinessProcess));

		// Act
		(string Surface, string Text)[] surfaces = [
			("modify-business-process [Description]", modifyDescription),
			("create-business-process [Description]", createDescription)
		];

		// Assert
		foreach ((string surface, string text) in surfaces) {
			text.Should().NotContain("whole 'Formula value error:' family",
				because: $"'{surface}' would be scoping the index to the whole family, and the platform splits it "
					+ "down the middle: the index comes from the parser, so a fault raised after the parse - a "
					+ "binding or a conversion - has no position to report");
			text.Should().Contain("type mismatch",
				because: $"'{surface}' has to name a conversion fault as one that carries no index; 'Cannot convert "
					+ "type \"Decimal\" to \"Int32\"' is measured and says exactly what to fix, so sending a caller "
					+ "to look for an index it does not have is what makes them distrust it");
			text.Should().Contain("unknown identifier",
				because: $"'{surface}' has to name the other one; 'Parameter \"System\" not found' was measured "
					+ "three times over and appears in no exception list either description offered");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Every shipped clio surface that tells a caller how to get rid of a flow condition also carries the consequence of the destructive route. Removing the last conditional flow off an element stops the platform synthesizing the exclusive gateway, so EVERY outgoing flow is then taken - and describe reports kind:'sequence' on both, which reads exactly like the condition was cleared as asked. A surface that teaches remove-and-add without that sentence turns an approval gate into a parallel split silently.")]
	public void ClearingACondition_ShouldCarryTheGatewayHazard_OnEveryShippedSurface() {
		// Arrange
		const string hazard = "stops synthesizing the gateway";
		string toolDescription = ReadToolDescription(typeof(ModifyBusinessProcessTool),
			nameof(ModifyBusinessProcessTool.ModifyBusinessProcess));
		string prompt = ModifyBusinessProcessPrompt.PromptByProcess("env", "UsrSampleProcess");
		string capabilityMap = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "McpCapabilityMap.md"));

		// Act
		(string Surface, string Text)[] surfaces = [
			("modify-business-process [Description]", toolDescription),
			("modify-business-process prompt", prompt),
			("docs/McpCapabilityMap.md", capabilityMap)
		];

		// Assert
		foreach ((string surface, string text) in surfaces) {
			text.Should().Contain(hazard,
				because: $"'{surface}' tells a caller what to do about an unwanted flow condition, and without this "
					+ "consequence they take the remove-and-add route, lose the synthesized gateway, and every "
					+ "outgoing branch runs - measured on a stand at the shipping archive: an approval path became "
					+ "unreachable for every input and describe still reported kind:'sequence' on both flows");
		}
	}
}
