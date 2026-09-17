using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-98566. BEHAVIOURAL coverage that each process-designer tool actually INVOKES its unknown-argument
/// guard - the proposition the reflective <see cref="ProcessDesignerArgumentGuardTests"/> cannot establish.
/// <para>
/// That fixture asserts a <c>[JsonExtensionData]</c> bag and a <c>ValidArgsHint</c> constant exist. Both
/// survive deleting the <c>BuildLegacyAliasError</c> call from a tool, which is exactly how the defect comes
/// back: a bag nobody reads is the failure mode, not the fix. So the guard needs an oracle that fails when
/// the CALL disappears, not when a declaration does. Every test here dies if its tool stops checking.
/// </para>
/// <para>
/// The tools are constructed with a null command on purpose: <c>BaseTool</c> declares its command parameter
/// nullable, and the guard returns before anything touches it. A test that reached the command would be
/// testing the wrong thing.
/// </para>
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ProcessDesignerUnknownArgumentRefusalTests {

	private const string EnvName = "dev";

	/// <summary>The mis-key used throughout: a plausible typo rather than a nonsense token.</summary>
	private const string UnknownKey = "procesName";

	private IToolCommandResolver _commandResolver;

	[SetUp]
	public void SetUp() {
		ConsoleLogger.Instance.ClearMessages();
		_commandResolver = Substitute.For<IToolCommandResolver>();
	}

	[TearDown]
	public void TearDown() => _commandResolver.ClearReceivedCalls();

	private static Dictionary<string, JsonElement> Overflow() => new() {
		[UnknownKey] = JsonDocument.Parse("\"UsrOrder_Handle\"").RootElement
	};

	private static string TextOf(CommandExecutionResult result) =>
		string.Join(" ", result.Output.Select(message => message.Value?.ToString()));

	private void AssertRefused(CommandExecutionResult result, string toolName) {
		result.ExitCode.Should().Be(1,
			because: toolName + " must classify a mis-keyed argument as an EXPECTED, caller-actionable "
				+ "validation error (exit 1), not as an unexpected runtime failure (-1) a consumer might retry");
		TextOf(result).Should().Contain(UnknownKey,
			because: toolName + " must NAME the key it could not bind - that is the entire remedy, because "
				+ "the serializer drops it silently and the caller cannot otherwise see the loss");
		_commandResolver.ReceivedCalls().Should().BeEmpty(
			because: toolName + " must answer a caller mistake without resolving an environment or reaching "
				+ "Creatio");
	}

	/// <summary>
	/// Asserts a null-args refusal on a command-shaped tool. Checks the exit CODE as well as the message:
	/// swapping FromValidationError for FromError leaves the text identical while changing the answer from
	/// "your call is wrong" (1) to "clio itself broke" (-1), which consumers branch on.
	/// </summary>
	private static void AssertNullRefused(CommandExecutionResult result, string toolName) {
		result.ExitCode.Should().Be(1,
			because: toolName + " must report a missing argument object as a caller-actionable validation "
				+ "error, not as an unexpected runtime failure");
		TextOf(result).Should().Contain("args is required",
			because: toolName + " must name what is missing rather than throw or blame something else");
	}

	[Test]
	[Category("Unit")]
	[Description("create-business-process names an unrecognized argument instead of dropping it and building "
		+ "from a descriptor the caller did not mean to send.")]
	public void CreateBusinessProcess_ShouldRefuseAnUnknownArgument_AndNameIt() {
		// Arrange
		CreateBusinessProcessTool tool = new(null, ConsoleLogger.Instance, _commandResolver);
		CreateBusinessProcessArgs args =
			new(EnvironmentName: EnvName, Descriptor: "{}") { ExtensionData = Overflow() };

		// Act
		CommandExecutionResult result = tool.CreateBusinessProcess(args);

		// Assert
		AssertRefused(result, CreateBusinessProcessTool.CreateBusinessProcessToolName);
	}

	[Test]
	[Category("Unit")]
	[Description("describe-business-process names an unrecognized argument. This is the tool an agent has "
		+ "usually just called before mis-keying the same argument into another one, so it must not itself "
		+ "model the silent-drop behaviour.")]
	public void DescribeProcess_ShouldRefuseAnUnknownArgument_AndNameIt() {
		// Arrange
		DescribeProcessTool tool = new(null, ConsoleLogger.Instance, _commandResolver);
		DescribeProcessArgs args =
			new(EnvironmentName: EnvName, ProcessName: "UsrOrder_Handle") { ExtensionData = Overflow() };

		// Act
		CommandExecutionResult result = tool.DescribeProcess(args);

		// Assert
		AssertRefused(result, DescribeProcessTool.ToolName);
	}

	[Test]
	[Category("Unit")]
	[Description("modify-business-process names an unrecognized argument. It is Destructive = true, so a call "
		+ "assembled from a partially-dropped payload is the worst of the family to let through.")]
	public void ModifyBusinessProcess_ShouldRefuseAnUnknownArgument_AndNameIt() {
		// Arrange
		ModifyBusinessProcessTool tool = new(null, ConsoleLogger.Instance, _commandResolver);
		ModifyBusinessProcessArgs args = new(EnvironmentName: EnvName, Operations: "[]",
			ProcessName: "UsrOrder_Handle") { ExtensionData = Overflow() };

		// Act
		CommandExecutionResult result = tool.ModifyBusinessProcess(args);

		// Assert
		AssertRefused(result, ModifyBusinessProcessTool.ModifyBusinessProcessToolName);
	}

	[Test]
	[Category("Unit")]
	[Description("modify-business-process-as-new-version names an unrecognized argument.")]
	public void ModifyProcessAsNewVersion_ShouldRefuseAnUnknownArgument_AndNameIt() {
		// Arrange
		ModifyProcessAsNewVersionTool tool = new(null, ConsoleLogger.Instance, _commandResolver);
		ModifyProcessAsNewVersionArgs args = new(EnvironmentName: EnvName,
			ProcessName: "UsrOrder_Handle", Operations: "[]") { ExtensionData = Overflow() };

		// Act
		CommandExecutionResult result = tool.ModifyProcessAsNewVersion(args);

		// Assert
		AssertRefused(result, ModifyProcessAsNewVersionTool.ModifyProcessAsNewVersionToolName);
	}

	[Test]
	[Category("Unit")]
	[Description("set-active-business-process-version names an unrecognized argument. It is Destructive = true "
		+ "and picks WHICH family member runs, so a dropped identity key changes what executes afterwards.")]
	public void SetActiveProcessVersion_ShouldRefuseAnUnknownArgument_AndNameIt() {
		// Arrange
		SetActiveProcessVersionTool tool = new(null, ConsoleLogger.Instance, _commandResolver);
		SetActiveProcessVersionArgs args =
			new(EnvironmentName: EnvName, VersionName: "UsrOrder_Handle") { ExtensionData = Overflow() };

		// Act
		CommandExecutionResult result = tool.SetActiveProcessVersion(args);

		// Assert
		AssertRefused(result, SetActiveProcessVersionTool.SetActiveProcessVersionToolName);
	}

	[Test]
	[Category("Unit")]
	[Description("get-process-signature names an unrecognized argument in its own response shape "
		+ "(success=false plus error) rather than the CommandExecutionResult shape the command-shaped tools use.")]
	public void GetProcessSignature_ShouldRefuseAnUnknownArgument_AndNameIt() {
		// Arrange
		GetProcessSignatureTool tool = new(null, ConsoleLogger.Instance, _commandResolver);
		GetProcessSignatureArgs args =
			new(ProcessName: "UsrOrder_Handle", EnvironmentName: EnvName) { ExtensionData = Overflow() };

		// Act
		GetProcessSignatureResponse response = tool.GetProcessSignature(args);

		// Assert
		response.Success.Should().BeFalse(
			because: "an argument the tool cannot bind is a caller mistake, not a signature it read");
		response.Error.Should().Contain(UnknownKey,
			because: "the offending key must be named back, in the response shape this tool uses");
		_commandResolver.ReceivedCalls().Should().BeEmpty(
			because: "the refusal must not cost an environment resolution");
	}

	[Test]
	[Category("Unit")]
	[Description("run-process names an unrecognized argument and returns BEFORE the progress heartbeat and "
		+ "deadline wrapper are created, so nothing is left to time out or to strand a progress token. It is "
		+ "Destructive = true: a dropped key here would launch a process the caller did not describe.")]
	public async Task RunProcess_ShouldRefuseAnUnknownArgument_AndNameIt() {
		// Arrange
		RunProcessTool tool = new(ConsoleLogger.Instance, _commandResolver);
		RunProcessArgs args = new() {
			ProcessName = "UsrOrder_Handle",
			EnvironmentName = EnvName,
			ExtensionData = Overflow()
		};

		// Act
		RunProcessResponse response = await tool.RunProcess(args);

		// Assert
		response.Error.Should().Contain(UnknownKey,
			because: "run-process signals failure through the error field, and the key must appear in it");
		response.Status.Should().BeNull(
			because: "the documented contract is that status is null when the call was rejected before launch");
		_commandResolver.ReceivedCalls().Should().BeEmpty(
			because: "no tenant key, no command and no heartbeat may be created for a refused call");
	}
	[Test]
	[Category("Unit")]
	[Description("ENG-98566 / Sonar S2259: a call carrying no argument object at all is REFUSED with a named "
		+ "reason, in every tool. Before this, three of them tolerated a null args in the guard and then "
		+ "dereferenced it on the next statement, outside any try - so the NullReferenceException escaped as a "
		+ "raw transport fault. Sonar derived the same three files independently from the null-flow model. "
		+ "The point of the test is that the decision is now UNIFORM: one place per tool decides, and none of "
		+ "them reaches a field read with null. This is a METHOD-contract test, not wire coverage: measured "
		+ "over the real MCP server, {\"args\":null} is answered by the SDK itself with 'The arguments "
		+ "dictionary is missing a value for the required parameter' and the tool body never runs, so there "
		+ "is no e2e for this path to write.")]
	public async Task EveryTool_ShouldRefuseANullArgumentObject() {
		// Arrange
		CreateBusinessProcessTool create = new(null, ConsoleLogger.Instance, _commandResolver);
		DescribeProcessTool describe = new(null, ConsoleLogger.Instance, _commandResolver);
		ModifyBusinessProcessTool modify = new(null, ConsoleLogger.Instance, _commandResolver);
		ModifyProcessAsNewVersionTool modifyAsNew = new(null, ConsoleLogger.Instance, _commandResolver);
		SetActiveProcessVersionTool setActive = new(null, ConsoleLogger.Instance, _commandResolver);
		GetProcessSignatureTool signature = new(null, ConsoleLogger.Instance, _commandResolver);
		RunProcessTool run = new(ConsoleLogger.Instance, _commandResolver);
		ListUserTasksTool listUserTasks = new(null, ConsoleLogger.Instance, _commandResolver);

		// Act
		CommandExecutionResult createResult = create.CreateBusinessProcess(null);
		CommandExecutionResult describeResult = describe.DescribeProcess(null);
		CommandExecutionResult modifyResult = modify.ModifyBusinessProcess(null);
		CommandExecutionResult modifyAsNewResult = modifyAsNew.ModifyProcessAsNewVersion(null);
		CommandExecutionResult setActiveResult = setActive.SetActiveProcessVersion(null);
		GetProcessSignatureResponse signatureResponse = signature.GetProcessSignature(null);
		RunProcessResponse runResponse = await run.RunProcess(null);
		CommandExecutionResult listUserTasksResult = listUserTasks.ListUserTasks(null);

		// Assert
		AssertNullRefused(createResult, CreateBusinessProcessTool.CreateBusinessProcessToolName);
		AssertNullRefused(describeResult, DescribeProcessTool.ToolName);
		AssertNullRefused(modifyResult, ModifyBusinessProcessTool.ModifyBusinessProcessToolName);
		AssertNullRefused(modifyAsNewResult, ModifyProcessAsNewVersionTool.ModifyProcessAsNewVersionToolName);
		AssertNullRefused(setActiveResult, SetActiveProcessVersionTool.SetActiveProcessVersionToolName);
		AssertNullRefused(listUserTasksResult, ListUserTasksTool.ListUserTasksToolName);
		signatureResponse.Error.Should().Contain("args is required",
			because: "get-process-signature is the second file Sonar flagged, and answers in its own shape");
		runResponse.Error.Should().Contain("args is required",
			because: "run-process is the third file Sonar flagged, and answers through its error field");
		_commandResolver.ReceivedCalls().Should().BeEmpty(
			because: "a call with no arguments cannot have earned an environment resolution");
	}
	[Test]
	[Category("Unit")]
	[Description("Review finding 13: describe-business-process was the only family member that never checked "
		+ "environment-name. The finding said a blank one fell through to the DEFAULT registered environment; "
		+ "measured, it does not - the resolver builds an empty EnvironmentSettings, finds no Uri and throws. "
		+ "What this guard buys is the family's own sentence instead of the resolver's generic one.")]
	public void DescribeProcess_ShouldRefuseABlankEnvironmentName() {
		// Arrange
		DescribeProcessTool tool = new(null, ConsoleLogger.Instance, _commandResolver);
		DescribeProcessArgs args = new(EnvironmentName: "   ", ProcessName: "UsrOrder_Handle");

		// Act
		CommandExecutionResult result = tool.DescribeProcess(args);

		// Assert
		result.ExitCode.Should().Be(1,
			because: "a blank required argument is caller-actionable, and the two guards above it in the same "
				+ "method already answer with exit 1");
		TextOf(result).Should().Contain("environment-name is required",
			because: "the caller must learn which argument was blank, not receive a graph from a stand they "
				+ "never named");
		_commandResolver.ReceivedCalls().Should().BeEmpty(
			because: "no environment may be resolved for a call that names none");
	}
	[Test]
	[Category("Unit")]
	[Description("Review finding 8: list-user-tasks belongs to the shipped process-designer family but sits "
		+ "outside the folder, so it had neither a bag nor a check. With a single declared argument the "
		+ "consequence was the quietest in the family - environmentName bound to nothing, EnvironmentName "
		+ "stayed null, and the call answered against the DEFAULT registered environment.")]
	public void ListUserTasks_ShouldRefuseAnUnknownArgument_AndNameIt() {
		// Arrange
		ListUserTasksTool tool = new(null, ConsoleLogger.Instance, _commandResolver);
		ListUserTasksArgs args = new(EnvName) { ExtensionData = Overflow() };

		// Act
		CommandExecutionResult result = tool.ListUserTasks(args);

		// Assert
		AssertRefused(result, ListUserTasksTool.ListUserTasksToolName);
	}
}
