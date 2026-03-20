using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ATF.Repository.Mock;
using Clio.Command;
using Clio.Common;
using ConsoleTables;
using CreatioModel;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Author("Kirill Krylov", "k.krylov@creatio.com")]
[TestFixture]
public class ListInstalledAppsCommandTests : BaseCommandTests<ListInstalledAppsOptions> {
	[Test]
	[Description("Renders the existing CLI table output for installed applications without changing get-app-list behavior.")]
	public void Execute_Should_Render_Table_Output() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		IInstalledApplicationQueryService installedApplicationQueryService =
			Substitute.For<IInstalledApplicationQueryService>();
		SysInstalledApp installedApplication = CreateApplications((
			Guid.NewGuid(),
			"Fake name",
			"FakeCode",
			"1.0.0",
			"Fake description"))[0];
		installedApplicationQueryService.GetApplications(Arg.Any<InstalledApplicationQuery?>()).Returns([
			installedApplication
		]);
		ListInstalledAppsCommand command = new(Substitute.For<ATF.Repository.Providers.IDataProvider>(), logger,
			installedApplicationQueryService);
		ListInstalledAppsOptions options = new();

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "get-app-list should still succeed when installed applications are available");
		logger.Received(1).PrintTable(Arg.Is<ConsoleTable>(table =>
			table.Rows.Count == 1
			&& (string)table.Rows[0].GetValue(0) == "Fake name"
			&& (string)table.Rows[0].GetValue(1) == "FakeCode"
			&& (string)table.Rows[0].GetValue(2) == "1.0.0"
			&& (string)table.Rows[0].GetValue(3) == "Fake description"));
	}

	[Test]
	[Description("Renders the existing CLI JSON output for installed applications without changing get-app-list behavior.")]
	public void Execute_Should_Render_Json_Output() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		IInstalledApplicationQueryService installedApplicationQueryService =
			Substitute.For<IInstalledApplicationQueryService>();
		SysInstalledApp installedApplication = CreateApplications((
			Guid.Parse("11111111-1111-1111-1111-111111111111"),
			"Fake name",
			"FakeCode",
			"1.0.0",
			"Fake description"))[0];
		installedApplicationQueryService.GetApplications(Arg.Any<InstalledApplicationQuery?>()).Returns([
			installedApplication
		]);
		ListInstalledAppsCommand command = new(Substitute.For<ATF.Repository.Providers.IDataProvider>(), logger,
			installedApplicationQueryService);
		ListInstalledAppsOptions options = new() {
			JsonFormat = true
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "get-app-list should still succeed when JSON output is requested");
		logger.Received(1).Write(Arg.Is<string>(json => IsExpectedJsonPayload(json)));
	}

	private static IReadOnlyList<SysInstalledApp> CreateApplications(
		params (Guid Id, string Name, string Code, string Version, string Description)[] applications) {
		DataProviderMock dataProvider = new();
		var mock = dataProvider.MockItems(nameof(SysInstalledApp));
		mock.Returns(applications.Select(application => new Dictionary<string, object> {
			["Id"] = application.Id,
			["Name"] = application.Name,
			["Code"] = application.Code,
			["Version"] = application.Version,
			["Description"] = application.Description
		}).ToList());
		InstalledApplicationQueryService service = new(dataProvider);
		return service.GetApplications();
	}

	private static bool IsExpectedJsonPayload(string json) {
		List<JsonInstalledApplication>? payload = JsonSerializer.Deserialize<List<JsonInstalledApplication>>(json);
		return payload is [{ Name: "Fake name", Code: "FakeCode", Version: "1.0.0", Description: "Fake description" }];
	}

	private sealed class JsonInstalledApplication {
		public string Name { get; set; } = string.Empty;

		public string Code { get; set; } = string.Empty;

		public string Version { get; set; } = string.Empty;

		public string Description { get; set; } = string.Empty;
	}
}
