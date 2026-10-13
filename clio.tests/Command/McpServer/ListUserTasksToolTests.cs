using System.Reflection;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class ListUserTasksToolTests {

	[Test]
	[Description("Resolves the list-user-tasks MCP tool for the requested environment and forwards the environment key into command options.")]
	[Category("Unit")]
	public void ListUserTasks_Should_Resolve_Command_For_Requested_Environment() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeListUserTasksCommand defaultCommand = new();
		FakeListUserTasksCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListUserTasksCommand>(Arg.Any<ListUserTasksOptions>()).Returns(resolvedCommand);
		ListUserTasksTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ListUserTasks(new ListUserTasksArgs("docker_fix2"));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the list-user-tasks tool should forward a valid command payload for the requested environment");
		commandResolver.Received(1).Resolve<ListUserTasksCommand>(Arg.Is<ListUserTasksOptions>(options =>
			options.Environment == "docker_fix2"));
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the environment-aware tool path should use the resolved command instance, not the startup one");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the forwarded list-user-tasks options");
		resolvedCommand.CapturedOptions!.Environment.Should().Be("docker_fix2",
			because: "the requested environment key must be preserved");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a failed result without resolving any command when the environment name is empty.")]
	[Category("Unit")]
	public void ListUserTasks_Should_Fail_When_Environment_Is_Empty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeListUserTasksCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ListUserTasksTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ListUserTasks(new ListUserTasksArgs("   "));

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "an empty environment name is a validation error that must not reach command resolution");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<ListUserTasksCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("list-user-tasks tells the caller to pass a task name on a generic userTask, so its description must carve out PreconfiguredPageUserTask - that route is refused, and a package that does not refuse it builds a page-less element.")]
	[Category("Unit")]
	public void ListUserTasks_Description_ShouldRoutePreconfiguredPageToItsDedicatedType() {
		// Arrange
		string description = typeof(ListUserTasksTool).GetMethod(nameof(ListUserTasksTool.ListUserTasks))!
			.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;

		// Act & Assert
		description.Should().Contain("PreconfiguredPageUserTask (Pre-configured page) is built ONLY as type preconfiguredPage",
			because: "the description names the generic userTask route as the default, so the one task it is refused for must be named");
		description.Should().Contain("the generic route cannot carry that block and is REFUSED",
			because: "the caller must learn that the generic route fails, not that it is merely second best - "
				+ "and a bare word would stay green if another clause said REFUSED while this one was removed");
		description.Should().Contain("for EmailTemplateUserTask and ApprovalUserTask only, fall back to a generic userTask named after the schema",
			because: "the older-package fallback is named positively, for the two tasks whose block a generic userTask carries");
		description.Should().Contain("a generic PreconfiguredPageUserTask or OpenEditPageUserTask has no page and fails at run time",
			because: "the fallback must not send the caller to a route that builds a page-less element");
	}

	[Test]
	[Description("list-user-tasks routes OpenEditPageUserTask to type openEditPage. Its block is refused on a generic userTask, so that route can only build an element with no page, which fails at run time (measured: ItemNotFoundException in OpenEditPageUserTask) - and the element catalog already lists Open edit page among the dedicated types.")]
	[Category("Unit")]
	public void ListUserTasks_Description_ShouldRouteOpenEditPageToItsDedicatedType() {
		// Arrange
		string description = typeof(ListUserTasksTool).GetMethod(nameof(ListUserTasksTool.ListUserTasks))!
			.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;

		// Act & Assert
		description.Should().Contain("OpenEditPageUserTask (Open edit page) is built as type openEditPage",
			because: "the description sends every other task name down the generic userTask route, which cannot carry this task's block");
		description.Should().Contain("generic route builds a page-less element that fails at run time",
			because: "the caller has to learn why the generic route is wrong for this task, not only that another type exists");
	}

	[Test]
	[Description("The list-user-tasks prompt tells the caller to use a returned name on a generic userTask, so it must carve out the two page tasks whose block a generic userTask cannot carry - the prompt has to match its tool's contract.")]
	[Category("Unit")]
	public void ListUserTasksPrompt_ShouldCarveOutThePageTasks() {
		// Arrange
		const string environmentName = "sandbox";

		// Act
		string prompt = Clio.Command.McpServer.Prompts.ListUserTasksPrompt.PromptByEnvironmentName(environmentName);

		// Assert
		prompt.Should().Contain("except `PreconfiguredPageUserTask` and `OpenEditPageUserTask`",
			because: "the prompt otherwise sends both page tasks down a generic route that builds a page-less element");
	}

	[Test]
	[Description("list-user-tasks tells the caller that AutoGeneratedPageUserTask and UserQuestionUserTask are refused: CrtProcessBuilder has no block that configures either, so the server refuses them at create (ENG-102537) - measured on a stand, an unconfigured Auto-generated page opens empty and an unconfigured User dialog cannot be saved, so the process waits forever.")]
	[Category("Unit")]
	public void ListUserTasks_Description_ShouldNameTheRefusedPageTasks() {
		// Arrange
		string description = typeof(ListUserTasksTool).GetMethod(nameof(ListUserTasksTool.ListUserTasks))!
			.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;

		// Act & Assert
		description.Should().Contain("AutoGeneratedPageUserTask (Auto-generated page) and UserQuestionUserTask (User "
				+ "dialog) are REFUSED on every route",
			because: "the description sends task names down the generic route by default, so the two the server refuses must be named");
		description.Should().Contain("use type openEditPage, preconfiguredPage or performTask instead",
			because: "a refusal without the alternative leaves the caller guessing which element does the job");
	}

	[Test]
	[Description("The list-user-tasks prompt carves out the two refused page tasks the same way its tool description does - the prompt has to match its tool's contract.")]
	[Category("Unit")]
	public void ListUserTasksPrompt_ShouldCarveOutTheRefusedTasks() {
		// Arrange
		const string environmentName = "sandbox";

		// Act
		string prompt = Clio.Command.McpServer.Prompts.ListUserTasksPrompt.PromptByEnvironmentName(environmentName);

		// Assert
		prompt.Should().Contain("except `AutoGeneratedPageUserTask` and `UserQuestionUserTask`, which are refused",
			because: "the prompt otherwise sends a caller to build a task the server refuses");
	}

	private sealed class FakeListUserTasksCommand : ListUserTasksCommand {
		public ListUserTasksOptions? CapturedOptions { get; private set; }

		public FakeListUserTasksCommand()
			: base(Substitute.For<IListUserTasksService>(), Substitute.For<ILogger>()) {
		}

		public override int Execute(ListUserTasksOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
