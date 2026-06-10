using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Common.BrowserSession;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Story 8 (browser-session-handoff): the clear-browser-session MCP tool deletes the cached session
/// (idempotent, destructive flag), and fails closed with a structured error for Safe environments.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ClearBrowserSessionToolTests {

	private IToolCommandResolver _resolver = null!;
	private IBrowserSessionService _service = null!;
	private ClearBrowserSessionTool _sut = null!;
	private readonly EnvironmentSettings _env = new() { Uri = "https://dev.creatio.com", Login = "u", Password = "p" };

	[SetUp]
	public void SetUp() {
		_resolver = Substitute.For<IToolCommandResolver>();
		_service = Substitute.For<IBrowserSessionService>();
		_resolver.Resolve<IBrowserSessionService>(Arg.Any<EnvironmentOptions>()).Returns(_service);
		_resolver.Resolve<EnvironmentSettings>(Arg.Any<EnvironmentOptions>()).Returns(_env);
		_sut = new ClearBrowserSessionTool(_resolver);
	}

	[Test]
	[Description("Clears the cached session and returns success for a valid environment.")]
	public void ClearBrowserSession_ShouldClearAndSucceed_WhenEnvironmentIsValid() {
		// Act
		ClearBrowserSessionResult result = _sut.ClearBrowserSession(new ClearBrowserSessionArgs("MyEnv"));

		// Assert
		result.Success.Should().BeTrue("because clearing a session is a successful, idempotent operation");
		result.Error.Should().BeNull("because there was no error");
		_service.Received(1).ClearSessionAsync(_env, Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("A Safe-flagged non-interactive environment returns a structured error instead of hanging.")]
	public void ClearBrowserSession_ShouldReturnError_WhenSafeEnvironmentNonInteractive() {
		// Arrange
		_resolver.Resolve<IBrowserSessionService>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new SafeEnvironmentConfirmationRequiredException("https://prod.creatio.com"));

		// Act
		ClearBrowserSessionResult result = _sut.ClearBrowserSession(new ClearBrowserSessionArgs("Prod"));

		// Assert
		result.Success.Should().BeFalse("because a Safe env cannot be confirmed non-interactively");
		result.Error.Should().Contain("Safe environment confirmation required",
			"because the fail-closed reason is surfaced as a structured error");
	}

	[Test]
	[Description("The tool is declared destructive and idempotent (deletes a cached file; repeatable).")]
	public void ClearBrowserSession_ShouldDeclareDestructiveIdempotentSafetyFlags_WhenInspected() {
		// Act
		McpServerToolAttribute attribute = typeof(ClearBrowserSessionTool)
			.GetMethod(nameof(ClearBrowserSessionTool.ClearBrowserSession))!
			.GetCustomAttributes<McpServerToolAttribute>(false)
			.Single();

		// Assert
		attribute.Destructive.Should().BeTrue("because the tool deletes a cached session file");
		attribute.Idempotent.Should().BeTrue("because clearing an already-cleared session is a no-op");
		attribute.ReadOnly.Should().BeFalse("because the tool mutates on-disk state");
	}
}
