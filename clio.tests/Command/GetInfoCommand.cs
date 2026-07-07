using System.Net.Http;
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

	private static GetCreatioInfoCommand CreateCommand(IApplicationClient client, IClioGateway gateway, ILogger logger = null)
	{
		EnvironmentSettings env = new() { Uri = "https://creatio.test", IsNetCore = true };
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
		GetCreatioInfoCommand command = CreateCommand(client, gateway: null);

		// Act
		int result = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		result.Should().Be(1,
			because: "an unusable ApplicationInfoService base must surface as a clean failure, not a crash");
	}
}
