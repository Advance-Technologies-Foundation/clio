using System;
using System.Collections.Generic;
using System.Linq;
using ATF.Repository.Mock;
using Clio.Command;
using CreatioModel;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class InstalledApplicationQueryServiceTests {
	private sealed record ApplicationRow(Guid Id, string Name, string Code, string Version, string Description);

	[Test]
	[Description("Returns all installed applications when no filters are provided.")]
	public void GetApplications_Should_Return_All_Applications_When_No_Filters_Are_Provided() {
		// Arrange
		DataProviderMock dataProvider = CreateProvider(
			CreateApplication(Guid.NewGuid(), "Beta", "BETA", "2.0.0"),
			CreateApplication(Guid.NewGuid(), "Alpha", "ALPHA", "1.0.0"));
		InstalledApplicationQueryService service = new(dataProvider);

		// Act
		IReadOnlyList<SysInstalledApp> result = service.GetApplications();

		// Assert
		result.Should().HaveCount(2,
			because: "the unfiltered installed-application query should return every SysInstalledApp row");
	}

	[Test]
	[Description("Filters installed applications by application id when a valid app-id is provided.")]
	public void GetApplications_Should_Filter_By_AppId() {
		// Arrange
		Guid applicationId = Guid.NewGuid();
		DataProviderMock dataProvider = CreateProvider(
			CreateApplication(applicationId, "Alpha", "ALPHA", "1.0.0"),
			CreateApplication(Guid.NewGuid(), "Beta", "BETA", "2.0.0"));
		InstalledApplicationQueryService service = new(dataProvider);

		// Act
		IReadOnlyList<SysInstalledApp> result = service.GetApplications(new InstalledApplicationQuery(
			AppId: applicationId.ToString(),
			AppCode: null));

		// Assert
		result.Should().ContainSingle(because: "the app-id filter should narrow the result to one installed application");
		result[0].Code.Should().Be("ALPHA",
			because: "the filtered result should preserve the matching installed application");
	}

	[Test]
	[Description("Returns an empty result when the provided app-id cannot be parsed as a guid.")]
	public void GetApplications_Should_Return_Empty_When_AppId_Is_Invalid() {
		// Arrange
		DataProviderMock dataProvider = CreateProvider(CreateApplication(Guid.NewGuid(), "Alpha", "ALPHA", "1.0.0"));
		InstalledApplicationQueryService service = new(dataProvider);

		// Act
		IReadOnlyList<SysInstalledApp> result = service.GetApplications(new InstalledApplicationQuery(
			AppId: "not-a-guid",
			AppCode: null));

		// Assert
		result.Should().BeEmpty(
			because: "an invalid app-id cannot match any installed application and should not throw for MCP callers");
	}

	[Test]
	[Description("Filters installed applications by application code using a case-insensitive comparison.")]
	public void GetApplications_Should_Filter_By_AppCode_Case_Insensitively() {
		// Arrange
		DataProviderMock dataProvider = CreateProvider(
			CreateApplication(Guid.NewGuid(), "Alpha", "ALPHA", "1.0.0"),
			CreateApplication(Guid.NewGuid(), "Beta", "BETA", "2.0.0"));
		InstalledApplicationQueryService service = new(dataProvider);

		// Act
		IReadOnlyList<SysInstalledApp> result = service.GetApplications(new InstalledApplicationQuery(
			AppId: null,
			AppCode: "beta"));

		// Assert
		result.Should().ContainSingle(because: "the app-code filter should match regardless of letter casing");
		result[0].Name.Should().Be("Beta",
			because: "the filtered result should preserve the matching installed application name");
	}

	[Test]
	[Description("Applies application id and application code filters conjunctively when both are provided.")]
	public void GetApplications_Should_Apply_Both_Filters_Conjunctively() {
		// Arrange
		Guid matchingId = Guid.NewGuid();
		DataProviderMock dataProvider = CreateProvider(
			CreateApplication(matchingId, "Alpha", "ALPHA", "1.0.0"),
			CreateApplication(Guid.NewGuid(), "Alpha Duplicate", "ALPHA", "2.0.0"));
		InstalledApplicationQueryService service = new(dataProvider);

		// Act
		IReadOnlyList<SysInstalledApp> result = service.GetApplications(new InstalledApplicationQuery(
			AppId: matchingId.ToString(),
			AppCode: "ALPHA"));

		// Assert
		result.Should().ContainSingle(
			because: "providing both filters should keep only rows that match both the id and the code");
		result[0].Id.Should().Be(matchingId,
			because: "the combined filters should preserve only the exact matching installed application");
	}

	[Test]
	[Description("Returns an empty result when the optional filters do not match any installed application.")]
	public void GetApplications_Should_Return_Empty_When_Filters_Match_Nothing() {
		// Arrange
		DataProviderMock dataProvider = CreateProvider(CreateApplication(Guid.NewGuid(), "Alpha", "ALPHA", "1.0.0"));
		InstalledApplicationQueryService service = new(dataProvider);

		// Act
		IReadOnlyList<SysInstalledApp> result = service.GetApplications(new InstalledApplicationQuery(
			AppId: null,
			AppCode: "MISSING"));

		// Assert
		result.Should().BeEmpty(
			because: "query callers should receive an empty list when no installed application matches the filters");
	}


	private static DataProviderMock CreateProvider(params ApplicationRow[] applications) {
		DataProviderMock dataProvider = new();
		var mock = dataProvider.MockItems(nameof(SysInstalledApp));
		mock.Returns(applications.Select(application => new Dictionary<string, object> {
			["Id"] = application.Id,
			["Name"] = application.Name,
			["Code"] = application.Code,
			["Version"] = application.Version,
			["Description"] = application.Description
		}).ToList());
		return dataProvider;
	}

	private static ApplicationRow CreateApplication(Guid id, string name, string code, string version) {
		return new ApplicationRow(id, name, code, version, $"{name} description");
	}
}
