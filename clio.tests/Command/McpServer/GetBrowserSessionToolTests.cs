using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Common.BrowserSession;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Story 7 (browser-session-handoff): the get-browser-session MCP tool returns the session file
/// path (never cookie values), forwards force-refresh, exposes no output-path, and fails closed
/// (structured error, no hang) for Safe environments and auth failures.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class GetBrowserSessionToolTests {

	private IToolCommandResolver _resolver = null!;
	private IBrowserSessionService _service = null!;
	private GetBrowserSessionTool _sut = null!;
	private readonly EnvironmentSettings _env = new() { Uri = "https://dev.creatio.com", Login = "u", Password = "p" };

	[SetUp]
	public void SetUp() {
		_resolver = Substitute.For<IToolCommandResolver>();
		_service = Substitute.For<IBrowserSessionService>();
		_resolver.Resolve<IBrowserSessionService>(Arg.Any<EnvironmentOptions>()).Returns(_service);
		_resolver.Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>()).Returns(_env);
		_sut = new GetBrowserSessionTool(_resolver);
	}

	[Test]
	[Description("Returns success and the session file path; the response carries no cookie values.")]
	public void GetBrowserSession_ShouldReturnSessionFilePath_WhenEnvironmentIsValid() {
		// Arrange
		_service.GetSessionPathAsync(_env, null, Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult("/home/.clio/sessions/dev_abc.storageState.json"));

		// Act
		GetBrowserSessionResult result = _sut.GetBrowserSession(new GetBrowserSessionArgs("MyEnv"));

		// Assert
		result.Success.Should().BeTrue("because a valid environment yields a session");
		result.SessionFilePath.Should().Be("/home/.clio/sessions/dev_abc.storageState.json",
			"because the tool returns the storageState file path");
		result.Error.Should().BeNull("because there was no error");
	}

	[Test]
	[Description("Forwards force-refresh to the session service.")]
	public void GetBrowserSession_ShouldForwardForceRefresh_WhenRequested() {
		// Arrange
		_service.GetSessionPathAsync(_env, null, true, Arg.Any<CancellationToken>())
			.Returns(Task.FromResult("/path.json"));

		// Act
		GetBrowserSessionResult result = _sut.GetBrowserSession(new GetBrowserSessionArgs("MyEnv", ForceRefresh: true));

		// Assert
		result.Success.Should().BeTrue("because the refresh succeeded");
		_service.Received(1).GetSessionPathAsync(_env, null, true, Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("A Safe-flagged non-interactive environment returns a structured error instead of hanging.")]
	public void GetBrowserSession_ShouldReturnError_WhenSafeEnvironmentNonInteractive() {
		// Arrange
		_resolver.Resolve<IBrowserSessionService>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new SafeEnvironmentConfirmationRequiredException("https://prod.creatio.com"));

		// Act
		GetBrowserSessionResult result = _sut.GetBrowserSession(new GetBrowserSessionArgs("Prod"));

		// Assert
		result.Success.Should().BeFalse("because a Safe env cannot be confirmed in a non-interactive MCP context");
		result.Error.Should().Contain("Safe environment confirmation required",
			"because the structured error explains the fail-closed reason");
		result.SessionFilePath.Should().BeNull("because no session was produced");
	}

	[Test]
	[Description("An authentication failure is surfaced as a structured error with a sanitized message.")]
	public void GetBrowserSession_ShouldReturnError_WhenAuthenticationFails() {
		// Arrange
		_service.GetSessionPathAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.ThrowsAsync(CreatioAuthenticationException.InvalidCredentials("https://dev.creatio.com"));

		// Act
		GetBrowserSessionResult result = _sut.GetBrowserSession(new GetBrowserSessionArgs("MyEnv"));

		// Assert
		result.Success.Should().BeFalse("because authentication failed");
		result.Error.Should().Contain("check username and password", "because the sanitized auth message is surfaced");
	}

	[Test]
	[Description("The MCP argument surface exposes only environment-name and force-refresh — never output-path (CLI-only).")]
	public void GetBrowserSessionArgs_ShouldNotExposeOutputPath_WhenInspected() {
		// Act
		string[] propertyNames = typeof(GetBrowserSessionArgs).GetProperties().Select(p => p.Name).ToArray();

		// Assert
		propertyNames.Should().Contain("EnvironmentName", "because the environment is the required argument");
		propertyNames.Should().Contain("ForceRefresh", "because force-refresh is the only optional knob");
		propertyNames.Should().NotContain("OutputPath",
			"because a bearer-token destination must not be agent-controllable — output-path is CLI-only");
	}
}
