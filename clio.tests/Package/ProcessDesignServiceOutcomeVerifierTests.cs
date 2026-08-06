using System;
using System.Linq;
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

	/// <summary>A successful ListUserTasks envelope, as ProcessDesignService actually returns it.</summary>
	private const string ServiceAnswersResponse =
		"{\"ListUserTasksResult\":{\"errorMessage\":null,\"success\":true,"
		+ "\"userTasks\":[{\"name\":\"ActivityUserTask\",\"uid\":\"b5c726f2-af5b-4381-bac6-913074144308\"}]}}";

	private const string ListUserTasksUrl = "http://localhost/0/rest/ProcessDesignService/ListUserTasks";

	private const string PackageName = "CrtProcessBuilder";

	#endregion

	#region Fields: Private

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ILogger _logger;
	private ProcessDesignServiceOutcomeVerifier _verifier;

	#endregion

	#region Methods: Public

	[SetUp]
	public void Setup() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ListUserTasks).Returns(ListUserTasksUrl);
		_verifier = new ProcessDesignServiceOutcomeVerifier(_applicationClient, _serviceUrlBuilder, _logger);
	}

	[TearDown]
	public void TearDown() {
		_applicationClient.ClearReceivedCalls();
		_serviceUrlBuilder.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("Reports the package operational when the service returns a successful ListUserTasks envelope.")]
	public void IsPackageOperational_ShouldReturnTrue_WhenServiceReturnsSuccessfulEnvelope() {
		// Arrange
		_applicationClient
			.ExecutePostRequest(ListUserTasksUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ServiceAnswersResponse);

		// Act
		bool operational = _verifier.IsPackageOperational(PackageName, out string diagnosis);

		// Assert
		operational.Should().BeTrue(
			because: "a successful envelope is positive evidence that the target compiled the package and the "
				+ "assembly is loaded and serving");
		diagnosis.Should().BeNull(
			because: "there is nothing to explain on the positive answer, and a non-null diagnosis would be "
				+ "printed by the caller as if something were wrong");
	}

	[Test]
	[Description("Reports the package NOT operational, and logs the cause at error level, when the route answers with an HTML error page.")]
	public void IsPackageOperational_ShouldReturnFalseAndLogTheCause_WhenRouteReturnsHtml() {
		// Arrange
		// An IIS error page is exactly what an unbound route returns — the shape a package that installed but
		// was never compiled produces, which is the whole reason this verification exists.
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("<!DOCTYPE html><html><head><title>404 - Not Found</title></head></html>");

		// Act
		bool operational = _verifier.IsPackageOperational(PackageName, out string diagnosis);

		// Assert
		operational.Should().BeFalse(
			because: "the response is not a parseable envelope, so nothing proves the package works — and this "
				+ "check must fail CLOSED, since reporting success is what makes an uncompiled package look "
				+ "like a healthy install");
		diagnosis.Should().BeNull(
			because: "the verifier knows only that nothing answered, so the caller's generic message — which "
				+ "sends the reader to the configuration build log — is the right report here");
		_logger.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(ILogger.WriteError))
			.Should().Be(1,
				because: "the parse failure carries WHY the probe failed, and it belongs at error level: the "
					+ "caller writes its summary as an error, so a cause logged below that level is invisible to "
					+ "anyone filtering on errors");
	}

	[Test]
	[Description("Reports the package NOT operational when the envelope is well-formed but says success:false, and hands back the service's own message as the diagnosis.")]
	public void IsPackageOperational_ShouldReturnFalseWithDiagnosis_WhenEnvelopeReportsFailure() {
		// Arrange
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"ListUserTasksResult\":{\"errorMessage\":\"You don't have permission for operation "
				+ "CanManageProcessDesign\",\"success\":false}}");

		// Act
		bool operational = _verifier.IsPackageOperational(PackageName, out string diagnosis);

		// Assert
		operational.Should().BeFalse(
			because: "a parseable response is not the same as a working package; only success:true proves it");
		diagnosis.Should().NotBeNull(
			because: "a PARSEABLE success:false envelope proves the opposite of a build failure — the assembly "
				+ "exists and is serving, and the failure is inside it. Letting the caller print its generic "
				+ "message would send the reader to a configuration build log that is clean");
		diagnosis.Should().Contain("CanManageProcessDesign",
			because: "the service's own message is the only statement of what it objected to, and the likeliest "
				+ "cause is the process-design right, which installing a package does not grant");
		diagnosis.Should().Contain("not a build failure",
			because: "the reader must be told explicitly not to go looking in the build log");
		diagnosis.Should().Contain("re-installing will NOT help",
			because: "the caller returns a failure exit code here, which an agent reads as retryable — so the "
					+ "message has to say that a retry cannot fix a permission problem");
		diagnosis.Should().Contain(PackageName,
			because: "the verifier is package-agnostic, so the package it was asked about must appear in the "
				+ "message rather than a hardcoded name");
	}

	[Test]
	[Description("Reports the package NOT operational when the response parses as JSON but carries no ListUserTasksResult.")]
	public void IsPackageOperational_ShouldReturnFalse_WhenEnvelopeIsMissing() {
		// Arrange
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"somethingElse\":true}");

		// Act
		bool operational = _verifier.IsPackageOperational(PackageName, out string diagnosis);

		// Assert
		operational.Should().BeFalse(
			because: "valid JSON from the wrong responder is not evidence about this package — a reverse proxy "
				+ "or login redirect can return a well-formed body");
		diagnosis.Should().BeNull(
			because: "nothing in the response says what went wrong, so there is no diagnosis to add");
	}

	[Test]
	[Description("Reports the package NOT operational, without throwing, when the call itself fails.")]
	public void IsPackageOperational_ShouldReturnFalse_WhenTheCallThrows() {
		// Arrange
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => throw new WebException("The remote server returned an error: (401) Unauthorized."));

		// Act
		bool operational = _verifier.IsPackageOperational(PackageName, out string diagnosis);

		// Assert
		operational.Should().BeFalse(
			because: "an unreachable service is not a working one, and the verdict must not escape as an "
				+ "exception — the caller turns a false into a readable failure, whereas a throw would surface "
				+ "as an unexpected clio error");
		diagnosis.Should().BeNull(
			because: "the exception text goes to the log as the cause; it is not a caller-facing diagnosis");
		_logger.Received().WriteError(Arg.Is<string>(message => message.Contains("401")));
	}

	[Test]
	[Description("Bounds every probe attempt, because this call decides the install command's exit code and the client would otherwise wait forever.")]
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
			.Returns(ServiceAnswersResponse);

		// Act
		_verifier.IsPackageOperational(PackageName, out string _);

		// Assert
		capturedTimeout.Should().BeGreaterThan(0,
			because: "ExecutePostRequest defaults to Timeout.Infinite, so an instance that accepts the "
				+ "connection but stalls behind the configuration-build lock would hang the CLI with no output "
				+ "and no way out but Ctrl+C — every other probe in the flow is bounded");
		capturedAttempts.Should().BeGreaterThan(1,
			because: "the readiness wait the caller performs is weaker than this question: it proves the host "
				+ "answers a health check, which a still-draining worker does too. One attempt would report "
				+ "'never compiled' about an environment that answers correctly seconds later");
		capturedDelay.Should().BeGreaterThan(0,
			because: "retrying immediately would spend all attempts inside the same warm-up window and prove "
				+ "nothing more than the first one did");
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
			"the cause of a failed probe is only ever reported through the logger");
	}

	#endregion

}
