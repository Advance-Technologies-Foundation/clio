using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
internal class FindAppCommandTests : BaseCommandTests<FindAppOptions> {
	private const string SelectUrl = "http://localhost/select";
	private FindAppCommand _command;
	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<FindAppCommand>();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		containerBuilder.AddSingleton(_applicationClient);
		containerBuilder.AddSingleton(_serviceUrlBuilder);
		containerBuilder.AddSingleton(_logger);
	}

	[Test]
	[Description("FindApplications joins sections to their owning application and returns them together in a single result.")]
	public void FindApplications_ReturnsAppsWithTheirSections_WhenResponsesContainRows() {
		// Arrange
		const string appId = "11111111-1111-1111-1111-111111111111";
		ArrangeApps([new AppRow(appId, "CrtCaseManagementApp", "Case Management", "Customer service app", "1.0.0")]);
		ArrangeSections([new SectionRow("aaaaaaaa-0000-0000-0000-000000000001", appId, "Cases", "Cases", "All cases", "Case")]);
		FindAppOptions options = new();

		// Act
		IReadOnlyList<AppSearchResult> results = _command.FindApplications(options);

		// Assert
		results.Should().HaveCount(1,
			because: "the single installed application should be returned");
		results[0].Code.Should().Be("CrtCaseManagementApp",
			because: "the application code maps from the Code column");
		results[0].Sections.Should().ContainSingle()
			.Which.Caption.Should().Be("Cases",
				because: "the section joined by ApplicationId should be embedded in the same result");
	}

	[Test]
	[Description("FindApplications maps an imprecise term to the right application by matching a section caption (ENG-91275 acceptance scenario).")]
	public void FindApplications_MapsImpreciseTermToApp_WhenSectionCaptionMatchesPattern() {
		// Arrange
		const string caseAppId = "11111111-1111-1111-1111-111111111111";
		const string otherAppId = "22222222-2222-2222-2222-222222222222";
		ArrangeApps([
			new AppRow(caseAppId, "CrtCaseManagementApp", "Case Management", null, "1.0.0"),
			new AppRow(otherAppId, "UsrSalesApp", "Sales", null, "2.0.0")
		]);
		ArrangeSections([
			new SectionRow("aaaaaaaa-0000-0000-0000-000000000001", caseAppId, "Customer requests", "Cases", null, "Case"),
			new SectionRow("bbbbbbbb-0000-0000-0000-000000000002", otherAppId, "Opportunities", "Opportunities", null, "Opportunity")
		]);
		FindAppOptions options = new() { SearchPattern = "Customer Request" };

		// Act
		IReadOnlyList<AppSearchResult> results = _command.FindApplications(options);

		// Assert
		results.Should().ContainSingle(
			because: "only the application whose section caption contains the term should match");
		results[0].Code.Should().Be("CrtCaseManagementApp",
			because: "a section-caption match must resolve the imprecise term to the real application code");
	}

	[Test]
	[Description("FindApplications matches the pattern against the application name, code, and description as well as section captions.")]
	public void FindApplications_MatchesPatternAcrossApplicationFields_WhenPatternProvided() {
		// Arrange
		ArrangeApps([
			new AppRow("11111111-1111-1111-1111-111111111111", "CrtCaseManagementApp", "Case Management", null, "1.0.0"),
			new AppRow("22222222-2222-2222-2222-222222222222", "UsrSalesApp", "Sales", "Pipeline and deals", "2.0.0")
		]);
		ArrangeSections([]);
		FindAppOptions options = new() { SearchPattern = "pipeline" };

		// Act
		IReadOnlyList<AppSearchResult> results = _command.FindApplications(options);

		// Assert
		results.Should().ContainSingle(
			because: "the description of the Sales app contains the pattern");
		results[0].Code.Should().Be("UsrSalesApp",
			because: "a description match must select the owning application");
	}

	[Test]
	[Description("FindApplications returns every application (with sections) when no search pattern or code is supplied.")]
	public void FindApplications_ReturnsAllApplications_WhenNoFilterProvided() {
		// Arrange
		ArrangeApps([
			new AppRow("11111111-1111-1111-1111-111111111111", "CrtCaseManagementApp", "Case Management", null, "1.0.0"),
			new AppRow("22222222-2222-2222-2222-222222222222", "UsrSalesApp", "Sales", null, "2.0.0")
		]);
		ArrangeSections([]);
		FindAppOptions options = new();

		// Act
		IReadOnlyList<AppSearchResult> results = _command.FindApplications(options);

		// Assert
		results.Should().HaveCount(2,
			because: "an empty search must enumerate every installed application");
	}

	[Test]
	[Description("FindApplications narrows the result to one application when an exact code is supplied.")]
	public void FindApplications_FiltersByExactCode_WhenCodeProvided() {
		// Arrange
		ArrangeApps([
			new AppRow("11111111-1111-1111-1111-111111111111", "CrtCaseManagementApp", "Case Management", null, "1.0.0"),
			new AppRow("22222222-2222-2222-2222-222222222222", "UsrSalesApp", "Sales", null, "2.0.0")
		]);
		ArrangeSections([]);
		FindAppOptions options = new() { Code = "usrsalesapp" };

		// Act
		IReadOnlyList<AppSearchResult> results = _command.FindApplications(options);

		// Assert
		results.Should().ContainSingle(
			because: "the exact code filter is case-insensitive and matches a single application");
		results[0].Code.Should().Be("UsrSalesApp",
			because: "only the application with the requested code should be returned");
	}

	[Test]
	[Description("FindApplications issues exactly two DataService queries regardless of application count, avoiding an N+1 per-application section scan.")]
	public void FindApplications_IssuesExactlyTwoQueries_RegardlessOfApplicationCount() {
		// Arrange
		ArrangeApps([
			new AppRow("11111111-1111-1111-1111-111111111111", "AppOne", "App One", null, "1.0.0"),
			new AppRow("22222222-2222-2222-2222-222222222222", "AppTwo", "App Two", null, "1.0.0"),
			new AppRow("33333333-3333-3333-3333-333333333333", "AppThree", "App Three", null, "1.0.0")
		]);
		ArrangeSections([]);
		FindAppOptions options = new();

		// Act
		_command.FindApplications(options);

		// Assert
		_applicationClient.ReceivedWithAnyArgs(2).ExecutePostRequest(default, default);
		_applicationClient.Received(1).ExecutePostRequest(SelectUrl, Arg.Is<string>(body => body.Contains("SysInstalledApp")));
		_applicationClient.Received(1).ExecutePostRequest(SelectUrl, Arg.Is<string>(body => body.Contains("ApplicationSection")));
	}

	[Test]
	[Description("Execute returns 0 and logs the application with its sections when matches are found.")]
	public void Execute_ReturnsZeroAndLogsApplications_WhenApplicationsFound() {
		// Arrange
		const string appId = "11111111-1111-1111-1111-111111111111";
		ArrangeApps([new AppRow(appId, "CrtCaseManagementApp", "Case Management", null, "1.0.0")]);
		ArrangeSections([new SectionRow("aaaaaaaa-0000-0000-0000-000000000001", appId, "Cases", "Cases", null, "Case")]);
		FindAppOptions options = new() { SearchPattern = "case" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "a successful search returns the success exit code");
		_logger.Received(1).WriteInfo(Arg.Is<string>(line => line.Contains("Case Management") && line.Contains("CrtCaseManagementApp")));
	}

	[Test]
	[Description("Execute returns 0 and logs a not-found message when no applications match the search.")]
	public void Execute_ReturnsZeroAndLogsNotFound_WhenNoApplicationsMatch() {
		// Arrange
		ArrangeApps([new AppRow("11111111-1111-1111-1111-111111111111", "CrtCaseManagementApp", "Case Management", null, "1.0.0")]);
		ArrangeSections([]);
		FindAppOptions options = new() { SearchPattern = "doesnotexist" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "an empty result is not an error");
		_logger.Received(1).WriteInfo(Arg.Is<string>(line => line.Contains("No applications found")));
	}

	[Test]
	[Description("Execute returns 1 and logs an error when the DataService query fails.")]
	public void Execute_ReturnsOneAndLogsError_WhenQueryFails() {
		// Arrange
		_applicationClient
			.ExecutePostRequest(SelectUrl, Arg.Any<string>())
			.Returns(JsonSerializer.Serialize(new { success = false, errorInfo = new { message = "boom" } }));
		FindAppOptions options = new();

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1,
			because: "a failed DataService query must surface as a non-zero exit code");
		_logger.Received(1).WriteError(Arg.Any<string>());
	}

	private void ArrangeApps(IEnumerable<AppRow> rows) {
		_applicationClient
			.ExecutePostRequest(SelectUrl, Arg.Is<string>(body => body.Contains("SysInstalledApp")))
			.Returns(BuildSuccessJson(rows.Cast<object>()));
	}

	private void ArrangeSections(IEnumerable<SectionRow> rows) {
		_applicationClient
			.ExecutePostRequest(SelectUrl, Arg.Is<string>(body => body.Contains("ApplicationSection")))
			.Returns(BuildSuccessJson(rows.Cast<object>()));
	}

	private static string BuildSuccessJson(IEnumerable<object> rows) =>
		JsonSerializer.Serialize(new { success = true, rows });

	private sealed record AppRow(string Id, string Code, string Name, string? Description, string Version);

	private sealed record SectionRow(
		string Id,
		string ApplicationId,
		string Caption,
		string Code,
		string? Description,
		string EntitySchemaName);
}
