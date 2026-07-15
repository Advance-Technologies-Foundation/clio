using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Author("Kirill Krylov", "k.krylov@creatio.com")]
[Category("Unit")]
[TestFixture]
[Property("Module", "Command")]
public class GetInfoCommandTests : BaseCommandTests<GetCreatioInfoCommandOptions>
{
	private const string ApplicationInfoMarker = "ApplicationInfoService.svc/GetApplicationInfo";
	private const string SystemEnvironmentInfoMarker = "ApplicationInfoService.svc/GetSystemEnvironmentInfo";
	private const string GetSysInfoMarker = "rest/CreatioApiGateway/GetSysInfo";

	private const string ApplicationInfoResponse =
		"""{ "applicationInfo": { "sysValues": { "coreVersion": "8.3.3.3292" } } }""";

	// The base ApplicationInfoService.GetApplicationInfo (POST) is always answered with the rich base.
	private static IApplicationClient SubstituteClient()
	{
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains(ApplicationInfoMarker)),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ApplicationInfoResponse);
		return client;
	}

	private static void StubSystemEnvironmentInfo(IApplicationClient client, string response) =>
		client.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains(SystemEnvironmentInfoMarker)),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(response);

	private static void StubCliogateSysInfo(IApplicationClient client, string response) =>
		client.ExecuteGetRequest(
				Arg.Is<string>(u => u.Contains(GetSysInfoMarker)), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(response);

	private static GetCreatioInfoCommand CreateCommand(IApplicationClient client, IClioGateway gateway,
		ILogger logger = null, string uri = "https://creatio.test")
	{
		EnvironmentSettings env = new() { Uri = uri, IsNetCore = true };
		return new GetCreatioInfoCommand(client, env, gateway) { Logger = logger ?? Substitute.For<ILogger>() };
	}

	[Test]
	[Description("Without a cliogate gateway the command reports the ApplicationInfoService base, never calls cliogate GetSysInfo, and returns success.")]
	public void Execute_ReportsBase_When_Cliogate_Absent()
	{
		// Arrange — no cliogate gateway at all.
		IApplicationClient client = SubstituteClient();
		GetCreatioInfoCommand command = CreateCommand(client, gateway: null);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(0,
			because: "the command must not fail when cliogate is missing — ApplicationInfoService still answers");
		client.Received().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains(ApplicationInfoMarker)),
			Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		client.DidNotReceive().ExecuteGetRequest(
			Arg.Is<string>(url => url.Contains(GetSysInfoMarker)), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("When the installed cliogate is older than required the command reports the ApplicationInfoService base rather than erroring out, and does not call cliogate GetSysInfo.")]
	public void Execute_ReportsBase_When_Cliogate_Incompatible()
	{
		// Arrange — cliogate present but below the required version.
		IClioGateway gateway = Substitute.For<IClioGateway>();
		gateway.IsCompatibleWith(Arg.Any<string>()).Returns(false);
		IApplicationClient client = SubstituteClient();
		GetCreatioInfoCommand command = CreateCommand(client, gateway);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(0,
			because: "an incompatible cliogate must not be a hard failure — the ApplicationInfoService base is still reported");
		client.Received().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains(ApplicationInfoMarker)),
			Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		client.DidNotReceive().ExecuteGetRequest(
			Arg.Is<string>(url => url.Contains(GetSysInfoMarker)), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Without cliogate, the ApplicationInfoService base is enriched with dbEngineType and framework from the admin-gated GetSystemEnvironmentInfo (no cliogate), and the command returns success.")]
	public void Execute_EnrichesDbAndFramework_When_SystemEnvironmentInfo_Succeeds()
	{
		// Arrange
		IApplicationClient client = SubstituteClient();
		StubSystemEnvironmentInfo(client,
			"""{ "success": true, "dbEngineType": "PostgreSql", "frameworkKind": "Net", "frameworkDescription": ".NET 8.0.11" }""");
		ILogger logger = Substitute.For<ILogger>();
		GetCreatioInfoCommand command = CreateCommand(client, gateway: null, logger);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(0,
			because: "GetSystemEnvironmentInfo enrichment without cliogate is a successful describe");
		logger.Received().WriteLine(Arg.Is<string>(s =>
			s.Contains("coreVersion") && s.Contains("dbEngineType") && s.Contains("PostgreSql") && s.Contains("frameworkKind")));
	}

	[Test]
	[Description("When a compatible cliogate is installed the command merges the cliogate-only productName and licenseInfo into the same report alongside the ApplicationInfoService base, and returns success.")]
	public void Execute_MergesProductNameAndLicenseInfo_When_Cliogate_Compatible()
	{
		// Arrange — compatible cliogate present; both the base and the cliogate report are answered.
		IClioGateway gateway = Substitute.For<IClioGateway>();
		gateway.IsCompatibleWith(Arg.Any<string>()).Returns(true);
		IApplicationClient client = SubstituteClient();
		StubCliogateSysInfo(client,
			"""{ "SysInfo": { "ProductName": "studio", "LicenseInfo": { "IsDemoMode": true }, "DbEngineType": "PostgreSql", "Runtime": ".NET 8.0.11", "IsNetCore": true } }""");
		ILogger logger = Substitute.For<ILogger>();
		GetCreatioInfoCommand command = CreateCommand(client, gateway, logger);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(0,
			because: "the cliogate path now contributes to the same unified report and still succeeds");
		client.Received().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains(ApplicationInfoMarker)),
			Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		client.Received().ExecuteGetRequest(
			Arg.Is<string>(url => url.Contains(GetSysInfoMarker)), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		logger.Received().WriteLine(Arg.Is<string>(s =>
			s.Contains("productName") && s.Contains("studio") && s.Contains("licenseInfo")));
	}

	[Test]
	[Description("On Creatio without the GetSystemEnvironmentInfo operation but with cliogate, the command backfills dbEngineType and framework from the cliogate SysInfo so the contract stays consistent.")]
	public void Execute_BackfillsDbAndFramework_FromCliogate_When_SystemEnvironmentInfo_Missing()
	{
		// Arrange — compatible cliogate; GetSystemEnvironmentInfo absent (throws); cliogate provides db/framework.
		IClioGateway gateway = Substitute.For<IClioGateway>();
		gateway.IsCompatibleWith(Arg.Any<string>()).Returns(true);
		IApplicationClient client = SubstituteClient();
		client.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains(SystemEnvironmentInfoMarker)),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new HttpRequestException("404 Not Found"));
		StubCliogateSysInfo(client,
			"""{ "SysInfo": { "ProductName": "studio", "DbEngineType": "MSSql", "Runtime": ".NET Framework 4.8", "IsNetCore": false } }""");
		ILogger logger = Substitute.For<ILogger>();
		GetCreatioInfoCommand command = CreateCommand(client, gateway, logger);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(0, because: "cliogate backfill keeps describe successful");
		logger.Received().WriteLine(Arg.Is<string>(s =>
			s.Contains("dbEngineType") && s.Contains("MSSql") && s.Contains("frameworkKind") && s.Contains("NetFramework")));
	}

	[Test]
	[Description("When both GetSystemEnvironmentInfo and a compatible cliogate report a (conflicting) dbEngineType/framework, the admin-gated GetSystemEnvironmentInfo value wins - the cliogate path backfills ABSENT fields only and never overwrites - and the merged fields sit at the report root.")]
	public void Execute_PrefersSystemEnvironmentInfo_Over_CliogateBackfill_When_Both_Present()
	{
		// Arrange — compatible cliogate AND a working GetSystemEnvironmentInfo, with CONFLICTING db/framework.
		IClioGateway gateway = Substitute.For<IClioGateway>();
		gateway.IsCompatibleWith(Arg.Any<string>()).Returns(true);
		IApplicationClient client = SubstituteClient();
		StubSystemEnvironmentInfo(client,
			"""{ "success": true, "dbEngineType": "PostgreSql", "frameworkKind": "Net", "frameworkDescription": ".NET 8.0.11" }""");
		StubCliogateSysInfo(client,
			"""{ "SysInfo": { "ProductName": "studio", "LicenseInfo": { "IsDemoMode": true }, "DbEngineType": "MSSql", "Runtime": ".NET Framework 4.8", "IsNetCore": false } }""");
		string captured = null;
		ILogger logger = Substitute.For<ILogger>();
		logger.When(l => l.WriteLine(Arg.Any<string>())).Do(ci => captured = ci.Arg<string>());
		GetCreatioInfoCommand command = CreateCommand(client, gateway, logger);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(0,
			because: "a successful describe with both enrichment sources still returns success");
		JObject report = JObject.Parse(captured);
		report["dbEngineType"]?.Value<string>().Should().Be("PostgreSql",
			because: "GetSystemEnvironmentInfo runs first and the cliogate backfill must only fill ABSENT fields, never overwrite a value the admin-gated source already set");
		report["frameworkKind"]?.Value<string>().Should().Be("Net",
			because: "the admin-gated framework value must win over the cliogate-derived IsNetCore mapping");
		report["productName"]?.Value<string>().Should().Be("studio",
			because: "cliogate-only fields are still merged from GetSysInfo");
		report.Should().ContainKey("dbEngineType",
			because: "enrichment fields are merged at the report root, not nested");
	}

	[Test]
	[Description("When the base ApplicationInfoService returns an unexpected shape the command reports a clean failure.")]
	public void Execute_Returns_Error_When_ApplicationInfo_Shape_Unexpected()
	{
		// Arrange — ApplicationInfoService gives an unusable body (no sysValues).
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains(ApplicationInfoMarker)),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{ "applicationInfo": { } }""");
		ILogger logger = Substitute.For<ILogger>();
		GetCreatioInfoCommand command = CreateCommand(client, gateway: null, logger);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(1,
			because: "an unusable ApplicationInfoService base must surface as a clean failure, not a crash");
		logger.Received(1).WriteError("The Creatio ApplicationInfoService returned an unexpected response.");
	}

	[Test]
	[Description("Classifies an HTML base response as a reachable non-Creatio target without exposing HTML or parser details.")]
	public void Execute_ShouldReportNonCreatioTarget_WhenBaseResponseIsHtml()
	{
		// Arrange
		const string html = "<html><body>not-creatio-secret-marker</body></html>";
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(html);
		ILogger logger = Substitute.For<ILogger>();
		GetCreatioInfoCommand command = CreateCommand(client, gateway: null, logger, "https://google.com");

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(1, because: "a reachable non-Creatio target cannot produce an environment report");
		logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("does not appear to be a Creatio application", StringComparison.Ordinal)
			&& message.Contains("https://google.com", StringComparison.Ordinal)));
		logger.DidNotReceive().WriteError(Arg.Is<string>(message =>
			message.Contains("Unexpected character", StringComparison.Ordinal)
			|| message.Contains("not-creatio-secret-marker", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Classifies a plain non-JSON base response as a reachable non-Creatio target.")]
	public void Execute_ShouldReportNonCreatioTarget_WhenBaseResponseIsPlainText()
	{
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("plain text response");
		ILogger logger = Substitute.For<ILogger>();
		GetCreatioInfoCommand command = CreateCommand(client, gateway: null, logger);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(1, because: "plain non-JSON content is not a usable Creatio response");
		logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("does not appear to be a Creatio application", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Classifies digit-prefixed plain text as a reachable non-Creatio response rather than malformed JSON.")]
	public void Execute_ShouldReportNonCreatioTarget_WhenBaseResponseStartsWithDigit()
	{
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("404 Not Found");
		ILogger logger = Substitute.For<ILogger>();
		GetCreatioInfoCommand command = CreateCommand(client, gateway: null, logger);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(1, because: "digit-prefixed server text is non-JSON content from a reachable non-Creatio target");
		logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("does not appear to be a Creatio application", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Rejects a sysValues object without the required coreVersion base signature.")]
	public void Execute_ShouldReportUnexpectedResponse_WhenBaseReportHasNoCoreVersion()
	{
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{ "applicationInfo": { "sysValues": { } } }""");
		ILogger logger = Substitute.For<ILogger>();
		GetCreatioInfoCommand command = CreateCommand(client, gateway: null, logger);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(1, because: "an empty sysValues object cannot establish a usable Creatio base report");
		logger.Received(1).WriteError("The Creatio ApplicationInfoService returned an unexpected response.");
	}

	[Test]
	[Description("Classifies malformed JSON from ApplicationInfoService with the stable unexpected-response error.")]
	public void Execute_ShouldReportUnexpectedResponse_WhenBaseJsonIsMalformed()
	{
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"applicationInfo\":");
		ILogger logger = Substitute.For<ILogger>();
		GetCreatioInfoCommand command = CreateCommand(client, gateway: null, logger);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(1, because: "malformed JSON cannot be trusted as a Creatio report");
		logger.Received(1).WriteError("The Creatio ApplicationInfoService returned an unexpected response.");
		logger.DidNotReceive().WriteError(Arg.Is<string>(message => message.Contains("JsonReaderException", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Classifies a truncated quoted JSON scalar as malformed rather than non-Creatio plain text.")]
	public void Execute_ShouldReportUnexpectedResponse_WhenQuotedBaseJsonIsMalformed()
	{
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("\"unterminated");
		ILogger logger = Substitute.For<ILogger>();
		GetCreatioInfoCommand command = CreateCommand(client, gateway: null, logger);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(1, because: "a JSON-token prefix with invalid syntax is a malformed response");
		logger.Received(1).WriteError("The Creatio ApplicationInfoService returned an unexpected response.");
	}

	[Test]
	[Description("Maps an unexpected recoverable base-client exception to a stable secret-safe response.")]
	public void Execute_ShouldKeepUnexpectedBaseFailureSecretSafe_WhenClientThrowsRecoverableException()
	{
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new InvalidOperationException("token=base-secret"));
		ILogger logger = Substitute.For<ILogger>();
		GetCreatioInfoCommand command = CreateCommand(client, gateway: null, logger);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(1, because: "recoverable library failures cannot bypass the classified command boundary");
		logger.Received(1).WriteError("The Creatio ApplicationInfoService returned an unexpected response.");
		logger.DidNotReceive().WriteError(Arg.Is<string>(message => message.Contains("base-secret", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Keeps the base report when system-environment enrichment returns an invalid success value.")]
	public void Execute_ShouldReturnBaseReport_WhenSystemEnvironmentInfoSuccessValueIsMalformed()
	{
		// Arrange
		IApplicationClient client = SubstituteClient();
		StubSystemEnvironmentInfo(client, """{ "success": "not-a-bool", "dbEngineType": "secret-value" }""");
		ILogger logger = Substitute.For<ILogger>();
		GetCreatioInfoCommand command = CreateCommand(client, gateway: null, logger);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(0, because: "malformed optional enrichment cannot invalidate a valid base report");
		logger.Received(1).WriteLine(Arg.Is<string>(message =>
			message.Contains("coreVersion", StringComparison.Ordinal)
			&& !message.Contains("secret-value", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Classifies an HTTP transport exception as an unavailable Creatio target.")]
	public void Execute_ShouldReportConnectionFailure_WhenBaseProbeThrowsHttpRequestException()
	{
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new HttpRequestException("secret transport implementation detail"));
		ILogger logger = Substitute.For<ILogger>();
		GetCreatioInfoCommand command = CreateCommand(client, gateway: null, logger);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(1, because: "an unreachable target cannot produce a base Creatio report");
		logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Could not connect to the Creatio application", StringComparison.Ordinal)
			&& !message.Contains("secret transport", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Classifies a request timeout as an unavailable Creatio target.")]
	public void Execute_ShouldReportConnectionFailure_WhenBaseProbeTimesOut()
	{
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new TaskCanceledException("secret timeout detail"));
		ILogger logger = Substitute.For<ILogger>();
		GetCreatioInfoCommand command = CreateCommand(client, gateway: null, logger);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(1, because: "a timed-out base probe did not establish a Creatio report");
		logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Could not connect to the Creatio application", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Classifies rejected basic-auth credentials separately from non-Creatio and transport failures.")]
	public void Execute_ShouldReportAuthenticationFailure_WhenBaseProbeRejectsCredentials()
	{
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new UnauthorizedAccessException("Unauthorized secret-user for secret-url"));
		ILogger logger = Substitute.For<ILogger>();
		GetCreatioInfoCommand command = CreateCommand(client, gateway: null, logger);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(1, because: "invalid credentials are a caller-actionable authentication failure");
		logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Authentication failed", StringComparison.Ordinal)
			&& message.Contains("Verify the credentials", StringComparison.Ordinal)
			&& !message.Contains("secret-user", StringComparison.Ordinal)));
	}

	[TestCase(HttpStatusCode.Unauthorized)]
	[TestCase(HttpStatusCode.Forbidden)]
	[Description("Classifies HTTP authentication status codes separately from transport and non-Creatio failures.")]
	public void Execute_ShouldReportAuthenticationFailure_WhenBaseProbeReturnsAuthenticationStatus(
		HttpStatusCode status)
	{
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new HttpRequestException("secret HTTP detail", null, status));
		ILogger logger = Substitute.For<ILogger>();
		GetCreatioInfoCommand command = CreateCommand(client, gateway: null, logger);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(1, because: "HTTP 401/403 means the caller must correct authentication settings");
		logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Authentication failed", StringComparison.Ordinal)
			&& !message.Contains("secret HTTP detail", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Classifies Creatio's session-expired HTML response as authentication failure before parsing.")]
	public void Execute_ShouldReportAuthenticationFailure_WhenBaseResponseIsLoginPage()
	{
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("<html><form action=\"/0/Login/NuiLogin.aspx\">secret-login-body</form></html>");
		ILogger logger = Substitute.For<ILogger>();
		GetCreatioInfoCommand command = CreateCommand(client, gateway: null, logger);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(1, because: "a Creatio login page means authentication failed, not that the URL is non-Creatio");
		logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Authentication failed", StringComparison.Ordinal)));
		logger.DidNotReceive().WriteError(Arg.Is<string>(message => message.Contains("secret-login-body", StringComparison.Ordinal)));
	}

	[TestCase("ftp://files.example.test")]
	[TestCase("not-a-uri")]
	[Description("Rejects malformed and unsupported application URIs before sending the base probe.")]
	public void Execute_ShouldRejectUriBeforeProbe_WhenUriIsNotHttpOrHttps(string uri)
	{
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		ILogger logger = Substitute.For<ILogger>();
		GetCreatioInfoCommand command = CreateCommand(client, gateway: null, logger, uri);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(1, because: "ApplicationInfoService is supported only over absolute HTTP or HTTPS URLs");
		logger.Received(1).WriteError("The application URL is invalid. Use an absolute HTTP or HTTPS URL.");
		client.DidNotReceive().ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[NonParallelizable]
	[Description("Debug diagnostics expose only safe classification metadata and redact URI user-info, query values, exception messages, and response bodies.")]
	public void Execute_ShouldKeepDebugDiagnosticsSecretSafe_WhenBaseProbeFails()
	{
		// Arrange
		bool originalDebugMode = Program.IsDebugMode;
		Program.IsDebugMode = true;
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new AggregateException(new HttpRequestException("token=exception-secret")));
		ILogger logger = Substitute.For<ILogger>();
		GetCreatioInfoCommand command = CreateCommand(client, gateway: null, logger,
			"https://user:password@creatio.test/site?token=query-secret#fragment-secret");

		try {
			// Act
			int result = command.Execute(new GetCreatioInfoCommandOptions());

			// Assert
			result.Should().Be(1, because: "the wrapped transport failure is still an unavailable target");
			logger.Received(1).WriteError(Arg.Is<string>(message =>
				message.Contains("https://creatio.test", StringComparison.Ordinal)
				&& !message.Contains("user", StringComparison.Ordinal)
				&& !message.Contains("password", StringComparison.Ordinal)
				&& !message.Contains("/site", StringComparison.Ordinal)
				&& !message.Contains("query-secret", StringComparison.Ordinal)));
			logger.Received(1).WriteDebug(Arg.Is<string>(message =>
				message.Contains("classification=Connection", StringComparison.Ordinal)
				&& message.Contains(nameof(HttpRequestException), StringComparison.Ordinal)
				&& !message.Contains("exception-secret", StringComparison.Ordinal)
				&& !message.Contains("query-secret", StringComparison.Ordinal)));
		} finally {
			Program.IsDebugMode = originalDebugMode;
		}
	}

	[Test]
	[Description("Keeps a successful base report when the optional ClioGate compatibility check fails.")]
	public void Execute_ShouldReturnBaseReport_WhenCliogateCompatibilityCheckFails()
	{
		// Arrange
		IApplicationClient client = SubstituteClient();
		IClioGateway gateway = Substitute.For<IClioGateway>();
		gateway.IsCompatibleWith(Arg.Any<string>()).Throws(new InvalidOperationException("secret cliogate failure"));
		ILogger logger = Substitute.For<ILogger>();
		GetCreatioInfoCommand command = CreateCommand(client, gateway, logger);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(0, because: "optional ClioGate compatibility cannot invalidate a successful base report");
		logger.Received(1).WriteLine(Arg.Is<string>(message => message.Contains("coreVersion", StringComparison.Ordinal)));
		logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("compatibility could not be determined", StringComparison.Ordinal)));
		logger.DidNotReceive().WriteError(Arg.Any<string>());
	}

	[Test]
	[Description("Keeps a successful base report when ClioGate package-list JSON cannot be deserialized.")]
	public void Execute_ShouldReturnBaseReport_WhenCliogateCompatibilityJsonIsMalformed()
	{
		// Arrange
		IApplicationClient client = SubstituteClient();
		IClioGateway gateway = Substitute.For<IClioGateway>();
		gateway.IsCompatibleWith(Arg.Any<string>())
			.Throws(new System.Text.Json.JsonException("token=cliogate-json-secret"));
		ILogger logger = Substitute.For<ILogger>();
		GetCreatioInfoCommand command = CreateCommand(client, gateway, logger);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(0, because: "package-list JSON is optional after the base report succeeds");
		logger.Received(1).WriteLine(Arg.Is<string>(message => message.Contains("coreVersion", StringComparison.Ordinal)));
		logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("compatibility could not be determined", StringComparison.Ordinal)
			&& !message.Contains("cliogate-json-secret", StringComparison.Ordinal)));
		logger.DidNotReceive().WriteError(Arg.Any<string>());
	}
}
