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
	[Description("Resolves the environment-name clear-redis MCP tool for the requested environment and forwards the environment key into command options.")]
	[Category("Unit")]
	public void ClearRedisByEnvironment_Should_Resolve_Command_For_Requested_Environment() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeRedisCommand defaultCommand = new();
		FakeRedisCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<RedisCommand>(Arg.Any<ClearRedisOptions>()).Returns(resolvedCommand);
		ClearRedisTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ClearRedisByName("docker_fix2");

		// Assert
		result.ExitCode.Should().Be(0, because: "the environment-name tool should forward a valid clear-redis command payload");
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
	[Description("Resolves the credentials clear-redis MCP tool and preserves the default false value for isNetCore when the argument is omitted.")]
	[Category("Unit")]
	public void ClearRedisByCredentials_Should_Use_Default_IsNetCore_When_Omitted() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeRedisCommand defaultCommand = new();
		FakeRedisCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<RedisCommand>(Arg.Any<ClearRedisOptions>()).Returns(resolvedCommand);
		ClearRedisTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ClearRedisByCredentials(
			"http://localhost:5000",
			"Supervisor",
			"Supervisor");

		// Assert
		result.ExitCode.Should().Be(0, because: "the credentials tool should forward a valid clear-redis command payload");
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
			because: "the MCP tool contract defines false as the default when isNetCore is omitted");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Resolves the credentials clear-redis MCP tool and preserves an explicit true value for isNetCore when the argument is provided.")]
	[Category("Unit")]
	public void ClearRedisByCredentials_Should_Preserve_Explicit_IsNetCore_When_Provided() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeRedisCommand defaultCommand = new();
		FakeRedisCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<RedisCommand>(Arg.Any<ClearRedisOptions>()).Returns(resolvedCommand);
		ClearRedisTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ClearRedisByCredentials(
			"http://localhost:5000",
			"Supervisor",
			"Supervisor",
			isNetCore: true);

		// Assert
		result.ExitCode.Should().Be(0, because: "the credentials tool should forward a valid clear-redis command payload when isNetCore is provided");
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
