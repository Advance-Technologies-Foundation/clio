using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class DescribeDynamicCaseToolTests {

	private FakeCommand _resolved;
	private IToolCommandResolver _resolver;
	private DescribeDynamicCaseTool _tool;

	[SetUp]
	public void Setup() {
		ConsoleLogger.Instance.ClearMessages();
		_resolved = new FakeCommand();
		_resolver = Substitute.For<IToolCommandResolver>();
		_resolver.Resolve<DescribeDynamicCaseCommand>(Arg.Any<DescribeDynamicCaseOptions>()).Returns(_resolved);
		_tool = new DescribeDynamicCaseTool(new FakeCommand(), ConsoleLogger.Instance, _resolver);
	}

	[TearDown]
	public void TearDown() {
		_resolver.ClearReceivedCalls();
		ConsoleLogger.Instance.ClearMessages();
	}

	private static DescribeDynamicCaseArgs Args(string json) => JsonSerializer.Deserialize<DescribeDynamicCaseArgs>(json)!;

	private static string TextOf(CommandExecutionResult result) =>
		string.Join(" | ", result.Output.Select(message => message.Value?.ToString()));

	private static MethodInfo Method() =>
		typeof(DescribeDynamicCaseTool).GetMethod(nameof(DescribeDynamicCaseTool.DescribeDynamicCase))!;

	[Test]
	[Category("Unit")]
	[Description("The tool is a read-only, non-destructive, idempotent, closed-world read named describe-dynamic-case.")]
	public void DescribeDynamicCase_ShouldDeclareReadOnlyFlags() {
		// Arrange
		MethodInfo method = Method();

		// Act
		McpServerToolAttribute attribute = method.GetCustomAttribute<McpServerToolAttribute>()!;

		// Assert
		attribute.Name.Should().Be("describe-dynamic-case", because: "the guidance and agents spell this name");
		attribute.ReadOnly.Should().BeTrue(because: "it changes nothing");
		attribute.Destructive.Should().BeFalse(because: "a read cannot delete or overwrite anything");
		attribute.Idempotent.Should().BeTrue(because: "reading twice answers the same");
		attribute.OpenWorld.Should().BeFalse(because: "it talks to one registered environment");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool runs in a per-call worker like the other process-designer tools.")]
	public void DescribeDynamicCase_ShouldRunInAPerCallWorker() {
		// Arrange
		MethodInfo method = Method();

		// Act
		McpToolExecutionAttribute execution = method.GetCustomAttribute<McpToolExecutionAttribute>()!;

		// Assert
		execution.Location.Should().Be(McpToolExecutionLocation.Worker, because: "the design places it in a worker");
		execution.Lifetime.Should().Be(McpToolExecutionLifetime.PerCall, because: "each call gets its own worker");
	}

	[TestCase("{\"environment-name\":\"dev\",\"case-name\":\" UsrOrderCase \"}", "UsrOrderCase", null, null)]
	[TestCase("{\"environment-name\":\"dev\",\"case-uid\":\"5c58c4c4-134b-4744-9c67-96d9c69c9d55\"}", null,
		"5c58c4c4-134b-4744-9c67-96d9c69c9d55", null)]
	[TestCase("{\"environment-name\":\"dev\",\"entity-schema-name\":\"UsrOrder\"}", null, null, "UsrOrder")]
	[Category("Unit")]
	[Description("Each identity - the case name, the case UId or the object - reaches the RESOLVED command trimmed, with the environment.")]
	public void DescribeDynamicCase_ShouldMapTheIdentity_ToTheResolvedCommand(string json, string caseName,
		string caseUId, string entity) {
		// Arrange
		DescribeDynamicCaseArgs args = Args(json);

		// Act
		CommandExecutionResult result = _tool.DescribeDynamicCase(args);

		// Assert
		result.ExitCode.Should().Be(0, because: "one identity is enough");
		_resolved.Captured!.Environment.Should().Be("dev", because: "the environment is forwarded");
		_resolved.Captured.CaseName.Should().Be(caseName, because: "the case name is forwarded trimmed");
		_resolved.Captured.CaseUId.Should().Be(caseUId, because: "the case UId is forwarded");
		_resolved.Captured.EntitySchemaName.Should().Be(entity, because: "the object is forwarded");
	}

	[Test]
	[Category("Unit")]
	[Description("A call that names neither the case nor the object is refused before the command runs.")]
	public void DescribeDynamicCase_ShouldRefuse_WhenNoIdentityIsNamed() {
		// Arrange
		DescribeDynamicCaseArgs args = Args("{\"environment-name\":\"dev\"}");

		// Act
		CommandExecutionResult result = _tool.DescribeDynamicCase(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "there is nothing to read");
		TextOf(result).Should().Be(DescribeDynamicCaseTool.MissingTargetError, because: "the refusal names the three ways");
		_resolver.DidNotReceiveWithAnyArgs().Resolve<DescribeDynamicCaseCommand>(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("A missing environment keeps the -1 refusal of its siblings.")]
	public void DescribeDynamicCase_ShouldRefuse_WhenTheEnvironmentIsMissing() {
		// Arrange
		DescribeDynamicCaseArgs args = Args("{\"entity-schema-name\":\"UsrOrder\"}");

		// Act
		CommandExecutionResult result = _tool.DescribeDynamicCase(args);

		// Assert
		result.ExitCode.Should().Be(-1, because: "a missing environment answers like the sibling tools");
		TextOf(result).Should().StartWith("environment-name is required", because: "the missing argument is named");
	}

	[Test]
	[Category("Unit")]
	[Description("An undeclared top-level key is refused through the overflow bag, naming the valid keys.")]
	public void DescribeDynamicCase_ShouldRefuseAnUnknownArgument() {
		// Arrange
		DescribeDynamicCaseArgs args = Args("{\"environment-name\":\"dev\",\"entity-schema-name\":\"UsrOrder\","
			+ "\"stages\":[]}");

		// Act
		CommandExecutionResult result = _tool.DescribeDynamicCase(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "an unknown key is a caller error");
		TextOf(result).Should().Contain("'stages'").And.Contain(DescribeDynamicCaseTool.ValidArgsHint,
			because: "the key and the valid set are named");
		_resolver.DidNotReceiveWithAnyArgs().Resolve<DescribeDynamicCaseCommand>(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("A call with no argument object is refused with the valid keys.")]
	public void DescribeDynamicCase_ShouldRefuseNullArgs() {
		// Arrange & Act
		CommandExecutionResult result = _tool.DescribeDynamicCase(null!);

		// Assert
		result.ExitCode.Should().Be(1, because: "there is nothing to run");
		TextOf(result).Should().Be(DescribeDynamicCaseTool.NullArgsError, because: "the refusal is the pinned one");
	}

	[Test]
	[Category("Unit")]
	[Description("The description is ASCII, its first sentence fits the compact index, and it tells the agent to read the case before answering about it or changing it.")]
	public void Description_ShouldBeAscii_AndSendTheAgentHereFirst() {
		// Arrange
		string description = Method().GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;

		// Act
		string firstSentence = description[..(description.IndexOf(". ", StringComparison.Ordinal) + 1)];

		// Assert
		description.All(character => character < 128).Should().BeTrue(because: "tool descriptions are ASCII only");
		firstSentence.Length.Should().BeLessThanOrEqualTo(120, because: "the compact index cuts the purpose at 120");
		description.Should().Contain("change nothing", because: "the read-only promise is stated");
		description.Should().Contain("before answering any question about an existing case",
			because: "an agent must read the stored case instead of guessing it");
		description.Should().Contain("modify-dynamic-case", because: "the read comes before a change");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool carries no [FeatureToggle]: it ships enabled like the other process-designer tools.")]
	public void DescribeDynamicCase_ShouldNotBeFeatureGated() {
		// Arrange
		Type toolType = typeof(DescribeDynamicCaseTool);

		// Act
		object[] toggles = toolType.GetCustomAttributes(typeof(FeatureToggleAttribute), inherit: true);

		// Assert
		toggles.Should().BeEmpty(because: "a gated tool is silently unreachable on a default install");
	}

	private sealed class FakeCommand(int exitCode = 0)
		: DescribeDynamicCaseCommand(Substitute.For<IDynamicCaseService>(), Substitute.For<ILogger>()) {

		public DescribeDynamicCaseOptions Captured { get; private set; }

		public override int Execute(DescribeDynamicCaseOptions options) {
			Captured = options;
			return exitCode;
		}
	}
}
