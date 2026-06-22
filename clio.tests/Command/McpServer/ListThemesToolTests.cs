using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class ListThemesToolTests {

	[Test]
	[Description("Resolves the environment-name list-themes MCP tool for the requested environment and returns the resolved command's themes as a structured success result.")]
	[Category("Unit")]
	public void ListThemesByEnvironment_Should_Resolve_Command_And_Return_Themes() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IReadOnlyList<ThemeDescriptor> themes = new List<ThemeDescriptor> {
			new() { Id = "ocean-theme", Caption = "Ocean", CssClassName = "ocean-theme", CssFilePath = "a/theme.css" }
		};
		FakeListThemesCommand defaultCommand = new();
		FakeListThemesCommand resolvedCommand = new(themes);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListThemesCommand>(Arg.Any<ListThemesOptions>()).Returns(resolvedCommand);
		ListThemesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListThemesResult result = tool.ListThemesByName("docker_fix2");

		// Assert
		result.Success.Should().BeTrue(because: "a successful catalog read must report success");
		result.Themes.Should().ContainSingle(because: "the single theme from the resolved command must be surfaced")
			.Which.Id.Should().Be("ocean-theme", because: "the descriptor fields must be mapped into the structured result");
		commandResolver.Received(1).Resolve<ListThemesCommand>(Arg.Is<ListThemesOptions>(options =>
			options.Environment == "docker_fix2" &&
			options.TimeOut == 30_000));
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command instance should have been queried for the themes");
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the environment-aware tool path should use the resolved command instance, not the injected one");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured failure without resolving a command when the environment name is empty.")]
	[Category("Unit")]
	public void ListThemesByEnvironment_Should_Return_Failure_When_Environment_Name_Is_Empty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeListThemesCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ListThemesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListThemesResult result = tool.ListThemesByName("   ");

		// Assert
		result.Success.Should().BeFalse(because: "an empty environment name is an invalid request and must not succeed");
		result.Error.Should().NotBeNullOrWhiteSpace(because: "the failure must carry a diagnostic message");
		commandResolver.DidNotReceive().Resolve<ListThemesCommand>(Arg.Any<ListThemesOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Surfaces the ThemeService failure message as a structured failure when the resolved command reports success=false.")]
	[Category("Unit")]
	public void ListThemesByEnvironment_Should_Return_Failure_When_Command_Reports_Failure() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeListThemesCommand defaultCommand = new();
		FakeListThemesCommand resolvedCommand = new(themes: null, success: false, error: "no permission");
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListThemesCommand>(Arg.Any<ListThemesOptions>()).Returns(resolvedCommand);
		ListThemesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListThemesResult result = tool.ListThemesByName("docker_fix2");

		// Assert
		result.Success.Should().BeFalse(because: "an explicit success=false read must surface as a tool failure");
		result.Error.Should().Contain("no permission",
			because: "the server-provided failure message must be forwarded to the caller");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Resolves the credentials list-themes MCP tool and preserves the default false value for isNetCore when the argument is omitted.")]
	[Category("Unit")]
	public void ListThemesByCredentials_Should_Use_Default_IsNetCore_When_Omitted() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeListThemesCommand defaultCommand = new();
		FakeListThemesCommand resolvedCommand = new(new List<ThemeDescriptor>());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListThemesCommand>(Arg.Any<ListThemesOptions>()).Returns(resolvedCommand);
		ListThemesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListThemesResult result = tool.ListThemesByCredentials(
			"http://localhost:5000",
			"Supervisor",
			"Supervisor");

		// Assert
		result.Success.Should().BeTrue(because: "the credentials tool should forward a valid list-themes command payload");
		result.Themes.Should().BeEmpty(because: "the resolved command returned an empty catalog");
		commandResolver.Received(1).Resolve<ListThemesCommand>(Arg.Is<ListThemesOptions>(options =>
			options.Uri == "http://localhost:5000" &&
			options.Login == "Supervisor" &&
			options.Password == "Supervisor" &&
			options.IsNetCore == false &&
			options.TimeOut == 30_000));
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Resolves the credentials list-themes MCP tool and preserves an explicit true value for isNetCore when the argument is provided.")]
	[Category("Unit")]
	public void ListThemesByCredentials_Should_Preserve_Explicit_IsNetCore_When_Provided() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeListThemesCommand defaultCommand = new();
		FakeListThemesCommand resolvedCommand = new(new List<ThemeDescriptor>());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ListThemesCommand>(Arg.Any<ListThemesOptions>()).Returns(resolvedCommand);
		ListThemesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListThemesResult result = tool.ListThemesByCredentials(
			"http://localhost:5000",
			"Supervisor",
			"Supervisor",
			isNetCore: true);

		// Assert
		result.Success.Should().BeTrue(because: "the credentials tool should forward a valid list-themes command payload when isNetCore is provided");
		commandResolver.Received(1).Resolve<ListThemesCommand>(Arg.Is<ListThemesOptions>(options =>
			options.Uri == "http://localhost:5000" &&
			options.IsNetCore == true &&
			options.TimeOut == 30_000));
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a structured failure carrying the missing-field message when a required credential argument is empty.")]
	[Category("Unit")]
	public void ListThemesByCredentials_Should_Return_Failure_When_Url_Is_Empty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeListThemesCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ListThemesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		ListThemesResult result = tool.ListThemesByCredentials("  ", "Supervisor", "Supervisor");

		// Assert
		result.Success.Should().BeFalse(because: "an empty url is an invalid request and must not resolve a command");
		result.Error.Should().Contain("url", because: "the failure must point at the missing field");
		commandResolver.DidNotReceive().Resolve<ListThemesCommand>(Arg.Any<ListThemesOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeListThemesCommand : ListThemesCommand {
		private readonly IReadOnlyList<ThemeDescriptor> _themes;
		private readonly bool _success;
		private readonly string _error;

		public ListThemesOptions CapturedOptions { get; private set; }

		public FakeListThemesCommand(IReadOnlyList<ThemeDescriptor> themes = null, bool success = true,
			string error = null)
			: base(
				Substitute.For<IApplicationClient>(),
				new EnvironmentSettings(),
				Substitute.For<IServiceUrlBuilder>()) {
			_themes = themes ?? Array.Empty<ThemeDescriptor>();
			_success = success;
			_error = error;
		}

		public override bool TryGetAvailableThemes(ListThemesOptions options,
			out IReadOnlyList<ThemeDescriptor> themes, out string errorMessage) {
			CapturedOptions = options;
			themes = _themes;
			errorMessage = _error;
			return _success;
		}
	}
}
