using System.Reflection;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit coverage for the <c>reset-navigation-cache</c> MCP tool: its safety flags and description, the
/// environment-scoped execution, the failure envelope and the passthrough handling of environment-name.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public class ResetNavigationCacheToolTests {

	private const string EnvironmentName = "dev";
	private const string BrowserSessionNote = "run GetData(true) in the open tab, then reload it";

	[Test]
	[Description("Publishes reset-navigation-cache under the CLI verb name as a non-destructive, idempotent write.")]
	public void ResetNavigationCacheTool_ShouldDeclareNonDestructiveIdempotentFlags() {
		// Arrange & Act
		McpServerToolAttribute attribute = typeof(ResetNavigationCacheTool)
			.GetMethod(nameof(ResetNavigationCacheTool.ResetNavigationCache))!
			.GetCustomAttribute<McpServerToolAttribute>()!;

		// Assert
		attribute.Name.Should().Be("reset-navigation-cache", because: "the tool name matches the CLI verb");
		attribute.ReadOnly.Should().BeFalse(because: "the call drops and rebuilds server-side cache entries");
		attribute.Destructive.Should().BeFalse(because: "it changes no data and logs nobody out");
		attribute.Idempotent.Should().BeTrue(because: "a repeated reset leaves the same cache state");
	}

	[Test]
	[Description("States in the description that only clio's own session is cleared, that browser tabs need the in-tab call, and that the tool never replaces clear-redis-db.")]
	public void ResetNavigationCacheTool_ShouldDescribeItsScopeAndLimits() {
		// Arrange & Act
		string description = typeof(ResetNavigationCacheTool)
			.GetMethod(nameof(ResetNavigationCacheTool.ResetNavigationCache))!
			.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;

		// Assert
		description.Should().Contain("clio's own Creatio session",
			because: "the agent must know which session the reset reaches");
		description.Should().Contain("does NOT refresh other sessions or open browser tabs",
			because: "the agent must not promise the user a refreshed browser tab");
		description.Should().Contain("next-step", because: "the in-tab call is how a browser tab is refreshed");
		description.Should().Contain("SysModule / SysModuleInWorkplace",
			because: "direct writes to those tables are the case the tool exists for");
		description.Should().Contain("never a substitute for clear-redis-db",
			because: "clearing Redis logs out every user");
	}

	[Test]
	[Description("Runs the reset in the environment-scoped command and returns its success envelope with the note.")]
	public void ResetNavigationCache_ShouldReturnTheResolvedCommandResult() {
		// Arrange
		FakeResetNavigationCacheCommand resolvedCommand = new(new ResetNavigationCacheResponse {
			Success = true, NextStep = BrowserSessionNote
		});
		ResetNavigationCacheTool tool = CreateTool(resolvedCommand);

		// Act
		ResetNavigationCacheResponse response =
			tool.ResetNavigationCache(new ResetNavigationCacheArgs { EnvironmentName = EnvironmentName });

		// Assert
		response.Success.Should().BeTrue(because: "the resolved command reported a confirmed reset");
		response.NextStep.Should().Be(BrowserSessionNote, because: "the browser-session note reaches the agent");
		resolvedCommand.CapturedOptions!.Environment.Should().Be(EnvironmentName,
			because: "the reset runs in the session of the addressed environment");
	}

	[Test]
	[Description("A failed reset is returned as success=false with its message and the note; a host in the message is redacted.")]
	public void ResetNavigationCache_ShouldReturnFailureWithMessageAndNote_WhenResetFails() {
		// Arrange
		ResetNavigationCacheTool tool = CreateTool(new FakeResetNavigationCacheCommand(new ResetNavigationCacheResponse {
			Success = false,
			Error = "navigation cache reset failed: connection to http://creatio.local:8080/0/rest refused",
			NextStep = BrowserSessionNote
		}));

		// Act
		ResetNavigationCacheResponse response =
			tool.ResetNavigationCache(new ResetNavigationCacheArgs { EnvironmentName = EnvironmentName });

		// Assert
		response.Success.Should().BeFalse(because: "the server did not confirm the reset");
		response.Error.Should().StartWith("navigation cache reset failed",
			because: "the agent must see that the reset failed");
		response.Error.Should().NotContain("creatio.local", because: "the host must not cross the MCP boundary");
		response.NextStep.Should().Be(BrowserSessionNote,
			because: "a browser tab can still be refreshed from inside the tab when clio's own reset failed");
	}

	[Test]
	[Description("Fails with a structured error naming environment-name when it is missing and no passthrough context is active.")]
	public void ResetNavigationCache_ShouldFail_WhenEnvironmentNameIsMissing() {
		// Arrange
		FakeResetNavigationCacheCommand resolvedCommand = new(new ResetNavigationCacheResponse { Success = true });
		ResetNavigationCacheTool tool = CreateTool(resolvedCommand);

		// Act
		ResetNavigationCacheResponse response = tool.ResetNavigationCache(new ResetNavigationCacheArgs());

		// Assert
		response.Success.Should().BeFalse(because: "without passthrough the environment can only come from the argument");
		response.Error.Should().Contain("environment-name", because: "the message names the missing argument");
		resolvedCommand.CapturedOptions.Should().BeNull(because: "nothing is sent without a target environment");
	}

	[Test]
	[Description("Does not require environment-name under active credential passthrough, which supplies the environment itself.")]
	public void ResetNavigationCache_ShouldNotRequireEnvironmentName_WhenPassthroughIsActive() {
		// Arrange
		FakeResetNavigationCacheCommand resolvedCommand = new(new ResetNavigationCacheResponse {
			Success = true, NextStep = BrowserSessionNote
		});
		ICredentialPassthroughToolGuard passthroughGuard = Substitute.For<ICredentialPassthroughToolGuard>();
		passthroughGuard.IsPassthroughActive.Returns(true);
		ResetNavigationCacheTool tool = CreateTool(resolvedCommand, passthroughGuard);

		// Act
		ResetNavigationCacheResponse response = tool.ResetNavigationCache(new ResetNavigationCacheArgs());

		// Assert
		response.Success.Should().BeTrue(
			because: "an omitted environment-name under passthrough must reach the resolver, which reads the tenant from the header");
	}

	private static ResetNavigationCacheTool CreateTool(FakeResetNavigationCacheCommand resolvedCommand,
		ICredentialPassthroughToolGuard? passthroughGuard = null) {
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ResetNavigationCacheCommand>(Arg.Any<ResetNavigationCacheOptions>())
			.Returns(resolvedCommand);
		return new ResetNavigationCacheTool(resolvedCommand, ConsoleLogger.Instance, commandResolver, passthroughGuard);
	}

	private sealed class FakeResetNavigationCacheCommand(ResetNavigationCacheResponse response)
		: ResetNavigationCacheCommand(Substitute.For<IApplicationClient>(), new EnvironmentSettings(),
			Substitute.For<INavigationCacheResetter>()) {

		public ResetNavigationCacheOptions? CapturedOptions { get; private set; }

		public override ResetNavigationCacheResponse Reset(ResetNavigationCacheOptions options) {
			CapturedOptions = options;
			return response;
		}
	}
}
