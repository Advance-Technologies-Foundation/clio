namespace Clio.Tests.Command;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class HealthCheckCommandTestCase
{
	private EnvironmentSettings _environmentSettings;
	private IApplicationClient _applicationClient;
	private IJsonResponseFormater _jsonResponseFormater;
	private HealthCheckCommand _hcCommand;

	[SetUp]
	public void SetUp()
	{
		_environmentSettings = new EnvironmentSettings
		{
			Login = "Test",
			Password = "Test",
			IsNetCore = false,
			Maintainer = "Test",
			Uri = "http://test.domain.com"
		};
		_applicationClient = Substitute.For<IApplicationClient>();
		_jsonResponseFormater = Substitute.For<IJsonResponseFormater>();
		_hcCommand = new HealthCheckCommand(_applicationClient, _environmentSettings, _jsonResponseFormater);
		// Default to a healthy 200 probe so tests that do not care about the transport still pass without a
		// real network call; individual tests override the factory to simulate failures/stalls.
		UseHandler(StubHttpMessageHandler.RespondingWith(() => new HttpResponseMessage(HttpStatusCode.OK)));
		// Never wall-clock sleep between retry attempts in unit tests (ENG-95709); tests that assert on the
		// delay override this with a recording stub.
		_hcCommand.SleepSeconds = _ => { };
	}

	private StubHttpMessageHandler _handler;

	private void UseHandler(StubHttpMessageHandler handler) {
		_handler = handler;
		_hcCommand.HttpMessageHandlerFactory = () => handler;
	}

	[Test]
	[Description("Probes the .NET Framework WebHost route (/0/api/HealthCheck/Ping) when --web-host is set.")]
	public void HealthCheckCommand_FormsCorrectApplicationRequest_WhenWebHostIsTrue() {
		// Act
		_hcCommand.Execute(new HealthCheckOptions { WebHost = "true" });

		// Assert
		_handler.RequestedUris.Should().ContainSingle()
			.Which.Should().Be(_environmentSettings.Uri + "/0/api/HealthCheck/Ping",
				because: "the WebHost check targets the .NET Framework health-check route");
	}

	[Test]
	[Description("Probes the WebAppLoader route (/api/HealthCheck/Ping) when --web-app is set.")]
	public void HealthCheckCommand_FormsCorrectApplicationRequest_WhenWebAppIsTrue() {
		// Act
		_hcCommand.Execute(new HealthCheckOptions { WebApp = "true" });

		// Assert
		_handler.RequestedUris.Should().ContainSingle()
			.Which.Should().Be(_environmentSettings.Uri + "/api/HealthCheck/Ping",
				because: "the WebApp check targets the WebAppLoader health-check route");
	}

	[Test]
	[Description("With no flags on a .NET Framework environment, probes the WebHost route.")]
	public void HealthCheckCommand_UsesConfiguredFrameworkRoute_WhenNoFlagsProvided() {
		// Act
		_hcCommand.Execute(new HealthCheckOptions());

		// Assert
		_handler.RequestedUris.Should().ContainSingle()
			.Which.Should().Be(_environmentSettings.Uri + "/0/api/HealthCheck/Ping",
				because: "a .NET Framework environment defaults to the WebHost route");
	}

	[Test]
	[Description("With no flags on a .NET Core environment, probes the WebAppLoader route.")]
	public void HealthCheckCommand_UsesConfiguredNetCoreRoute_WhenNoFlagsProvided() {
		// Arrange
		_environmentSettings.IsNetCore = true;

		// Act
		_hcCommand.Execute(new HealthCheckOptions());

		// Assert
		_handler.RequestedUris.Should().ContainSingle()
			.Which.Should().Be(_environmentSettings.Uri + "/api/HealthCheck/Ping",
				because: "a .NET Core environment defaults to the WebAppLoader route");
	}

	[Test]
	[Description("A transport error on the probe yields a non-zero exit code.")]
	public void HealthCheckCommand_ReturnsFailure_WhenRequestThrows() {
		// Arrange
		UseHandler(StubHttpMessageHandler.Throwing(new HttpRequestException("boom")));

		// Act
		int result = _hcCommand.Execute(new HealthCheckOptions());

		// Assert
		result.Should().Be(1, because: "a probe that cannot reach the endpoint is unhealthy");
	}

	[Test]
	[Description("A non-2xx status on the probe is classified unhealthy (status-aware probe, ENG-94417 AC2).")]
	public void HealthCheckCommand_ReturnsFailure_WhenProbeReturnsNon2xx() {
		// Arrange
		UseHandler(StubHttpMessageHandler.RespondingWith(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

		// Act
		int result = _hcCommand.Execute(new HealthCheckOptions());

		// Assert
		result.Should().Be(1, because: "only a genuine 2xx answer is healthy; a 503 warm-up response is not");
	}

	[Test]
	[Description("ENG-94417 AC2 regression: the ping endpoint answering a redirect (typically to the login page) is classified unhealthy — the redirect is NOT followed, so the login page's own 200 cannot be counted as a healthy answer.")]
	public void HealthCheckCommand_ClassifiesRedirect_AsUnhealthy() {
		// Arrange — the degraded shape from the ticket: the app redirects instead of serving its health endpoint.
		UseHandler(StubHttpMessageHandler.RespondingWith(() => {
			HttpResponseMessage redirect = new(HttpStatusCode.Found);
			redirect.Headers.Location = new Uri("http://test.domain.com/Login/NuiLogin.aspx");
			return redirect;
		}));
		_hcCommand.Logger = Substitute.For<ILogger>();

		// Act
		int result = _hcCommand.Execute(new HealthCheckOptions { Json = true });

		// Assert
		result.Should().Be(1,
			because: "a health endpoint that redirects is not serving a health response and must be reported unhealthy");
		_handler.RequestedUris.Should().ContainSingle(
			because: "the redirect must not be followed — following it would probe the login page instead");
		_jsonResponseFormater.Received(1).FormatEnvelope("healthcheck",
			Clio.Common.CommandErrorCodes.HealthCheckFailed,
			Arg.Is<string>(message => message.Contains("redirect")));
	}

	[Test]
	[Description("ENG-94417 AC2/AC4 regression: a connect-but-never-answer endpoint is classified unhealthy, not reported OK.")]
	public void HealthCheckCommand_ClassifiesStalledEndpoint_AsUnhealthy() {
		// Arrange — the endpoint accepts the connection and then never answers.
		UseHandler(StubHttpMessageHandler.Stalling());

		// Act
		int result = _hcCommand.Execute(new HealthCheckOptions { TimeOut = 500, MaxAttempts = 1 });

		// Assert
		result.Should().Be(1,
			because: "a stalled endpoint (TCP accept, no response) must be reported unhealthy rather than OK");
	}

	[Test]
	[Description("ENG-94417 AC3/AC4 regression: --timeout bounds an individual probe, so a stalled endpoint aborts at ~N ms rather than the inherited ~100s default.")]
	public void HealthCheckCommand_BoundsProbe_ByTimeout_ForStalledEndpoint() {
		// Arrange — a stalled endpoint that would otherwise pin the probe for the ~100s default.
		UseHandler(StubHttpMessageHandler.Stalling());
		_hcCommand.Logger = Substitute.For<ILogger>();

		// Act
		Stopwatch stopwatch = Stopwatch.StartNew();
		int result = _hcCommand.Execute(new HealthCheckOptions { WebHost = "true", TimeOut = 1000, MaxAttempts = 1, Json = true });
		stopwatch.Stop();

		// Assert
		result.Should().Be(1, because: "the stalled probe is unhealthy");
		stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
			because: "the probe must be bounded by --timeout (1000ms), not the inherited ~100s DefaultTimeout; the "
				+ "5s ceiling still leaves headroom for a loaded CI agent while failing on a regression to a larger timeout");
		_jsonResponseFormater.Received(1).FormatEnvelope("healthcheck",
			Clio.Common.CommandErrorCodes.HealthCheckFailed,
			Arg.Is<string>(message => message.Contains("timed out")));
	}

	[Test]
	[Description("Execute should emit a success envelope (via FormatEnvelope) and return 0 when --json is set and all probes succeed")]
	public void Execute_ShouldEmitSuccessEnvelope_WhenJsonAndHealthy() {
		// Act
		int result = _hcCommand.Execute(new HealthCheckOptions { Json = true });

		// Assert
		result.Should().Be(0, because: "a healthy 2xx probe returns success");
		_jsonResponseFormater.Received(1).FormatEnvelope("healthcheck", Arg.Any<HealthCheckResult>());
	}

	[Test]
	[Description("Execute should emit an error envelope (ok=false with healthcheck-failed) and return 1 when --json is set and a probe fails")]
	public void Execute_ShouldEmitErrorEnvelope_WhenJsonAndProbeFails() {
		// Arrange
		UseHandler(StubHttpMessageHandler.Throwing(new HttpRequestException("boom")));

		// Act
		int result = _hcCommand.Execute(new HealthCheckOptions { Json = true });

		// Assert
		result.Should().Be(1, because: "a failing probe returns the healthcheck-failed exit code");
		_jsonResponseFormater.Received(1).FormatEnvelope("healthcheck",
			Clio.Common.CommandErrorCodes.HealthCheckFailed, Arg.Any<string>());
	}

	[Test]
	[Description("Execute in non-JSON mode should write the exact human progress/outcome lines in order — text-output regression guard for the Probe refactor")]
	public void Execute_ShouldWriteHumanLinesInOrder_WhenNonJsonAndHealthy() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		_hcCommand.Logger = logger;

		// Act
		_hcCommand.Execute(new HealthCheckOptions { WebHost = "true" });

		// Assert
		Received.InOrder(() => {
			logger.WriteInfo($"Checking WebHost {_environmentSettings.Uri}/0/api/HealthCheck/Ping ...");
			logger.WriteInfo("\tWebHost - OK");
		});
		logger.DidNotReceive().WriteLine(Arg.Any<string>()); // no JSON envelope in non-json mode
	}

	[Test]
	[Description("Execute in non-JSON mode should NOT emit a JSON envelope even when a probe fails (text-output regression guard)")]
	public void Execute_ShouldNotEmitEnvelope_WhenNonJsonAndProbeFails() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		_hcCommand.Logger = logger;
		UseHandler(StubHttpMessageHandler.Throwing(new HttpRequestException("boom")));

		// Act
		int result = _hcCommand.Execute(new HealthCheckOptions { WebHost = "true", MaxAttempts = 1 });

		// Assert
		result.Should().Be(1, because: "the probe failed");
		logger.Received(1).WriteError("\tError: boom");
		logger.DidNotReceive().WriteLine(Arg.Any<string>());
		_jsonResponseFormater.DidNotReceive().FormatEnvelope(Arg.Any<string>(), Arg.Any<HealthCheckResult>());
	}

	[Test]
	[Description("HealthCheckCommand resolves from the DI container.")]
	public void HealthCheckCommand_IsRegistered()
	{
		BindingsModule bs = new BindingsModule();
		var container = bs.Register(_environmentSettings);
		var command = container.GetRequiredService<HealthCheckCommand>();
		command.Should().NotBeNull(because: "the command must be registered for CLI dispatch");
	}

	[Test]
	[Description("ENG-95709: a transient warm-up 500 is retried and, once the app serves a 2xx, the healthcheck passes.")]
	public void HealthCheckCommand_RetriesTransient500_ThenSucceeds() {
		// Arrange — the health endpoint answers 500 twice (cold-start warm-up) then 200.
		Queue<HttpStatusCode> statuses = new(new[] {
			HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError, HttpStatusCode.OK });
		UseHandler(StubHttpMessageHandler.RespondingWith(
			() => new HttpResponseMessage(statuses.Count > 0 ? statuses.Dequeue() : HttpStatusCode.OK)));

		// Act
		int result = _hcCommand.Execute(new HealthCheckOptions { WebApp = "true", MaxAttempts = 3 });

		// Assert
		result.Should().Be(0, because: "the app became healthy within the retry budget");
		_handler.RequestedUris.Should().HaveCount(3, because: "two warm-up 500s are retried before the 200");
	}

	[Test]
	[Description("ENG-95709: a persistent 5xx is retried up to MaxAttempts and then reported unhealthy.")]
	public void HealthCheckCommand_RetriesTransientError_UpToMaxAttempts_ThenFails() {
		// Arrange
		UseHandler(StubHttpMessageHandler.RespondingWith(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

		// Act
		int result = _hcCommand.Execute(new HealthCheckOptions { WebApp = "true", MaxAttempts = 3 });

		// Assert
		result.Should().Be(1, because: "a 5xx that never clears within the retry budget is unhealthy");
		_handler.RequestedUris.Should().HaveCount(3, because: "the probe is attempted MaxAttempts times");
	}

	[Test]
	[Description("ENG-95709: MaxAttempts bounds the number of probe attempts.")]
	public void HealthCheckCommand_HonorsMaxAttempts() {
		// Arrange
		UseHandler(StubHttpMessageHandler.RespondingWith(() => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

		// Act
		_hcCommand.Execute(new HealthCheckOptions { WebApp = "true", MaxAttempts = 2 });

		// Assert
		_handler.RequestedUris.Should().HaveCount(2, because: "exactly MaxAttempts (2) probes are issued");
	}

	[Test]
	[Description("ENG-95709: a redirect is a persistent misconfiguration, not a warm-up shape — it is NOT retried (preserves ENG-94417).")]
	public void HealthCheckCommand_DoesNotRetry_Redirect() {
		// Arrange
		UseHandler(StubHttpMessageHandler.RespondingWith(() => {
			HttpResponseMessage redirect = new(HttpStatusCode.Found);
			redirect.Headers.Location = new Uri("http://test.domain.com/Login/NuiLogin.aspx");
			return redirect;
		}));

		// Act
		int result = _hcCommand.Execute(new HealthCheckOptions { WebApp = "true", MaxAttempts = 3 });

		// Assert
		result.Should().Be(1, because: "a redirecting health endpoint is unhealthy");
		_handler.RequestedUris.Should().ContainSingle(
			because: "a 3xx is persistent, so it must fail fast without consuming the retry budget");
	}

	[Test]
	[Description("ENG-95709: the command waits RetryDelay seconds between attempts.")]
	public void HealthCheckCommand_WaitsRetryDelay_BetweenAttempts() {
		// Arrange — one warm-up 500 then a 200; record the requested sleep durations.
		Queue<HttpStatusCode> statuses = new(new[] { HttpStatusCode.InternalServerError, HttpStatusCode.OK });
		UseHandler(StubHttpMessageHandler.RespondingWith(
			() => new HttpResponseMessage(statuses.Count > 0 ? statuses.Dequeue() : HttpStatusCode.OK)));
		List<int> sleeps = [];
		_hcCommand.SleepSeconds = seconds => sleeps.Add(seconds);

		// Act
		int result = _hcCommand.Execute(new HealthCheckOptions { WebApp = "true", MaxAttempts = 3, RetryDelay = 7 });

		// Assert
		result.Should().Be(0);
		sleeps.Should().ContainSingle(because: "exactly one wait precedes the single retry")
			.Which.Should().Be(7, because: "the wait uses RetryDelay (7s)");
	}

	/// <summary>
	/// Deterministic <see cref="HttpMessageHandler"/> test double: responds with a canned response, throws a
	/// canned exception, or stalls (accepts the connection and never answers) until the HttpClient timeout
	/// cancels the request. Records every requested URI so route-composition assertions survive the move off
	/// creatio.client onto a clio-owned HttpClient.
	/// </summary>
	private sealed class StubHttpMessageHandler : HttpMessageHandler {
		private readonly Func<HttpResponseMessage> _responseFactory;
		private readonly Exception _throw;
		private readonly bool _stall;

		public List<string> RequestedUris { get; } = [];

		private StubHttpMessageHandler(Func<HttpResponseMessage> responseFactory, Exception toThrow, bool stall) {
			_responseFactory = responseFactory;
			_throw = toThrow;
			_stall = stall;
		}

		public static StubHttpMessageHandler RespondingWith(Func<HttpResponseMessage> responseFactory) =>
			new(responseFactory, null, false);

		public static StubHttpMessageHandler Throwing(Exception toThrow) => new(null, toThrow, false);

		public static StubHttpMessageHandler Stalling() => new(null, null, true);

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request, CancellationToken cancellationToken) {
			RequestedUris.Add(request.RequestUri?.ToString());
			if (_stall) {
				// Never completes on its own; only the HttpClient.Timeout-driven cancellation ends it, which
				// is exactly how a connect-but-never-answer endpoint must be bounded (ENG-94417).
				await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
			}
			if (_throw is not null) {
				throw _throw;
			}
			return _responseFactory();
		}
	}
}
