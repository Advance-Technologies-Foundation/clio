using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Unit tests for <see cref="EnvironmentAvailabilityProbe"/>, driven through an in-process stub
/// <see cref="HttpMessageHandler"/> (no sockets, no Creatio), following
/// <see cref="CliogateHttpReadinessProbeTests"/>.
/// </summary>
/// <remarks>
/// This probe is the sole producer of <c>Reachable</c>, <c>ReloadObserved</c> and <c>EverReachable</c>,
/// which select between <c>ConfirmedByReload</c>, <c>TransportFailure</c> and <c>KeepWaiting</c> in
/// <see cref="CompilationCompletionDecider"/> - that is, they set the compile command's exit code. Each
/// behaviour below fails silently when regressed: a status check would make an authenticated-only stand
/// permanently unreachable, and an unenforced timeout would make the reload invisible again.
/// </remarks>
[Category("Unit")]
[Property("Module", "Common")]
[TestFixture]
internal sealed class EnvironmentAvailabilityProbeTests {

	private const string ProbeUrl = "https://localhost/0/api/ConfigurationStatus/GetLastCompilationResult";

	[Test]
	[Description("Any HTTP status means the application is serving requests. 200, 401 and a 302 to the login page all count as reachable, because the question is whether the environment answers, not whether this probe is authenticated - a status check here would make an authenticated-only stand read as permanently unreachable and end every build in a transport failure.")]
	[TestCase(HttpStatusCode.OK)]
	[TestCase(HttpStatusCode.Unauthorized)]
	[TestCase(HttpStatusCode.Found)]
	public void IsReachable_ShouldReturnTrue_ForAnyHttpStatus(HttpStatusCode status) {
		// Arrange
		using StubHttpMessageHandler handler = StubHttpMessageHandler.Returning(status);
		IEnvironmentAvailabilityProbe probe = CreateProbe(handler);

		// Act
		bool reachable = probe.IsReachable(TimeSpan.FromSeconds(5), CancellationToken.None);

		// Assert
		reachable.Should().BeTrue(
			because: "an answered request proves the application is up, whatever status it answered with");
	}

	[Test]
	[Description("A transport failure is 'not answering right now', which is exactly what the caller acts on, so it maps to false rather than propagating.")]
	public void IsReachable_ShouldReturnFalse_WhenTheRequestFails() {
		// Arrange
		using StubHttpMessageHandler handler = StubHttpMessageHandler.Throwing(
			new HttpRequestException("connection refused"));
		IEnvironmentAvailabilityProbe probe = CreateProbe(handler);

		// Act
		bool reachable = probe.IsReachable(TimeSpan.FromSeconds(5), CancellationToken.None);

		// Assert
		reachable.Should().BeFalse(
			because: "a refused connection means the environment is not answering, and no caller would act differently on the distinction");
	}

	[Test]
	[Description("A timed-out request maps to false the same way a refused one does. HttpClient surfaces its own timeout as a cancellation, so this is the path an unreachable-but-accepting host takes.")]
	public void IsReachable_ShouldReturnFalse_WhenTheRequestTimesOut() {
		// Arrange
		using StubHttpMessageHandler handler = StubHttpMessageHandler.Throwing(
			new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));
		IEnvironmentAvailabilityProbe probe = CreateProbe(handler);

		// Act
		bool reachable = probe.IsReachable(TimeSpan.FromSeconds(5), CancellationToken.None);

