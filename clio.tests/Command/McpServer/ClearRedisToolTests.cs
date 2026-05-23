using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class ClearRedisToolTests {

	[Test]
	[Description("Resolves the consolidated clear-redis-db MCP tool in environment mode and forwards the environment key into command options.")]
	[Category("Unit")]
	public void ClearRedis_EnvironmentMode_Should_Resolve_Command_For_Requested_Environment() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeRedisCommand defaultCommand = new();
		FakeRedisCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<RedisCommand>(Arg.Any<ClearRedisOptions>()).Returns(resolvedCommand);
		ClearRedisTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ClearRedis(new ClearRedisDbRunArgs(
			Mode: ClearRedisTool.ModeEnvironment,
			EnvironmentName: "docker_fix2"));

		// Assert
		result.ExitCode.Should().Be(0, because: "the environment mode should forward a valid clear-redis command payload");
		commandResolver.Received(1).Resolve<RedisCommand>(Arg.Is<ClearRedisOptions>(options =>
			options.Environment == "docker_fix2" &&
			options.TimeOut == 30_000));
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the environment-aware tool path should use the resolved command instance");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the forwarded clear-redis options");
		resolvedCommand.CapturedOptions!.Environment.Should().Be("docker_fix2",
			because: "the requested environment key must be preserved");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Resolves the consolidated clear-redis-db MCP tool in credentials mode and preserves the default false value for is-net-core when the argument is omitted.")]
	[Category("Unit")]
	public void ClearRedis_CredentialsMode_Should_Use_Default_IsNetCore_When_Omitted() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeRedisCommand defaultCommand = new();
		FakeRedisCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<RedisCommand>(Arg.Any<ClearRedisOptions>()).Returns(resolvedCommand);
		ClearRedisTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ClearRedis(new ClearRedisDbRunArgs(
			Mode: ClearRedisTool.ModeCredentials,
			Url: "http://localhost:5000",
			Login: "Supervisor",
			Password: "Supervisor"));

		// Assert
		result.ExitCode.Should().Be(0, because: "the credentials mode should forward a valid clear-redis command payload");
		commandResolver.Received(1).Resolve<RedisCommand>(Arg.Is<ClearRedisOptions>(options =>
			options.Uri == "http://localhost:5000" &&
			options.Login == "Supervisor" &&
			options.Password == "Supervisor" &&
			options.IsNetCore == false &&
			options.TimeOut == 30_000));
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the environment-sensitive tool should use the resolved command instance for credentials-based requests");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the forwarded credentials payload");
		resolvedCommand.CapturedOptions!.IsNetCore.Should().BeFalse(
			because: "the MCP tool contract defines false as the default when is-net-core is omitted");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Resolves the consolidated clear-redis-db MCP tool in credentials mode and preserves an explicit true value for is-net-core when the argument is provided.")]
	[Category("Unit")]
	public void ClearRedis_CredentialsMode_Should_Preserve_Explicit_IsNetCore_When_Provided() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeRedisCommand defaultCommand = new();
		FakeRedisCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<RedisCommand>(Arg.Any<ClearRedisOptions>()).Returns(resolvedCommand);
		ClearRedisTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ClearRedis(new ClearRedisDbRunArgs(
			Mode: ClearRedisTool.ModeCredentials,
			Url: "http://localhost:5000",
			Login: "Supervisor",
			Password: "Supervisor",
			IsNetCore: true));

		// Assert
		result.ExitCode.Should().Be(0, because: "the credentials mode should forward a valid clear-redis command payload when is-net-core is provided");
		commandResolver.Received(1).Resolve<RedisCommand>(Arg.Is<ClearRedisOptions>(options =>
			options.Uri == "http://localhost:5000" &&
			options.Login == "Supervisor" &&
			options.Password == "Supervisor" &&
			options.IsNetCore == true &&
			options.TimeOut == 30_000));
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the environment-sensitive tool should use the resolved command instance for credentials-based requests");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the forwarded credentials payload");
		resolvedCommand.CapturedOptions!.IsNetCore.Should().BeTrue(
			because: "the MCP tool contract should preserve explicit optional argument values");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Rejects an unknown mode value with a clear error listing allowed modes.")]
	[Category("Unit")]
	public void ClearRedis_Should_Reject_Invalid_Mode() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeRedisCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ClearRedisTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ClearRedis(new ClearRedisDbRunArgs(
			Mode: "bogus",
			EnvironmentName: "dev"));

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "an unknown mode discriminator should be rejected before any command is invoked");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<RedisCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Advertises the stable consolidated tool name 'clear-redis-db' and the two supported mode discriminator values.")]
	[Category("Unit")]
	public void ClearRedis_Should_Advertise_Stable_Contract() {
		// Arrange / Act
		string toolName = ClearRedisTool.ClearRedisToolName;
		string[] modes = [ClearRedisTool.ModeEnvironment, ClearRedisTool.ModeCredentials];

		// Assert
		toolName.Should().Be("clear-redis-db",
			because: "the MCP contract identifier must stay stable after consolidation");
		modes.Should().BeEquivalentTo(["environment", "credentials"],
			because: "the two supported mode discriminator values must remain stable");
	}

	private sealed class FakeRedisCommand : RedisCommand {
		public ClearRedisOptions? CapturedOptions { get; private set; }

		public FakeRedisCommand()
			: base(
				Substitute.For<IApplicationClient>(),
				new EnvironmentSettings(),
				Substitute.For<IServiceUrlBuilder>()) {
		}

		public override int Execute(ClearRedisOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
