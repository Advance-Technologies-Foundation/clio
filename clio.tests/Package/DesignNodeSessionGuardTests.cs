namespace Clio.Tests.Package;

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Package;
using Creatio.Client;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
internal sealed class DesignNodeSessionGuardTests {

	#region Constants: Private

	private const string SessionStartUrl = "session-start-url";
	private const string DesignProbeUrl = "design-probe-url";

	#endregion

	#region Fields: Private

	private ICreatioApplicationClient _session;
	private ILogger _logger;
	private EnvironmentSettings _environmentSettings;
	private DesignNodeSessionGuard _guard;

	#endregion

	#region Methods: Private

	private static CreatioSessionCookie Cookie(string name) =>
		new(name, string.Empty, "127.0.0.1", "/", true, false, "None", DateTime.MinValue);

	private void ArrangeCookies(params CreatioSessionCookie[] cookies) =>
		_session.ExportSessionCookies().Returns(cookies);

	private void ArrangeDesignProbe(HttpStatusCode status, string body = null) =>
		_session.ExecuteGetRequestAsync(DesignProbeUrl, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromResult(new HttpResponseMessage(status) {
				Content = new StringContent(body ?? string.Empty)
			}));

	private int SessionCallsTo(string methodName) =>
		_session.ReceivedCalls().Count(call => call.GetMethodInfo().Name == methodName);

	private string[] LoggedWith(string methodName) =>
		_logger.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == methodName)
			.Select(call => (string)call.GetArguments()[0])
			.ToArray();

	#endregion

	#region Methods: Public

	[SetUp]
	public void SetUp() {
		_session = Substitute.For<ICreatioApplicationClient>();
		_session.ExecutePostRequestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
		_logger = Substitute.For<ILogger>();
		_environmentSettings = new EnvironmentSettings { IsNetCore = true };
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetCurrentUserInfo, _environmentSettings)
			.Returns(SessionStartUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DesignReady, _environmentSettings)
			.Returns(DesignProbeUrl);
		_guard = new DesignNodeSessionGuard(serviceUrlBuilder, _logger);
	}

	[Test]
	[Description("The guard starts the server session with an authenticated request before it reads the session cookies, because Creatio issues StickySession only in the response to that request.")]
	public void Prepare_ShouldStartServerSessionBeforeReadingCookies_WhenCalled() {
		// Arrange
		ArrangeCookies();

		// Act
		_guard.Prepare(_session, _environmentSettings);

		// Assert
		string[] calls = _session.ReceivedCalls().Select(call => call.GetMethodInfo().Name).ToArray();
		calls.Should().Equal([nameof(ICreatioApplicationClient.ExecutePostRequestAsync),
				nameof(ICreatioApplicationClient.ExportSessionCookies)],
			because: "the cookie only exists after the first authenticated request of the session");
		_session.ReceivedCalls().First().GetArguments().Take(2).Should().Equal([SessionStartUrl, "{}"],
			because: "the session is started with the user-info request, which starts a session on both runtimes");
	}

	[Test]
	[Description("Without StickySession the environment is not a sticky web farm, so the design-node probe is not consulted and nothing is reported.")]
	public void Prepare_ShouldSkipDesignProbe_WhenStickySessionIsNotIssued() {
		// Arrange
		ArrangeCookies(Cookie(".ASPXAUTH"), Cookie("BPMCSRF"));

		// Act
		_guard.Prepare(_session, _environmentSettings);

		// Assert
		SessionCallsTo(nameof(ICreatioApplicationClient.ExecuteGetRequestAsync)).Should().Be(0,
			because: "the probe answers 503 outside a web farm and must not be consulted there");
		_logger.ReceivedCalls().Should().BeEmpty(
			because: "an installation outside a web farm must keep its output unchanged");
	}

	[Test]
	[Description("A sticky session confirmed on the design node lets the installation continue and says so.")]
	public void Prepare_ShouldReportDesignNode_WhenProbeConfirmsDesignNode() {
		// Arrange
		ArrangeCookies(Cookie("StickySession"));
		ArrangeDesignProbe(HttpStatusCode.OK, "{\"isDesignNode\":true}");

		// Act
		Action act = () => _guard.Prepare(_session, _environmentSettings);

		// Assert
		act.Should().NotThrow(because: "the session reaches the design node");
		LoggedWith(nameof(ILogger.WriteInfo)).Should().Equal([DesignNodeSessionGuard.RoutedToDesignNodeMessage],
			because: "the operator should see that the web farm routing was confirmed");
	}

	[Test]
	[Description("A 200 without the probe's own confirmation (a gateway catch-all page, a redirect that ends on a 200 page) is not taken as proof of the design node, so the installation is refused.")]
	public void Prepare_ShouldThrow_WhenProbeAnswersOkWithoutConfirmation() {
		// Arrange
		ArrangeCookies(Cookie("StickySession"));
		ArrangeDesignProbe(HttpStatusCode.OK, "<html>gateway page</html>");

		// Act
		Action act = () => _guard.Prepare(_session, _environmentSettings);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "only the design node may change, and a 200 page that is not the probe's answer confirms nothing")
			.WithMessage("*does not reach the design node*HTTP 200*");
		LoggedWith(nameof(ILogger.WriteInfo)).Should().BeEmpty(
			because: "an unconfirmed answer must not be reported as a confirmation");
	}

	[Test]
	[Description("A Creatio version without the design-node probe answers 404; the installation continues with a warning instead of being blocked.")]
	public void Prepare_ShouldWarnAndContinue_WhenProbeIsNotAvailable() {
		// Arrange
		ArrangeCookies(Cookie("StickySession"));
		ArrangeDesignProbe(HttpStatusCode.NotFound);

		// Act
		Action act = () => _guard.Prepare(_session, _environmentSettings);

		// Assert
		act.Should().NotThrow(because: "a missing probe says nothing about where the session is routed");
		LoggedWith(nameof(ILogger.WriteWarning)).Should().Equal([DesignNodeSessionGuard.DesignProbeUnavailableMessage],
			because: "the operator should know the design node could not be confirmed");
	}

	[Test]
	[Description("A sticky session that reaches a common node (or a gateway whose design backend is down) is refused before any package data is sent, because only the design node may change during an installation.")]
	public void Prepare_ShouldThrow_WhenProbeAnswersServiceUnavailable() {
		// Arrange
		ArrangeCookies(Cookie("StickySession"));
		ArrangeDesignProbe(HttpStatusCode.ServiceUnavailable, "{\"isDesignNode\":false}");

		// Act
		Action act = () => _guard.Prepare(_session, _environmentSettings);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "installing through a common node splits the archive and changes a node that must stay untouched")
			.WithMessage("*does not reach the design node*HTTP 503*Nothing was uploaded*");
	}

	#endregion

}