		// Assert
		reachable.Should().BeFalse(
			because: "a host that accepts the connection and never answers is unreachable for this purpose");
	}

	[Test]
	[Description("An already-cancelled token stops the probe rather than running it, which is what makes Stop() prompt instead of waiting out the last in-flight probe.")]
	public void IsReachable_ShouldReturnFalse_WhenCancelled() {
		// Arrange
		using StubHttpMessageHandler handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK);
		IEnvironmentAvailabilityProbe probe = CreateProbe(handler);
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		// Act
		bool reachable = probe.IsReachable(TimeSpan.FromSeconds(5), cancellation.Token);

		// Assert
		reachable.Should().BeFalse(
			because: "an abandoned probe has observed nothing, so it must not report the environment as answering");
	}

	[Test]
	[Description("THE TIMEOUT IS ENFORCED ON THE CLIENT. This class exists only because the IApplicationClient path did not honour the bound it was given - a 60-second read measured at 100 seconds - so the bound passed in must land on the HttpClient that issues the request. Asserted on the client rather than by waiting, so the check is deterministic on every OS.")]
	public void IsReachable_ShouldApplyTheGivenTimeoutToTheClient() {
		// Arrange
		using StubHttpMessageHandler handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK);
		List<HttpClient> createdClients = [];
		IEnvironmentAvailabilityProbe probe = CreateProbe(handler, createdClients);
		TimeSpan timeout = TimeSpan.FromSeconds(7);

		// Act
		probe.IsReachable(timeout, CancellationToken.None);

		// Assert
		createdClients.Should().ContainSingle(
			because: "the probe issues exactly one request per call");
		createdClients[0].Timeout.Should().Be(timeout,
			because: "a probe sampled every few seconds is useless if its real granularity is the client's default minute and a half");
	}

	[Test]
	[Description("The probed URL is the verdict endpoint itself, so 'the environment answers again' means exactly 'the verdict is readable'. Probing any other route would let the reload be reported before the endpoint the result is read from has come back.")]
	public void IsReachable_ShouldProbeTheVerdictEndpoint() {
		// Arrange
		using StubHttpMessageHandler handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK);
		IEnvironmentAvailabilityProbe probe = CreateProbe(handler);

		// Act
		probe.IsReachable(TimeSpan.FromSeconds(5), CancellationToken.None);

		// Assert
		handler.LastRequestUri.Should().Be(ProbeUrl,
			because: "availability has to be measured on the endpoint the compilation verdict is read from");
		handler.LastMethod.Should().Be(HttpMethod.Get,
			because: "the status line is the whole answer, so the probe must stay a cheap read");
	}

	[Test]
	[Description("The probe resolves its OWN named client, never the default one. The default registration validates server certificates while every other request in this feature goes through creatio.client, which does not - so on a self-signed stand the default client would fail the probe permanently, turn EnvironmentReachable off and disable both completion rules.")]
	public void IsReachable_ShouldUseTheDedicatedClientRegistration() {
		// Arrange
		using StubHttpMessageHandler handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK);
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.LastCompilationResult).Returns(ProbeUrl);
		IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();
		httpClientFactory.CreateClient(Arg.Any<string>())
			.Returns(_ => new HttpClient(handler, disposeHandler: false));
		IEnvironmentAvailabilityProbe probe = new EnvironmentAvailabilityProbe(httpClientFactory, serviceUrlBuilder);

		// Act
		probe.IsReachable(TimeSpan.FromSeconds(5), CancellationToken.None);

		// Assert
		httpClientFactory.Received(1).CreateClient(EnvironmentAvailabilityProbe.HttpClientName);
		httpClientFactory.DidNotReceive().CreateClient(string.Empty);
	}

	private static IEnvironmentAvailabilityProbe CreateProbe(HttpMessageHandler handler,
		List<HttpClient> createdClients = null) {
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.LastCompilationResult).Returns(ProbeUrl);
		IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();
		httpClientFactory.CreateClient(EnvironmentAvailabilityProbe.HttpClientName).Returns(_ => {
			HttpClient client = new(handler, disposeHandler: false);
			createdClients?.Add(client);
			return client;
		});
		return new EnvironmentAvailabilityProbe(httpClientFactory, serviceUrlBuilder);
	}

	/// <summary>
	/// Answers every request with one fixed status, or throws one fixed exception, and records what it
	/// was asked for.
	/// </summary>
	private sealed class StubHttpMessageHandler : HttpMessageHandler {

		private readonly HttpStatusCode _status;
		private readonly Exception _failure;

		private StubHttpMessageHandler(HttpStatusCode status, Exception failure) {
			_status = status;
			_failure = failure;
		}

		public string LastRequestUri { get; private set; }

		public HttpMethod LastMethod { get; private set; }

		public static StubHttpMessageHandler Returning(HttpStatusCode status) => new(status, null);

		public static StubHttpMessageHandler Throwing(Exception failure) =>
			new(HttpStatusCode.OK, failure);

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
			CancellationToken cancellationToken) {
			cancellationToken.ThrowIfCancellationRequested();
			LastRequestUri = request.RequestUri?.ToString();
			LastMethod = request.Method;
			return _failure is null
				? Task.FromResult(new HttpResponseMessage(_status))
				: Task.FromException<HttpResponseMessage>(_failure);
		}

	}

}
