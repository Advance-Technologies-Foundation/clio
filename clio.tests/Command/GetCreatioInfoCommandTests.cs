using System;
using System.Collections.Generic;
using System.Net.Http;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class GetCreatioInfoCommandTests
{

	#region Constants: Private

	private const string AppInfoResponse =
		"""{ "applicationInfo": { "sysValues": { "coreVersion": "10.1.89.0" } } }""";

	#endregion

	#region Fields: Private

	private readonly List<string> _writtenLines = [];
	private readonly List<string> _warnings = [];

	#endregion

	#region Methods: Private

	private GetCreatioInfoCommand CreateCommand(IApplicationClient client) {
		IClioGateway gateway = Substitute.For<IClioGateway>();
		// Force the no-cliogate fallback path (cliogate absent / older than the GetSysInfo minimum).
		gateway.IsCompatibleWith(Arg.Any<string>()).Returns(false);
		ILogger logger = Substitute.For<ILogger>();
		logger.When(l => l.WriteLine(Arg.Any<string>())).Do(ci => _writtenLines.Add(ci.Arg<string>()));
		logger.When(l => l.WriteWarning(Arg.Any<string>())).Do(ci => _warnings.Add(ci.Arg<string>()));
		EnvironmentSettings env = new() { Uri = "https://creatio.test", IsNetCore = true };
		return new GetCreatioInfoCommand(client, env, gateway) { Logger = logger };
	}

	private static IApplicationClient SubstituteClient(Action<IApplicationClient> configureEnvInfo) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(
				Arg.Is<string>(u => u.Contains("GetApplicationInfo")),
				Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(AppInfoResponse);
		configureEnvInfo(client);
		return client;
	}

	#endregion

	#region Methods: Public

	[TearDown]
	public void ClearCaptured() {
		_writtenLines.Clear();
		_warnings.Clear();
	}

	[Test]
	[Description("When cliogate is absent, describe enriches the ApplicationInfoService report with dbEngineType and framework from GetSystemEnvironmentInfo (admin-gated, no cliogate) and exits 0.")]
	public void Execute_ShouldEnrichWithDbEngineAndFramework_WhenCliogateAbsentAndSystemEnvironmentInfoSucceeds() {
		// Arrange
		IApplicationClient client = SubstituteClient(c =>
			c.ExecutePostRequest(
					Arg.Is<string>(u => u.Contains("GetSystemEnvironmentInfo")),
					Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
				.Returns("""{ "success": true, "dbEngineType": "PostgreSql", "frameworkKind": "Net", "frameworkDescription": ".NET 8.0.11" }"""));
		GetCreatioInfoCommand command = CreateCommand(client);

		// Act
		int exitCode = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		exitCode.Should().Be(0,
			because: "the no-cliogate fallback, enriched or not, is a successful describe");
		_writtenLines.Should().Contain(s => s.Contains("dbEngineType") && s.Contains("PostgreSql") && s.Contains("frameworkKind"),
			because: "the database engine and framework from GetSystemEnvironmentInfo must be merged into the reported output");
		_warnings.Should().Contain(s => s.Contains("GetSystemEnvironmentInfo") && s.Contains("LicenseInfo"),
			because: "the warning must state DbEngineType/Runtime came from GetSystemEnvironmentInfo and only LicenseInfo/ProductName still need cliogate");
	}

	[Test]
	[Description("When GetSystemEnvironmentInfo is denied (no CanManageSolution) or absent (older Creatio), describe degrades gracefully: still exits 0 with the limited-info warning and no db/framework fields.")]
	public void Execute_ShouldDegradeGracefully_WhenSystemEnvironmentInfoUnavailable() {
		// Arrange
		IApplicationClient client = SubstituteClient(c =>
			c.ExecutePostRequest(
					Arg.Is<string>(u => u.Contains("GetSystemEnvironmentInfo")),
					Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
				.Throws(new HttpRequestException("403 Forbidden - CanManageSolution required")));
		GetCreatioInfoCommand command = CreateCommand(client);

		// Act
		int exitCode = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		exitCode.Should().Be(0,
			because: "a gated/absent GetSystemEnvironmentInfo must not fail describe - it degrades to ApplicationInfoService");
		_warnings.Should().Contain(s => s.Contains("limited info") && s.Contains("DbEngineType"),
			because: "without the enrichment the warning must say DbEngineType/Runtime require cliogate");
		_writtenLines.Should().NotContain(s => s.Contains("dbEngineType"),
			because: "no database engine field is reported when GetSystemEnvironmentInfo could not be read");
	}

	[Test]
	[Description("When GetSystemEnvironmentInfo responds with success:false the report is not enriched and describe still exits 0.")]
	public void Execute_ShouldNotEnrich_WhenSystemEnvironmentInfoReportsFailure() {
		// Arrange
		IApplicationClient client = SubstituteClient(c =>
			c.ExecutePostRequest(
					Arg.Is<string>(u => u.Contains("GetSystemEnvironmentInfo")),
					Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
				.Returns("""{ "success": false }"""));
		GetCreatioInfoCommand command = CreateCommand(client);

		// Act
		int exitCode = command.Execute(new GetCreatioInfoCommandOptions());

		// Assert
		exitCode.Should().Be(0,
			because: "a success:false environment-info response is a soft miss, not a describe failure");
		_writtenLines.Should().NotContain(s => s.Contains("dbEngineType"),
			because: "a success:false response must not contribute any db/framework fields");
	}

	#endregion

}
