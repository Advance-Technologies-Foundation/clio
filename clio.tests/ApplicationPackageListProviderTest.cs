using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Common;
using Clio.Package;
using Clio.Tests.Command;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests;

[TestFixture(Category = "UnitTests")]
internal class ApplicationPackageListProviderTest
{
	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ApplicationPackageListProvider _sut;

	[SetUp]
	public void Setup() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>())
			.Returns("http://localhost/0/DataService/json/SyncReply/SelectQuery");
		_sut = new ApplicationPackageListProvider(_applicationClient, _serviceUrlBuilder);
	}

	private static string BuildSelectQueryResponse(params (string name, string uid, string? maintainer, string? version)[] packages) {
		var rows = packages.Select(p => new Dictionary<string, object?>
		{
			["Name"] = p.name,
			["UId"] = p.uid,
			["Maintainer"] = p.maintainer ?? string.Empty,
			["Version"] = p.version ?? string.Empty
		}).ToList();
		return JsonSerializer.Serialize(new { success = true, rows });
	}

	[Test]
	[Description("GetPackages selects Type and ProjectPath and maps them onto the descriptor of an assembly package")]
	public void GetPackages_MapsTypeAndProjectPath_WhenTheEnvironmentStoresThem() {
		// Arrange
		string response = JsonSerializer.Serialize(new {
			success = true,
			rows = new object[] {
				new { Name = "UsrAsm", UId = "00000000-0000-0000-0000-000000000001", Maintainer = "Customer",
					Version = "1.0.0", Type = 1, ProjectPath = "Files/UsrAsm.csproj" },
				new { Name = "UsrPlain", UId = "00000000-0000-0000-0000-000000000002", Maintainer = "Customer",
					Version = "1.0.0", Type = 0, ProjectPath = "" }
			}
		});
		string requestBody = null;
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(body => requestBody = body))
			.Returns(response);

		// Act
		List<PackageInfo> result = _sut.GetPackages().ToList();

		// Assert
		requestBody.Should().Contain("\"ProjectPath\"", "because the read must select the stored project path");
		requestBody.Should().Contain("\"Type\"", "because the read must select the stored package type");
		result[0].Descriptor.Type.Should().Be(PackageType.Assembly, "because SysPackage.Type 1 is an assembly package");
		result[0].Descriptor.ProjectPath.Should().Be("Files/UsrAsm.csproj", "because the stored project path is reported as is");
		result[0].IsTypeKnown.Should().BeTrue("because the environment returned the Type column");
		result[1].Descriptor.Type.Should().Be(PackageType.General, "because SysPackage.Type 0 is an ordinary package");
		result[1].Descriptor.ProjectPath.Should().BeEmpty("because an ordinary package has no project");
	}

	[Test]
	[Description("GetPackages falls back to the read without Type and ProjectPath when the environment refuses them, and reports the type as unknown")]
	public void GetPackages_FallsBackAndReportsUnknownType_WhenTypeColumnsAreRefused() {
		// Arrange
		string legacy = BuildSelectQueryResponse(("OldPkg", "00000000-0000-0000-0000-000000000003", "M", "1.0"));
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Is<string>(body => body.Contains("ProjectPath")))
			.Returns("{\"success\":false,\"errorInfo\":{\"message\":\"Column by path ProjectPath not found\"}}");
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Is<string>(body => !body.Contains("ProjectPath")))
			.Returns(legacy);

		// Act
		List<PackageInfo> result = _sut.GetPackages().ToList();

		// Assert
		result.Should().ContainSingle("because the fallback read still returns the package list");
		result[0].Descriptor.Name.Should().Be("OldPkg", "because the fallback read maps the base columns");
		result[0].IsTypeKnown.Should().BeFalse("because an environment without the Type column cannot report it");
	}

	[Test]
	[Description("GetPackages returns empty list when SelectQuery returns no rows")]
	public void GetPackages_ReturnsEmpty_WhenNoRows() {
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns("{\"success\":true,\"rows\":[]}");

		IEnumerable<PackageInfo> result = _sut.GetPackages();

		result.Should().BeEmpty("because the SelectQuery returned no rows");
	}

	[Test]
	[Description("GetPackages returns single package with correct name and UId")]
	public void GetPackages_ReturnsSinglePackage_WhenOneRow() {
		string response = BuildSelectQueryResponse(
			("TestPackage", "00000000-0000-0000-0000-000000000001", null, null));
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(response);

		List<PackageInfo> result = _sut.GetPackages().ToList();

		result.Should().HaveCount(1, "because SelectQuery returned one row");
		result[0].Descriptor.Name.Should().Be("TestPackage", "because that is the package name in the response");
		result[0].Descriptor.UId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000001"),
			"because that is the UId in the response");
	}

	[Test]
	[Description("GetPackages correctly maps Maintainer and Version fields")]
	public void GetPackages_MapsAllFields_WhenFullRow() {
		string response = BuildSelectQueryResponse(
			("MyPkg", "a0120c05-78fd-41e4-baf5-112ab9006c3e", "Creatio", "1.2.3"));
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(response);

		List<PackageInfo> result = _sut.GetPackages().ToList();

		result.Should().HaveCount(1, "because one package was returned");
		result[0].Descriptor.Maintainer.Should().Be("Creatio", "because Maintainer field was set");
		result[0].Descriptor.PackageVersion.Should().Be("1.2.3", "because Version field was set");
	}

	[Test]
	[Description("GetPackages returns multiple packages when SelectQuery returns multiple rows")]
	public void GetPackages_ReturnsMultiple_WhenMultipleRows() {
		string response = BuildSelectQueryResponse(
			("Pkg1", "00000000-0000-0000-0000-000000000001", null, null),
			("Pkg2", "00000000-0000-0000-0000-000000000002", "M", "2.0"),
			("Pkg3", "00000000-0000-0000-0000-000000000003", null, "3.0"));
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(response);

		List<PackageInfo> result = _sut.GetPackages().ToList();

		result.Should().HaveCount(3, "because SelectQuery returned three rows");
	}

	[Test]
	[Description("GetPackages throws when SelectQuery response indicates failure")]
	public void GetPackages_Throws_WhenResponseIndicatesFailure() {
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns("{\"success\":false,\"errorInfo\":{\"message\":\"Access denied\"}}");

		Action act = () => _sut.GetPackages();

		act.Should().Throw<InvalidOperationException>("because the SelectQuery returned success=false")
			.WithMessage("*Access denied*");
	}

	[Test]
	[Description("GetPackages throws when SelectQuery returns empty response")]
	public void GetPackages_Throws_WhenResponseIsEmpty() {
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(string.Empty);

		Action act = () => _sut.GetPackages();

		act.Should().Throw<InvalidOperationException>("because an empty response is invalid");
	}

	[Test]
	[Description("GetPackages uses SelectQuery endpoint, not cliogate")]
	public void GetPackages_UsesSelectQueryEndpoint() {
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns("{\"success\":true,\"rows\":[]}");

		_sut.GetPackages();

		_serviceUrlBuilder.Received(1).Build(ServiceUrlBuilder.KnownRoute.Select);
	}

	[Test]
	[Description("GetPackages with isCustomer filter includes InstallType filter in request body")]
	public void GetPackages_WithCustomerFilter_IncludesInstallTypeFilter() {
		string capturedBody = string.Empty;
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(body => capturedBody = body))
			.Returns("{\"success\":true,\"rows\":[]}");

		_sut.GetPackages("{\"isCustomer\": true}");

		capturedBody.Should().Contain("InstallType", "because isCustomer filter should add InstallType condition");
	}

	[Test]
	[Description("GetPackages with isCustomer as string true includes InstallType filter (WorkspaceCreator compat)")]
	public void GetPackages_WithCustomerFilterAsString_IncludesInstallTypeFilter() {
		string capturedBody = string.Empty;
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(body => capturedBody = body))
			.Returns("{\"success\":true,\"rows\":[]}");

		_sut.GetPackages("{\"isCustomer\": \"true\"}");

		capturedBody.Should().Contain("InstallType", "because string 'true' should be treated as boolean true for backward compatibility");
	}

	[Test]
	[Description("GetPackages handles null rows in response without throwing")]
	public void GetPackages_NullRowsInResponse_ReturnsEmpty() {
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns("{\"success\":true}");

		IEnumerable<PackageInfo> result = _sut.GetPackages();

		result.Should().BeEmpty("because null rows should be treated as empty collection");
	}
}
