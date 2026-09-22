using System.Linq;
using System.Reflection;
using Clio.Command;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Prompts.ProcessDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Command.ProcessModel;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class DescribeProcessToolTests {

	[Test]
	[Category("Unit")]
	[Description("describe-business-process is a read-only, non-destructive, idempotent, closed-world MCP tool.")]
	public void DescribeProcess_ShouldCarryReadOnlySafetyFlags_WhenInspected() {
		// Arrange
		MethodInfo method = typeof(DescribeProcessTool).GetMethod(nameof(DescribeProcessTool.DescribeProcess));
		McpServerToolAttribute attribute = method!.GetCustomAttribute<McpServerToolAttribute>();

		// Assert
		attribute.Should().NotBeNull(because: "describe-business-process must be exposed as an MCP tool");
		attribute!.ReadOnly.Should().BeTrue(because: "describe-business-process only reads a process");
		attribute.Destructive.Should().BeFalse(because: "describe-business-process changes nothing");
		attribute.Idempotent.Should().BeTrue(because: "reading the same process yields the same description");
		attribute.OpenWorld.Should().BeFalse(because: "it reads a known environment, not an open world");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the environment-aware describe-business-process command and maps MCP arguments into command options.")]
	public void DescribeProcess_ShouldResolveCommandAndMapArguments_WhenInvoked() {
		// Arrange
		FakeDescribeProcessCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<DescribeProcessCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		DescribeProcessTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.DescribeProcess(new DescribeProcessArgs(
			EnvironmentName: "dev",
			ProcessName: "UsrProcess_493d4c9",
			ProcessUid: null,
			ProcessCaption: null,
			Culture: "uk-UA"));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a valid describe-business-process request executes the resolved environment-aware command");
		commandResolver.Received(1).Resolve<DescribeProcessCommand>(Arg.Is<EnvironmentOptions>(o => o.Environment == "dev"));
		resolvedCommand.CapturedOptions.Should().NotBeNull(because: "the resolved command receives mapped options");
		resolvedCommand.CapturedOptions!.ProcessName.Should().Be("UsrProcess_493d4c9",
			because: "the process name must be forwarded from MCP arguments");
		resolvedCommand.CapturedOptions.Culture.Should().Be("uk-UA",
			because: "the requested culture must be forwarded to the command");
		resolvedCommand.CapturedOptions.Environment.Should().Be("dev",
			because: "the environment name must be preserved for environment-aware resolution");
	}

	[Test]
	[Category("Unit")]
	[Description("Omitted optional culture falls back to the command default so MCP and CLI stay aligned.")]
	public void DescribeProcess_ShouldUseDefaultCulture_WhenCultureOmitted() {
		// Arrange
		FakeDescribeProcessCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<DescribeProcessCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		DescribeProcessTool tool = new(resolvedCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		tool.DescribeProcess(new DescribeProcessArgs("dev", null, null, "AI PoC Read Contact", null));

		// Assert
		resolvedCommand.CapturedOptions!.Culture.Should().Be("en-US",
			because: "omitting culture should use the command default");
		resolvedCommand.CapturedOptions.ProcessCaption.Should().Be("AI PoC Read Contact",
			because: "the caption identity must be forwarded");
	}

	[Test]
	[Category("Unit")]
	[Description("The prompt references the exact tool name and keeps the identity arguments visible.")]
	public void DescribeProcessPrompt_ShouldMentionToolNameAndArguments_WhenRendered() {
		// Act
		string prompt = DescribeProcessPrompt.DescribeProcessGuidance("UsrProcess_493d4c9", "dev");

		// Assert
		prompt.Should().Contain(DescribeProcessTool.ToolName, because: "the prompt references the production tool name");
		prompt.Should().Contain("process-name", because: "the prompt keeps the identity arguments visible");
		prompt.Should().Contain("process-modeling", because: "the prompt points callers at the narration guidance");
	}

	[Test]
	[Category("Unit")]
	[Description("The prompt's per-flow field list names EVERY field describe reports, and this pins it field by field because the prompt does NOT defer that list to guidance - step 2 sends the caller to get-guidance for the element catalog and connection-rule vocabulary only. An agent driven by this prompt is told what a flow carries and has no reason to look further, so a field added to the tool and not to the prompt is invisible to it. That is exactly what happened to `label`: the tool, the capability map, the unit tests and the e2e all gained it while the prompt still enumerated six fields, and nothing turned red because this fixture asserted only the tool name and the identity arguments. Add the next flow field here as well as to the prompt. It also pins the ABSENT-label wording in BOTH clio-owned describe channels - the tool [Description] and the prompt - because an agent reads one or the other, and the inverse version pin in BundledProcessBuilderPackageTests only forbids a NUMBER: deleting the sentence outright would make that test greener rather than redder.")]
	public void DescribeProcessPrompt_ShouldEnumerateEveryFlowFieldDescribeReports() {
		// Act
		string prompt = DescribeProcessPrompt.DescribeProcessGuidance("UsrProcess_493d4c9", "dev");

		// Assert - on the CONTIGUOUS enumeration, not on the bare words. Asserting Contain("label") was
		// useless: the prompt discusses labels in five other places, so deleting `label` from the flows
		// enumeration - the exact regression this test is named for - left it green. Same for "kind" and
		// "condition", which appear throughout the surrounding prose. Only branchesOnActivityResult was
		// really pinned. The fragment below is the enumeration itself, so a field removed from it fails here.
		prompt.Should().Contain("`flows` (name, source, target, kind, `label`, and on a branch its `condition`",
			because: "the per-flow field list is what an agent reads to learn a flow's shape, and this prompt "
				+ "does NOT defer that list to guidance - so a field missing from THIS enumeration is a field "
				+ "no agent looks for, whatever else the prose mentions");
		prompt.Should().Contain("`branchesOnActivityResult`, `results` and `resultsActivity`",
			because: "the enumeration continues on the next line and would otherwise fall outside the "
				+ "fragment above - and `results` is the field that says WHICH results decide the branch, so "
				+ "a reader who learns only the boolean can see THAT a selection exists and never read it");

		// The TOOL description carries the same disambiguation, and an agent may read either one. Asserted
		// on both because the inverse pin in BundledProcessBuilderPackageTests only forbids a VERSION -
		// deleting the sentence outright would make that test greener, not redder.
		string toolText = ((System.ComponentModel.DescriptionAttribute)typeof(DescribeProcessTool)
			.GetMethod(nameof(DescribeProcessTool.DescribeProcess))!
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false).Single()).Description;
		toolText.Should().Contain("PREDATES",
			because: "an agent reading the tool contract instead of the prompt must reach the same warning "
				+ "about what an absent label can mean");
		prompt.Should().Contain("PREDATES",
			because: "an absent label has two meanings - no label, or a package that cannot report one - and "
				+ "the prompt has to name the second one, or an agent reports the ambiguous read as a fact");
		prompt.Should().Contain("version NUMBER cannot tell those apart",
			because: "naming a version here would be worse than saying nothing: clio already refuses a "
				+ "package older than the one it ships, so a high number reads as evidence the member is "
				+ "present when it is no evidence at all");
	}

	[Test]
	[Category("Unit")]
	[Description("The version-family entry advertises packageName, and separates the two absences the reader really produces: ONE package that did not resolve is silent, and only a package read that failed outright - which loses every name at once - reaches versionReadWarning. The first draft of this sentence promised a warning for both, which the reader's own test pins the opposite of; an agent reading it treats silence as proof the names are complete.")]
	public void DescribeProcess_Description_ShouldAdvertisePackageNameAndScopeItsAbsence() {
		// Arrange
		MethodInfo method = typeof(DescribeProcessTool).GetMethod(nameof(DescribeProcessTool.DescribeProcess));

		// Act
		string description = ((System.ComponentModel.DescriptionAttribute)method!
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false).Single()).Description;

		// Assert
		description.Should().Contain("packageName",
			because: "the field is what a person asking which package a version lives in actually reads, and "
				+ "nothing else in CI notices when it stops being advertised");
		description.Should().Contain("that single absence is SILENT",
			because: "a lone unresolved name raises no warning, and a contract that promises one teaches the "
				+ "agent to read silence as completeness");
		description.Should().Contain("answered for NO member",
			because: "only the whole-table case is announced, and the two have different remedies - the phrase "
				+ "also has to cover a stall and an empty result, not just an outright refusal");
	}

	[Test]
	[Category("Unit")]
	[Description("The describe-business-process description states the version contract: which field reaches the running version, and that absent version fields mean unknown rather than unversioned.")]
	public void DescribeProcess_ShouldStateTheVersionContract_WhenItsDescriptionIsRead() {
		// Arrange
		MethodInfo method = typeof(DescribeProcessTool).GetMethod(nameof(DescribeProcessTool.DescribeProcess))!;

		// Act
		System.ComponentModel.DescriptionAttribute description = (System.ComponentModel.DescriptionAttribute)method
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false).Single();
		McpServerToolAttribute toolAttribute = (McpServerToolAttribute)method
			.GetCustomAttributes(typeof(McpServerToolAttribute), false).Single();

		// Assert
		description.Description.Should().Contain("activeVersionSchemaUId",
			because: "an agent told the graph is not the running one needs the field that addresses the one that is");
		description.Description.Should().Contain("versionReadWarning",
			because: "the description must name where the reason for absent version facts is reported");
		description.Description.Should().Contain("never 'unversioned'",
			because: "reading absent version fields as 'this process has no versions' reproduces the ENG-94374 defect with the fields in place");
		description.Description.Should().Contain("FAMILY state",
			because: "a family entry's enabled flag is per-family, and an agent that reads it per-version reports the wrong thing");
		toolAttribute.ReadOnly.Should().BeTrue(
			because: "the version read is an additional read on the describe path; it must not change the tool's classification");
	}

	[Test]
	[Category("Unit")]
	[Description("Cross-channel drift guard: the active-version invariant must appear in BOTH clio-owned describe channels — the tool [Description] and the describe prompt — so dropping it from either fails the build.")]
	public void ActiveVersionInvariant_Should_Appear_In_All_Clio_Channels() {
		// Arrange
		string toolDescription = ((System.ComponentModel.DescriptionAttribute)typeof(DescribeProcessTool)
			.GetMethod(nameof(DescribeProcessTool.DescribeProcess))!
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false).Single()).Description;

		// Act
		string prompt = DescribeProcessPrompt.DescribeProcessGuidance("UsrProcess_493d4c9", "dev");

		// Assert — the guidance article that repeats this lives in clio-knowledge and is guarded there, not here.
		toolDescription.Should().Contain("isActiveVersion",
			because: "the tool contract has to name the flag that decides whether the returned graph can be trusted");
		prompt.Should().Contain("isActiveVersion",
			because: "an agent following the prompt instead of the tool description must reach the same check");
		toolDescription.Should().Contain("activeVersionSchemaUId",
			because: "both channels have to point at the same field for reaching the running version");
		prompt.Should().Contain("activeVersionSchemaUId",
			because: "a prompt that says to check the flag but not how to act on it leaves the agent stuck");
		prompt.Should().Contain("BEFORE narrating",
			because: "the check is worthless after the answer is written, so the prompt has to order it first");
		toolDescription.Should().Contain("ALONGSIDE fields that WERE established",
			because: "a read can succeed and still not settle everything, and an agent that reads the warning as 'no version data' throws away facts it has");
		prompt.Should().Contain("nothing to redirect to",
			because: "the branch with no activeVersionSchemaUId is reachable, and without it the prompt tells the agent to redirect by a field that is not there");
	}

	[Test]
	[Category("Unit")]
	[Description("The describe tool contract documents the multiInstanceOptions read block and routes the "
		+ "caller to calleeInSync. inSync is FALSE BY CONSTRUCTION on a multi-instance element - it compares "
		+ "the callee against the element's ROOT parameters, which there are the five service ones - so an "
		+ "agent told only 'inSync is false' reports permanent drift on a healthy element. calleeInSync is "
		+ "the field that answers the question one level down, and it is a SEPARATE field precisely so that "
		+ "no deserializer, schema check or version negotiation has to notice a redefinition.")]
	public void DescribeProcess_ShouldDocumentMultiInstanceOptions_WhenToolContractIsRead() {
		// Arrange
		string toolText = ReadDescribeToolDescription();

		// Act
		// (nothing to act on - the contract is the attribute itself)

		// Assert
		toolText.Should().Contain("multiInstanceOptions",
			because: "the block is reported on every multi-instance element and an agent that cannot find "
				+ "its name here cannot ask for it");
		toolText.Should().Contain("calleeInSync",
			because: "it is the only field that answers whether the callee's contract is still carried, and "
				+ "inSync cannot answer it on a multi-instance element");
		toolText.Should().Contain("FALSE BY CONSTRUCTION",
			because: "an agent must be told WHY inSync is false there, or it reports a healthy element as "
				+ "drifted");
		foreach (string parameterName in new[] {
				"inputCollection", "outputCollection", "completedIterationsCount",
				"terminatedIterationsCount", "totalIterationsCount" }) {
			toolText.Should().Contain(parameterName,
				because: $"'{parameterName}' is one of the five names a converted element carries instead of "
					+ "the callee's parameters, and a mapping is written in terms of it");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("THE RETRACTION SWEEP. Three clio-owned agent-facing surfaces used to state that a "
		+ "multi-instance element cannot be built or re-synchronized. Both claims are now false - the "
		+ "element is buildable through subProcess.multiInstanceOptions and a re-synchronization is "
		+ "subProcess.resync:true - and a false sentence left beside a true one is worse than silence, "
		+ "because an agent that reads the stale half stops looking. This sweeps all three in one test so "
		+ "the correction cannot be applied to one surface and forgotten on the others.")]
	public void AgentFacingSurfaces_ShouldNotClaimMultiInstanceIsUnsupported_WhenSwept() {
		// Arrange
		string toolText = ReadDescribeToolDescription();
		string validatePrompt = ValidateProcessGraphPrompt.ProcessDesignGuidance();
		string validateTool = ((System.ComponentModel.DescriptionAttribute)typeof(ValidateProcessGraphTool)
			.GetMethod(nameof(ValidateProcessGraphTool.Validate))!
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false).Single()).Description;

		// Act
		// (the surfaces are the subject; nothing is invoked)

		// Assert
		toolText.Should().NotContain("re-sync is REFUSED",
			because: "a re-synchronization IS available on a multi-instance element - subProcess.resync:true "
				+ "de-converts, re-synchronizes and re-converts it, which is what the process designer does "
				+ "for the same edit");
		validatePrompt.Should().NotContain("neither is one that runs the called process once per item",
			because: "such an element is buildable now, and this prompt is what tells an agent which slice "
				+ "of the palette it may plan with");
		validatePrompt.Should().Contain("multiInstanceOptions",
			because: "removing the refusal is only half the correction - the prompt has to name the member "
				+ "that replaced it, or an agent learns only that its previous knowledge was wrong");
		validateTool.Should().NotContain("once per item of a collection.",
			because: "THIS description is the CANONICAL buildable-slice list - the prompt defers to it BY NAME "
				+ "and deliberately does not restate it - so a retraction applied to the prompt alone leaves "
				+ "the authoritative copy still claiming the element cannot be built. That is precisely what "
				+ "happened: the first version of this sweep named three surfaces, read two, and the one it "
				+ "skipped was this one");
		validateTool.Should().Contain("multiInstanceOptions",
			because: "the canonical list has to name the member that replaced the refusal, for the same reason "
				+ "the prompt does");
	}

	/// <summary>Reads the describe tool's own [Description] - the agent-facing contract under test.</summary>
	private static string ReadDescribeToolDescription() =>
		((System.ComponentModel.DescriptionAttribute)typeof(DescribeProcessTool)
			.GetMethod(nameof(DescribeProcessTool.DescribeProcess))!
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false).Single()).Description;

	private sealed class FakeDescribeProcessCommand : DescribeProcessCommand {
		public DescribeProcessOptions CapturedOptions { get; private set; }

		public FakeDescribeProcessCommand()
			: base(Substitute.For<IProcessDescriber>(), Substitute.For<ILogger>()) {
		}

		public override int Execute(DescribeProcessOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
