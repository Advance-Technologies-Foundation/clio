using System;
using System.Net;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package;

[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public class ProcessDesignServiceOutcomeVerifierTests {

	#region Constants: Private

	private const string PingUrl = "http://localhost/0/rest/ProcessDesignService/Ping";

	private const string PackageName = "CrtProcessBuilder";

	#endregion

	#region Fields: Private

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ILogger _logger;
	private ProcessDesignServiceOutcomeVerifier _verifier;

	#endregion

	#region Methods: Private

	/// <summary>The Ping envelope as the shipped service returns it (BodyStyle = Wrapped).</summary>
	private static string PingResponse(bool success = true) =>
		$"{{\"PingResult\":{{\"success\":{success.ToString().ToLowerInvariant()}}}}}";

	private void ArrangeResponse(string response) =>
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(response);

	#endregion

	#region Methods: Public

	[SetUp]
	public void Setup() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder
			.Build(ServiceUrlBuilder.KnownRoute.ProcessBuilderPing)
			.Returns(PingUrl);
		_verifier = new ProcessDesignServiceOutcomeVerifier(_applicationClient, _serviceUrlBuilder, _logger);
	}

	[TearDown]
	public void TearDown() {
		_applicationClient.ClearReceivedCalls();
		_serviceUrlBuilder.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("Reports the package operational when its own service answers, since for a source-only package a reachable route is the only evidence the target compiled it.")]
	public void IsPackageOperational_ShouldReturnTrue_WhenTheServiceAnswers() {
		// Arrange
		ArrangeResponse(PingResponse());

		// Act
		bool operational = _verifier.IsPackageOperational(PackageName, out string diagnosis);

		// Assert
		operational.Should().BeTrue(
			because: "Creatio registers services by reflecting over LOADED types, so an answer on this route "
				+ "proves an assembly exists — which no database read can establish, since SysPackage records the "
				+ "version that was ACCEPTED whether or not anything compiled. Note the deliberate limit: this is "
				+ "LIVENESS, not identity. A stale assembly left by a failed configuration build on an UPGRADE "
				+ "also answers, and therefore also passes. Reporting the shipped version back would require a "
				+ "hand-maintained copy of it inside the package sources — the assembly version is the "
				+ "platform's, and descriptor.json is absent from the target's build directory, both measured — "
				+ "and that duplicate was judged more expensive than the upgrade case it would catch");
		diagnosis.Should().BeNull(
			because: "a positive verdict has nothing to explain");
	}

	[Test]
	[Description("Reports NOT operational when the route answers with an HTML error page, which is what an unresolved route returns.")]
	public void IsPackageOperational_ShouldReturnFalse_WhenRouteReturnsHtml() {
		// Arrange
		// An IIS error page is exactly what an unbound route returns — the shape of an install whose
		// configuration build produced no assembly, so no ProcessDesignService type, so no route to answer.
		ArrangeResponse("<!DOCTYPE html><html><head><title>404 - Not Found</title></head></html>");

		// Act
		bool operational = _verifier.IsPackageOperational(PackageName, out string diagnosis);

		// Assert
		operational.Should().BeFalse(
			because: "the response is not a parseable envelope, so nothing is serving — and this check must fail "
				+ "CLOSED, since reporting success is what makes an uncompiled package look like a healthy "
				+ "install");
		diagnosis.Should().BeNull(
			because: "the verifier knows only that nothing answered, so the caller's generic message — which "
				+ "sends the reader to the configuration build log and to the compile-marker schema — is the "
				+ "right report here");
	}

	[Test]
	[Description("Reports NOT operational when the response parses as JSON but is not the Ping envelope.")]
	public void IsPackageOperational_ShouldReturnFalse_WhenEnvelopeIsMissing() {
		// Arrange
		ArrangeResponse("{\"somethingElse\":true}");

		// Act
		bool operational = _verifier.IsPackageOperational(PackageName, out string diagnosis);

		// Assert
		operational.Should().BeFalse(
			because: "valid JSON from the wrong responder is not evidence about this package — a reverse proxy "
				+ "or a login redirect can return a well-formed body");
		diagnosis.Should().NotBeNull(
			because: "the caller's fallback message blames the configuration build, and that is the WRONG cause "
				+ "here: something answered this route, so the build is not implicated. Leaving the diagnosis "
				+ "null sent an operator to the build log for a proxy problem");
		diagnosis.Should().Contain(PackageName,
			because: "the reader needs to know which package the verdict is about");
		diagnosis.Should().Contain("somethingElse",
			because: "quoting what actually answered is what lets the reader recognise the responder — a "
				+ "diagnosis that only asserts 'something else answered' cannot be acted on");
		diagnosis.Should().Contain("NOT implicated",
			because: "the message must actively CLEAR the configuration build, not merely omit it. Asserting the "
				+ "absence of the words 'configuration build' cannot express that — the sentence doing the "
				+ "clearing necessarily contains them, which is the same trap as guarding a claim by banning a "
				+ "phrase instead of asserting the claim");
	}

	[Test]
	[Description("Reports NOT operational when the envelope carries no success flag, rather than treating an absent field as agreement.")]
	public void IsPackageOperational_ShouldReturnFalse_WhenSuccessFieldIsMissing() {
		// Arrange
		ArrangeResponse("{\"PingResult\":{}}");

		// Act
		bool operational = _verifier.IsPackageOperational(PackageName, out string diagnosis);

		// Assert
		operational.Should().BeFalse(
			because: "the envelope name alone can be produced by something other than the shipped build, so the "
				+ "flag must be present and true — an absent field must never be read as agreement");
		diagnosis.Should().NotBeNull(
			because: "this case is distinguishable from 'nothing answered' and leads somewhere else: the route "
				+ "IS answering in this package's envelope, so the reader must not be sent to the build log");
	}

	[Test]
	[Description("Reports NOT operational when the envelope explicitly says success:false, so a negative answer is never read as a positive one.")]
	public void IsPackageOperational_ShouldReturnFalse_WhenSuccessIsFalse() {
		// Arrange
		ArrangeResponse(PingResponse(success: false));

		// Act
		bool operational = _verifier.IsPackageOperational(PackageName, out string diagnosis);

		// Assert
		operational.Should().BeFalse(
			because: "the shipped operation cannot return false today, but this check must not depend on that — a "
				+ "future build that reports a problem through the flag must not be read as healthy");
		diagnosis.Should().NotBeNull(
			because: "the shipped Ping returns a constant true, so a false is evidence that the serving build is "
				+ "not the one clio ships — a conclusion the caller's generic build-failure message would hide");
	}

	[Test]
	[Description("Reports NOT operational, without throwing, when the call itself fails.")]
	public void IsPackageOperational_ShouldReturnFalse_WhenTheCallThrows() {
		// Arrange
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => throw new WebException("The remote server returned an error: (503) Server Unavailable."));

		// Act
		bool operational = _verifier.IsPackageOperational(PackageName, out string diagnosis);

		// Assert
		operational.Should().BeFalse(
			because: "an unreachable service is not a working one, and the verdict must not escape as an "
				+ "exception — the caller turns a false into a readable failure, whereas a throw would surface "
				+ "as an unexpected clio error");
		diagnosis.Should().BeNull(
			because: "the exception text goes to the log as the cause; it is not a caller-facing diagnosis");
		_logger.Received().WriteError(Arg.Is<string>(message => message.Contains("503")));
	}

	[Test]
	[Description("Bounds and retries the probe, because this call decides the install command's exit code and the client would otherwise wait forever.")]
	public void IsPackageOperational_ShouldBoundTheProbe_AndRetryIt() {
		// Arrange
		int capturedTimeout = 0;
		int capturedAttempts = 0;
		int capturedDelay = 0;
		_applicationClient
			.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Any<string>(),
				Arg.Do<int>(timeout => capturedTimeout = timeout),
				Arg.Do<int>(attempts => capturedAttempts = attempts),
				Arg.Do<int>(delay => capturedDelay = delay))
			.Returns(PingResponse());

		// Act
		_verifier.IsPackageOperational(PackageName, out string _);

		// Assert
		capturedTimeout.Should().BeGreaterThan(0,
			because: "ExecutePostRequest defaults to Timeout.Infinite, so an instance that accepts the "
				+ "connection but stalls behind the configuration-build lock would hang the CLI with no output "
				+ "and no way out but Ctrl+C — every other probe in the flow is bounded");
		capturedAttempts.Should().BeGreaterThan(1,
			because: "the readiness wait the caller performs is weaker than this question: it proves the host "
				+ "answers a health check, which a still-draining worker does too. One attempt would report an "
				+ "uncompiled package about an environment that answers correctly seconds later");
		capturedDelay.Should().BeGreaterThan(0,
			because: "retrying immediately would spend all attempts inside the same warm-up window and prove "
				+ "nothing more than the first one did");
	}

	[Test]
	[Description("Calls the ungated Ping route rather than a gated functional operation.")]
	public void IsPackageOperational_ShouldProbeTheUngatedPingRoute() {
		// Arrange
		ArrangeResponse(PingResponse());

		// Act
		_verifier.IsPackageOperational(PackageName, out string _);

		// Assert
		// NSubstitute's Received() takes no `because`; stated here. The route choice is the design: Ping is
		// ungated on the package side, so this check answers the INSTALL question alone. The predecessor probed
		// ListUserTasks, which is behind CanManageProcessDesign + General user, and so conflated "the build did
		// not take" with "you may not design processes" — two problems with different fixes, and only the first
		// is this command's business.
		_serviceUrlBuilder.Received().Build(ServiceUrlBuilder.KnownRoute.ProcessBuilderPing);
		_serviceUrlBuilder.DidNotReceive().Build(ServiceUrlBuilder.KnownRoute.ListUserTasks);
	}

	[Test]
	[Description("Rejects null collaborators, so a misconfigured DI graph fails at construction rather than mid-install.")]
	public void Constructor_ShouldRejectNullCollaborators() {
		// Arrange, Act & Assert
		Assert.Throws<ArgumentNullException>(
			() => new ProcessDesignServiceOutcomeVerifier(null, _serviceUrlBuilder, _logger),
			"the verifier cannot answer anything without a client, and failing here names the missing "
			+ "dependency instead of throwing a NullReferenceException after the package is already installed");
		Assert.Throws<ArgumentNullException>(
			() => new ProcessDesignServiceOutcomeVerifier(_applicationClient, null, _logger),
			"without a url builder there is no route to probe");
		Assert.Throws<ArgumentNullException>(
			() => new ProcessDesignServiceOutcomeVerifier(_applicationClient, _serviceUrlBuilder, null),
			"without a logger the cause of a failed probe would be lost");
	}

	#endregion

}
