namespace Clio.Tests.Package;

using System;
using System.Collections.Generic;
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
	private const string DesignNodeConfirmation = "{\"isDesignNode\":true}";
	private const string CommonNodeAnswer = "{\"isDesignNode\":false}";
	private const string ProbeTimeout = "timeout";
	private const string GatewayNoServerPage = "<html><body><h1>503 Service Unavailable</h1>No server is available to handle this request.</body></html>";

	#endregion

	#region Fields: Private

	private ICreatioApplicationClient _session;
	private ILogger _logger;
	private EnvironmentSettings _environmentSettings;
	private ManualTimeProvider _clock;
	private ClockAdvancingDelay _delay;
	private DesignNodeSessionGuard _guard;

	#endregion

	#region Methods: Private

	private static CreatioSessionCookie Cookie(string name) =>
		new(name, string.Empty, "127.0.0.1", "/", true, false, "None", DateTime.MinValue);

	private void ArrangeCookies(params CreatioSessionCookie[] cookies) =>
		_session.ExportSessionCookies().Returns(cookies);

	private void ArrangeDesignProbe(HttpStatusCode? status, string body = null) =>
		ArrangeDesignProbeSequence((status, body));

	// The probe answers each listed response once, in order, and keeps answering the last one afterwards.
	// A null status stands for a probe that gets no HTTP answer at all: a timeout when the body is "timeout",
	// a reset connection otherwise.
	private void ArrangeDesignProbeSequence(params (HttpStatusCode? Status, string Body)[] answers) {
		Queue<(HttpStatusCode? Status, string Body)> pending = new(answers);
		(HttpStatusCode? Status, string Body) last = answers[^1];
		_session.ExecuteGetRequestAsync(DesignProbeUrl, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<CancellationToken>())
			.Returns(_ => {
				(HttpStatusCode? status, string body) = pending.Count > 0 ? pending.Dequeue() : last;
				return status is null
					? Task.FromException<HttpResponseMessage>(body == ProbeTimeout
						? new TaskCanceledException("probe timeout")
						: new HttpRequestException("connection reset"))
					: Task.FromResult(new HttpResponseMessage(status.Value) {
						Content = new StringContent(body ?? string.Empty)
					});
			});
	}

	private void ArrangeSessionStart(HttpStatusCode status) =>
		_session.ExecutePostRequestAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromResult(new HttpResponseMessage(status)));

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
		ArrangeSessionStart(HttpStatusCode.OK);
		_logger = Substitute.For<ILogger>();
		_environmentSettings = new EnvironmentSettings { IsNetCore = true };
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetCurrentUserInfo, _environmentSettings)
			.Returns(SessionStartUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DesignReady, _environmentSettings)
			.Returns(DesignProbeUrl);
		_clock = new ManualTimeProvider();
		_delay = new ClockAdvancingDelay(_clock);
		_guard = new DesignNodeSessionGuard(serviceUrlBuilder, _logger, _clock, _delay);
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
	[Description("An unsuccessful answer to the session-start request is reported, because a web farm whose session did not start issues no StickySession and would otherwise pass as a single node without a word.")]
	public void Prepare_ShouldWarnAndContinue_WhenSessionStartIsNotSuccessful() {
		// Arrange
		ArrangeSessionStart(HttpStatusCode.InternalServerError);
		ArrangeCookies(Cookie(".ASPXAUTH"));

		// Act
		Action act = () => _guard.Prepare(_session, _environmentSettings);

		// Assert
		act.Should().NotThrow(
			because: "a single node whose user-info service misbehaves must still be able to install as before");
		LoggedWith(nameof(ILogger.WriteWarning)).Should().ContainSingle(
				because: "the operator must learn that the web farm routing could not be established")
			.Which.Should().Contain("HTTP 500", because: "the warning names the status the request answered");
	}

	[Test]
	[Description("A sticky session confirmed on the design node lets the installation continue at once and says so.")]
	public void Prepare_ShouldReportDesignNode_WhenProbeConfirmsDesignNode() {
		// Arrange
		ArrangeCookies(Cookie("StickySession"));
		ArrangeDesignProbe(HttpStatusCode.OK, DesignNodeConfirmation);

		// Act
		Action act = () => _guard.Prepare(_session, _environmentSettings);

		// Assert
		act.Should().NotThrow(because: "the session reaches the design node");
		LoggedWith(nameof(ILogger.WriteInfo)).Should().Equal([DesignNodeSessionGuard.RoutedToDesignNodeMessage],
			because: "the operator should see that the web farm routing was confirmed");
		_delay.Calls.Should().Be(0, because: "a confirmed design node is never waited for");
		_session.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(ICreatioApplicationClient.ExecuteGetRequestAsync))
			.Select(call => call.GetArguments()[1])
			.Should().Equal([15_000],
				because: "one hung probe must not outlast the polling it is part of, so it times out after 15 seconds");
	}

	[Test]
	[Description("A 200 without the probe's own confirmation (a gateway catch-all page, a redirect that ends on a 200 page) is not taken as proof of the design node, so the installation is refused without waiting.")]
	public void Prepare_ShouldThrow_WhenProbeAnswersOkWithoutConfirmation() {
		// Arrange
		ArrangeCookies(Cookie("StickySession"));
		ArrangeDesignProbe(HttpStatusCode.OK, "<html>gateway page</html>");

		// Act
		Action act = () => _guard.Prepare(_session, _environmentSettings);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "only the design node may change, and a 200 page that is not the probe's answer confirms nothing")
			.WithMessage("*does not reach the design node*HTTP 200*",
				because: "the refusal names the status the probe answered");
		LoggedWith(nameof(ILogger.WriteInfo)).Should().BeEmpty(
			because: "an unconfirmed answer must not be reported as a confirmation");
		_delay.Calls.Should().Be(0, because: "a 200 from somewhere else will not turn into the design node by waiting");
	}

	[TestCase(HttpStatusCode.NotFound)]
	[TestCase(HttpStatusCode.Unauthorized)]
	[TestCase(HttpStatusCode.InternalServerError)]
	[Description("ENG-102757 AC-3: an answer that neither confirms the design node nor says it is down for the moment (a missing probe or a gateway that does not forward it, a rejected request, a server error) refuses the installation at once, before anything is uploaded.")]
	public void Prepare_ShouldThrowWithoutWaiting_WhenProbeAnswersNeitherConfirmationNorUnavailability(
		HttpStatusCode status) {
		// Arrange
		ArrangeCookies(Cookie("StickySession"));
		ArrangeDesignProbe(status);

		// Act
		Action act = () => _guard.Prepare(_session, _environmentSettings);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "only the design node may change, and this answer does not show the session reaches it")
			.WithMessage($"*does not reach the design node*HTTP {(int)status}*Nothing was uploaded*",
				because: "the refusal names the status and says that nothing was sent");
		_delay.Calls.Should().Be(0, because: "only a gateway saying the design node is down is worth waiting for");
		LoggedWith(nameof(ILogger.WriteWarning)).Should().BeEmpty(
			because: "the routing is refused, not let through with a warning");
	}

	[TestCase(HttpStatusCode.ServiceUnavailable)]
	[TestCase(HttpStatusCode.BadGateway)]
	[TestCase(HttpStatusCode.GatewayTimeout)]
	[Description("A design node that is down for the moment (it restarts after the previous installation, so the gateway answers 502, 503 or 504) is waited for, and the installation continues once the probe confirms it.")]
	public void Prepare_ShouldWaitAndContinue_WhenDesignNodeComesBackWithinTheBudget(HttpStatusCode status) {
		// Arrange
		ArrangeCookies(Cookie("StickySession"));
		ArrangeDesignProbeSequence((status, null), (status, null), (status, null),
			(HttpStatusCode.OK, DesignNodeConfirmation));

		// Act
		Action act = () => _guard.Prepare(_session, _environmentSettings);

		// Assert
		act.Should().NotThrow(because: "the design node came back before the wait ran out");
		_delay.Calls.Should().Be(3, because: "the probe is repeated after each pause until it confirms the design node");
		string[] info = LoggedWith(nameof(ILogger.WriteInfo));
		info.Should().HaveCount(2, because: "the wait is announced once and the confirmation follows it");
		info[0].Should().Contain($"HTTP {(int)status}", because: "the wait names the answer it is waiting out")
			.And.Contain("Waiting up to 5 minutes",
				because: "the operator must see why the installation pauses and for how long at most");
		info[1].Should().Be(DesignNodeSessionGuard.RoutedToDesignNodeMessage,
			because: "the installation starts only after the design node is confirmed");
	}

	[Test]
	[Description("A design node that stays down for the whole wait (the gateway keeps answering with its own 503 page) refuses the installation before anything is uploaded, after five minutes and with a progress line each minute.")]
	public void Prepare_ShouldThrowAfterTheWait_WhenDesignNodeStaysUnavailable() {
		// Arrange
		ArrangeCookies(Cookie("StickySession"));
		ArrangeDesignProbe(HttpStatusCode.ServiceUnavailable, GatewayNoServerPage);
		DateTimeOffset startedAt = _clock.GetUtcNow();

		// Act
		Action act = () => _guard.Prepare(_session, _environmentSettings);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "installing through a common node splits the archive and changes a node that must stay untouched")
			.WithMessage("*does not reach the design node*HTTP 503*Nothing was uploaded*",
				because: "the refusal names the status the gateway kept answering");
		(_clock.GetUtcNow() - startedAt).Should().Be(TimeSpan.FromMinutes(5),
			because: "the guard waits out its whole budget, and not longer, before it refuses");
		_delay.Calls.Should().Be(60, because: "the probe is repeated every five seconds during the wait");
		LoggedWith(nameof(ILogger.WriteInfo)).Count(line => line.StartsWith("Still waiting", StringComparison.Ordinal))
			.Should().Be(5, because: "a CI log should show the wait is alive once a minute");
	}

	[Test]
	[Description("A common node answers the probe itself with 503 {\"isDesignNode\":false}: the session reached a node that is not the design node, and waiting would not change that, so the installation is refused at once.")]
	public void Prepare_ShouldThrowWithoutWaiting_WhenACommonNodeAnswersTheProbe() {
		// Arrange
		ArrangeCookies(Cookie("StickySession"));
		ArrangeDesignProbe(HttpStatusCode.ServiceUnavailable, CommonNodeAnswer);

		// Act
		Action act = () => _guard.Prepare(_session, _environmentSettings);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a sticky session routed to a common node must never upload there")
			.WithMessage("*does not reach the design node*HTTP 503*",
				because: "the refusal names the status the common node answered");
		_delay.Calls.Should().Be(0,
			because: "only the gateway's own 503 means the design node is down; a node's own answer is final");
	}

	[Test]
	[Description("A wait ends as soon as the probe gives a real answer instead of the gateway's unavailable page, so a design node that turns out to be missing is refused without spending the rest of the budget.")]
	public void Prepare_ShouldStopWaiting_WhenTheProbeTurnsIntoARealAnswer() {
		// Arrange
		ArrangeCookies(Cookie("StickySession"));
		ArrangeDesignProbeSequence((HttpStatusCode.ServiceUnavailable, GatewayNoServerPage),
			(HttpStatusCode.NotFound, null));

		// Act
		Action act = () => _guard.Prepare(_session, _environmentSettings);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a missing probe confirms nothing")
			.WithMessage("*does not reach the design node*HTTP 404*",
				because: "the refusal names the last answer, not the first one");
		_delay.Calls.Should().Be(1, because: "the wait ends on the first answer that is not the gateway's own 503");
	}

	[Test]
	[Description("A probe that gets no HTTP answer while the design node is down (a connection reset by a stopping node) keeps the wait going instead of ending it with a transport error.")]
	public void Prepare_ShouldKeepWaiting_WhenAProbeFailsDuringTheWait() {
		// Arrange
		ArrangeCookies(Cookie("StickySession"));
		ArrangeDesignProbeSequence((HttpStatusCode.ServiceUnavailable, GatewayNoServerPage), (null, null),
			(HttpStatusCode.OK, DesignNodeConfirmation));

		// Act
		Action act = () => _guard.Prepare(_session, _environmentSettings);

		// Assert
		act.Should().NotThrow(because: "the design node came back after the failed probe");
		_delay.Calls.Should().Be(2, because: "the failed probe counts as one more unavailable answer");
		LoggedWith(nameof(ILogger.WriteInfo)).Last().Should().Be(DesignNodeSessionGuard.RoutedToDesignNodeMessage,
			because: "the installation starts only after the design node is confirmed");
	}

	[Test]
	[Description("A first probe that gets no HTTP answer (a hung design backend times out) starts the wait instead of failing the command with a transport error, and the installation continues once the design node confirms.")]
	public void Prepare_ShouldWait_WhenTheFirstProbeTimesOut() {
		// Arrange
		ArrangeCookies(Cookie("StickySession"));
		ArrangeDesignProbeSequence((null, ProbeTimeout), (HttpStatusCode.OK, DesignNodeConfirmation));

		// Act
		Action act = () => _guard.Prepare(_session, _environmentSettings);

		// Assert
		act.Should().NotThrow(because: "the design node answered on the next probe");
		_delay.Calls.Should().Be(1, because: "the timed-out probe counts as an unavailable design node");
		LoggedWith(nameof(ILogger.WriteInfo)).First().Should().Contain("got no answer",
			because: "the wait says the probe got no answer rather than inventing a status");
	}

	[Test]
	[Description("When no probe gets an answer for the whole wait, the refusal says the probe got no answer instead of naming a status, and nothing is uploaded.")]
	public void Prepare_ShouldSayTheProbeGotNoAnswer_WhenEveryProbeFails() {
		// Arrange
		ArrangeCookies(Cookie("StickySession"));
		ArrangeDesignProbe(null, ProbeTimeout);

		// Act
		Action act = () => _guard.Prepare(_session, _environmentSettings);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "the design node was never confirmed")
			.WithMessage("*does not reach the design node*got no answer*Nothing was uploaded*",
				because: "a probe without an HTTP answer has no status to name");
		_delay.Calls.Should().Be(60, because: "failed probes are waited out like the gateway's own 503");
	}

	[Test]
	[Description("Outside a web farm (no StickySession) the restart is not followed by any wait or probe, so single-node output is unchanged.")]
	public void WaitForRestartedDesignNode_ShouldReturnAtOnce_WhenStickySessionIsNotIssued() {
		// Arrange
		ArrangeCookies(Cookie(".ASPXAUTH"));

		// Act
		_guard.WaitForRestartedDesignNode(_session, _environmentSettings);

		// Assert
		SessionCallsTo(nameof(ICreatioApplicationClient.ExecuteGetRequestAsync)).Should().Be(0,
			because: "the probe is meaningful only on a web farm");
		_delay.Calls.Should().Be(0, because: "a single node is not waited for");
		_logger.ReceivedCalls().Should().BeEmpty(because: "an installation outside a web farm keeps its output");
	}

	[Test]
	[Description("ENG-102757 EC-4: RestartApp answers before the design node stops, so the wait first sees the node still up, then waits until it has stopped answering twice in a row and confirms it again; the next installation then starts on a design node that is not about to stop.")]
	public void WaitForRestartedDesignNode_ShouldWaitUntilTheDesignNodeWentDownAndCameBack() {
		// Arrange
		ArrangeCookies(Cookie("StickySession"));
		ArrangeDesignProbeSequence((HttpStatusCode.OK, DesignNodeConfirmation),
			(HttpStatusCode.ServiceUnavailable, GatewayNoServerPage), (null, null),
			(HttpStatusCode.ServiceUnavailable, GatewayNoServerPage), (HttpStatusCode.OK, DesignNodeConfirmation));

		// Act
		_guard.WaitForRestartedDesignNode(_session, _environmentSettings);

		// Assert
		SessionCallsTo(nameof(ICreatioApplicationClient.ExecuteGetRequestAsync)).Should().Be(5,
			because: "a confirmation from the node that is still stopping must not end the wait");
		LoggedWith(nameof(ILogger.WriteInfo)).Should().Equal([
				string.Format(DesignNodeSessionGuard.WaitingForRestartMessageFormat, 5),
				DesignNodeSessionGuard.BackAfterRestartMessage],
			because: "the operator sees why the command pauses after the restart and when the design node is back");
		LoggedWith(nameof(ILogger.WriteWarning)).Should().BeEmpty(because: "the design node came back in time");
	}

	[Test]
	[Description("A single failed probe while the design node still answers (one connection reset while it drains) is not taken for the stop: the shutdown watch restarts its count and ends only after two answers in a row that do not confirm the design node.")]
	public void WaitForRestartedDesignNode_ShouldNotTakeASingleFailedProbeForTheStop() {
		// Arrange
		ArrangeCookies(Cookie("StickySession"));
		ArrangeDesignProbeSequence((HttpStatusCode.OK, DesignNodeConfirmation), (null, null),
			(HttpStatusCode.OK, DesignNodeConfirmation), (null, null), (HttpStatusCode.OK, DesignNodeConfirmation),
			(HttpStatusCode.ServiceUnavailable, GatewayNoServerPage),
			(HttpStatusCode.ServiceUnavailable, GatewayNoServerPage), (HttpStatusCode.OK, DesignNodeConfirmation));

		// Act
		_guard.WaitForRestartedDesignNode(_session, _environmentSettings);

		// Assert
		SessionCallsTo(nameof(ICreatioApplicationClient.ExecuteGetRequestAsync)).Should().Be(8,
			because: "the confirmations after each single failure keep the watch going until the node really stops");
		LoggedWith(nameof(ILogger.WriteInfo)).Last().Should().Be(DesignNodeSessionGuard.BackAfterRestartMessage,
			because: "the node is reported back only after it was seen stopped and confirmed again");
	}

	[Test]
	[Description("A restart too quick to observe (the design node never stops answering) ends the wait after the one-minute shutdown window with the design node confirmed.")]
	public void WaitForRestartedDesignNode_ShouldEndAfterTheShutdownWindow_WhenTheDesignNodeNeverStopsAnswering() {
		// Arrange
		ArrangeCookies(Cookie("StickySession"));
		ArrangeDesignProbe(HttpStatusCode.OK, DesignNodeConfirmation);
		DateTimeOffset startedAt = _clock.GetUtcNow();

		// Act
		_guard.WaitForRestartedDesignNode(_session, _environmentSettings);

		// Assert
		(_clock.GetUtcNow() - startedAt).Should().Be(TimeSpan.FromSeconds(65),
			because: "the guard looks for the shutdown for one minute and then accepts the next confirmation");
		LoggedWith(nameof(ILogger.WriteInfo)).Last().Should().Be(DesignNodeSessionGuard.BackAfterRestartMessage,
			because: "the design node answers as the design node after the window");
	}

	[Test]
	[Description("A design node that does not come back within five minutes after the restart is reported as a warning and does not fail the installation, which already succeeded.")]
	public void WaitForRestartedDesignNode_ShouldWarn_WhenTheDesignNodeDoesNotComeBack() {
		// Arrange
		ArrangeCookies(Cookie("StickySession"));
		ArrangeDesignProbe(HttpStatusCode.ServiceUnavailable, GatewayNoServerPage);
		DateTimeOffset startedAt = _clock.GetUtcNow();

		// Act
		Action act = () => _guard.WaitForRestartedDesignNode(_session, _environmentSettings);

		// Assert
		act.Should().NotThrow(because: "the package is installed; only the restart did not finish in time");
		(_clock.GetUtcNow() - startedAt).Should().Be(TimeSpan.FromMinutes(5),
			because: "the wait after the restart has the same five-minute budget");
		LoggedWith(nameof(ILogger.WriteWarning)).Should().ContainSingle(
				because: "the operator must learn that the design node is not back")
			.Which.Should().Contain("did not answer within 5 minutes",
				because: "the warning says how long the guard waited");
	}

	#endregion

}
