using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using Clio.Package.Responses;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using WorkspacePackageDto = Clio.Package.WorkspacePackageDto;

namespace Clio.Tests.Package;

[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public class RemotePackageCreatorTests
{

	#region Constants: Private

	private const string CreateUrl = "https://stub/CreatePackage";
	private const string CreateInAppUrl = "https://stub/CreatePackageInApp";
	private const string PropertiesUrl = "https://stub/GetPackageProperties";

	#endregion

	#region Fields: Private

	private IApplicationClient _applicationClient;
	private IApplicationPackageListProvider _packageListProvider;
	private IPackageDependencyManager _dependencyManager;
	private ISysSettingsManager _sysSettingsManager;
	private IApplicationInfoService _applicationInfoService;
	private EnvironmentSettings _environmentSettings;
	private RemotePackageCreator _creator;
	private string _createUrl;
	private string _createBody;

	#endregion

	#region Setup/Teardown

	[SetUp]
	public void Init() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_packageListProvider = Substitute.For<IApplicationPackageListProvider>();
		_dependencyManager = Substitute.For<IPackageDependencyManager>();
		_sysSettingsManager = Substitute.For<ISysSettingsManager>();
		_applicationInfoService = Substitute.For<IApplicationInfoService>();
		_environmentSettings = new EnvironmentSettings();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.CreatePackage).Returns(CreateUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.CreatePackageInApp).Returns(CreateInAppUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetPackageProperties).Returns(PropertiesUrl);
		_sysSettingsManager.GetSysSettingValueByCode("SchemaNamePrefix").Returns("Usr");
		_packageListProvider.GetPackages("{}").Returns([Installed("CrtBase"), Installed("CrtUIv2")]);
		_createUrl = null;
		_createBody = null;
		_applicationClient.ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(call => {
				(_createUrl, _createBody) = (call.ArgAt<string>(0), call.ArgAt<string>(1));
				return "{\"success\":true}";
			});
		_creator = new RemotePackageCreator(_applicationClient, serviceUrlBuilder, _packageListProvider,
			_dependencyManager, _sysSettingsManager, _applicationInfoService, _environmentSettings);
	}

	#endregion

	#region Methods: Private

	private static PackageInfo Installed(string name) =>
		new(new PackageDescriptor { Name = name, UId = Guid.NewGuid(), PackageVersion = "1.0.0" }, string.Empty, []);

	private void ArrangeReadBack(string name, int installType, params string[] dependencies) {
		_applicationClient.ExecutePostRequest<PackagePropertiesResponse>(PropertiesUrl, Arg.Any<string>())
			.Returns(new PackagePropertiesResponse {
				Success = true,
				Package = new WorkspacePackageDto {
					Name = name,
					DependsOnPackages = dependencies.Select(d => new WorkspacePackageDto { Name = d }).ToList(),
					AdditionalData = new Dictionary<string, JToken> {
						["maintainer"] = "Customer",
						["description"] = "desc",
						["installType"] = installType
					}
				}
			});
	}

	private static RemotePackageCreateRequest Request(string name, string[] dependencies = null,
		string applicationCode = null) =>
		new(name, "desc", dependencies ?? [], applicationCode);

	#endregion

	[Test]
	[Description("Prepends the environment's SchemaNamePrefix and posts a general, required package to PackageService CreatePackage.")]
	public void Create_ShouldPrefixTheNameAndPostToCreatePackage_WhenNameHasNoPrefix() {
		// Arrange
		ArrangeReadBack("UsrCalls", 0);

		// Act
		RemotePackageCreateResult result = _creator.Create(Request("Calls"));

		// Assert
		result.PackageName.Should().Be("UsrCalls", because: "the environment prefix is added when the name lacks it");
		_createUrl.Should().Be(CreateUrl, because: "a package without an application code is standalone");
		JObject body = JObject.Parse(_createBody);
		body["name"]!.Value<string>().Should().Be("UsrCalls", because: "the prefixed name is what the platform stores");
		body["type"]!.Value<int>().Should().Be(0, because: "the Configuration section creates a general package");
		body["installBehavior"]!.Value<int>().Should().Be(0, because: "a new package is required, not optional");
		Guid.Parse(body["uId"]!.Value<string>()!).Should().Be(result.PackageUId,
			because: "the UId clio sends is the one it reads back and reports");
	}

	[Test]
	[Description("Keeps a name that already starts with the prefix in any letter case instead of prefixing it twice.")]
	public void Create_ShouldNotPrefixTwice_WhenNameAlreadyStartsWithPrefix() {
		// Arrange
		ArrangeReadBack("usrCalls", 0);

		// Act
		_creator.Create(Request("usrCalls"));

		// Assert
		JObject.Parse(_createBody)["name"]!.Value<string>().Should().Be("usrCalls",
			because: "prefixing an already-prefixed name would produce UsrusrCalls");
	}

	[Test]
	[Description("Uses the name as given when the environment has no SchemaNamePrefix.")]
	public void Create_ShouldUseTheNameAsGiven_WhenEnvironmentHasNoPrefix() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode("SchemaNamePrefix").Returns(string.Empty);
		ArrangeReadBack("Calls", 0);

		// Act
		_creator.Create(Request("Calls"));

		// Assert
		JObject.Parse(_createBody)["name"]!.Value<string>().Should().Be("Calls",
			because: "there is no prefix to add");
	}

	[Test]
	[Description("Refuses a name that already exists, matched case-insensitively after prefixing, without sending the create request.")]
	public void Create_ShouldRefuseAndCreateNothing_WhenPackageAlreadyExists() {
		// Arrange
		_packageListProvider.GetPackages("{}").Returns([Installed("UsrCalls")]);

		// Act
		Action act = () => _creator.Create(Request("calls"));

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "an existing package must never be overwritten")
			.WithMessage("*already exists*Nothing was created*");
		_createUrl.Should().BeNull(because: "the refusal happens before anything is written");
	}

	[Test]
	[Description("Refuses when a requested dependency is not installed, naming it, without sending the create request.")]
	public void Create_ShouldRefuseAndCreateNothing_WhenDependencyIsMissing() {
		// Act
		Action act = () => _creator.Create(Request("Calls", ["CrtBase", "NoSuchPkg"]));

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a package with a dangling dependency cannot be saved")
			.WithMessage("*NoSuchPkg*Nothing was created*");
		_createUrl.Should().BeNull(because: "dependencies are resolved before the package is created");
	}

	[Test]
	[Description("Relays the environment's refusal with its message when CreatePackage answers success=false.")]
	public void Create_ShouldThrowWithTheServerMessage_WhenEnvironmentRefuses() {
		// Arrange
		_applicationClient.ExecuteNonReplayablePostRequest(CreateUrl, Arg.Any<string>())
			.Returns("{\"success\":false,\"errorInfo\":{\"message\":\"Invalid package name\"}}");

		// Act
		Action act = () => _creator.Create(Request("Calls"));

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "the platform rejected the package")
			.WithMessage("*Invalid package name*");
	}

	[Test]
	[Description("Applies the dependencies with a second request after creation, because CreatePackage ignores dependsOnPackages, and reports the readback.")]
	public void Create_ShouldApplyDependenciesAfterCreation_AndReturnTheReadback() {
		// Arrange
		ArrangeReadBack("UsrCalls", 0, "CrtBase", "CrtUIv2");

		// Act
		RemotePackageCreateResult result = _creator.Create(Request("Calls", ["CrtBase", "crtuiv2", "CrtBase"]));

		// Assert
		_dependencyManager.Received(1).AddDependencies("UsrCalls",
			Arg.Is<IEnumerable<PackageDependencySpec>>(specs =>
				specs.Select(spec => spec.Name).SequenceEqual(new[] { "CrtBase", "crtuiv2" })));
		result.IncompleteReason.Should().BeNull(because: "every step succeeded");
		result.Dependencies.Should().Equal(["CrtBase", "CrtUIv2"], because: "dependencies come from the readback");
		result.Maintainer.Should().Be("Customer", because: "the maintainer is the one the environment assigned");
		result.Editable.Should().BeTrue(because: "InstallType 0 is an editable package");
	}

	[Test]
	[Description("Does not call the dependency manager when no dependencies are requested.")]
	public void Create_ShouldNotSaveDependencies_WhenNoneRequested() {
		// Arrange
		ArrangeReadBack("UsrCalls", 0);

		// Act
		_creator.Create(Request("Calls"));

		// Assert
		_dependencyManager.DidNotReceiveWithAnyArgs().AddDependencies(default, default);
	}

	[Test]
	[Description("Reports a created package with an incomplete reason, instead of throwing, when saving its dependencies fails.")]
	public void Create_ShouldReportIncomplete_WhenDependenciesFailAfterCreation() {
		// Arrange
		ArrangeReadBack("UsrCalls", 0);
		_dependencyManager.AddDependencies(Arg.Any<string>(), Arg.Any<IEnumerable<PackageDependencySpec>>())
			.Throws(new InvalidOperationException("save failed"));

		// Act
		RemotePackageCreateResult result = _creator.Create(Request("Calls", ["CrtBase"]));

		// Assert
		result.PackageName.Should().Be("UsrCalls", because: "the package exists and the caller must learn its name");
		result.IncompleteReason.Should().Contain("dependencies were not applied").And.Contain("save failed",
			because: "the caller must know the package exists without the dependencies it asked for");
	}

	[Test]
	[Description("Creates the package inside the application resolved from application-code via ApplicationPackagesService CreatePackageInApp.")]
	public void Create_ShouldPostToCreatePackageInApp_WhenApplicationCodeIsGiven() {
		// Arrange
		Guid appId = Guid.NewGuid();
		_applicationInfoService.FindApplicationId(_environmentSettings, "UsrApp")
			.Returns(new InstalledAppSummary(appId.ToString(), "UsrApp", "App", null));
		ArrangeReadBack("UsrCalls", 0);

		// Act
		RemotePackageCreateResult result = _creator.Create(Request("Calls", applicationCode: "UsrApp"));

		// Assert
		_createUrl.Should().Be(CreateInAppUrl, because: "an application code routes creation into that application");
		JObject body = JObject.Parse(_createBody);
		Guid.Parse(body["appId"]!.Value<string>()!).Should().Be(appId, because: "the resolved application receives the package");
		body["package"]!["name"]!.Value<string>().Should().Be("UsrCalls", because: "the package payload is nested");
		result.ApplicationCode.Should().Be("UsrApp", because: "the readback names the application");
	}

	[Test]
	[Description("Sends the create request through the non-replayable POST so an expired-session recovery cannot send it twice.")]
	public void Create_ShouldSendTheCreateRequestAsNonReplayable() {
		// Arrange
		ArrangeReadBack("UsrCalls", 0);

		// Act
		_creator.Create(Request("Calls"));

		// Assert
		_applicationClient.Received(1).ExecuteNonReplayablePostRequest(CreateUrl, Arg.Any<string>());
		_applicationClient.DidNotReceive().ExecutePostRequest<Clio.Common.Responses.BaseResponse>(CreateUrl, Arg.Any<string>());
	}

	[Test]
	[Description("Treats the package as created when the create request fails in transport but the readback finds the UId clio sent.")]
	public void Create_ShouldReportCreated_WhenTransportFailsButPackageExists() {
		// Arrange
		_applicationClient.ExecuteNonReplayablePostRequest(CreateUrl, Arg.Any<string>())
			.Throws(new System.Net.Http.HttpRequestException("connection reset"));
		ArrangeReadBack("UsrCalls", 0);

		// Act
		RemotePackageCreateResult result = _creator.Create(Request("Calls"));

		// Assert
		result.PackageName.Should().Be("UsrCalls", because: "the server stored the package before the connection dropped");
		result.IncompleteReason.Should().BeNull(because: "the readback confirmed the package");
	}

	[Test]
	[Description("Refuses when the create request fails in transport and the readback finds no package.")]
	public void Create_ShouldThrow_WhenTransportFailsAndPackageIsAbsent() {
		// Arrange
		_applicationClient.ExecuteNonReplayablePostRequest(CreateUrl, Arg.Any<string>())
			.Throws(new System.Net.Http.HttpRequestException("connection reset"));
		_applicationClient.ExecutePostRequest<PackagePropertiesResponse>(PropertiesUrl, Arg.Any<string>())
			.Returns(new PackagePropertiesResponse { Success = false });

		// Act
		Action act = () => _creator.Create(Request("Calls"));

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "no package exists after the failed request")
			.WithMessage("*connection reset*");
	}

	[Test]
	[Description("Reports a created package with an incomplete reason when the readback after creation fails, instead of reporting nothing created.")]
	public void Create_ShouldReportIncomplete_WhenReadbackFailsAfterCreation() {
		// Arrange
		_applicationClient.ExecutePostRequest<PackagePropertiesResponse>(PropertiesUrl, Arg.Any<string>())
			.Returns(new PackagePropertiesResponse { Success = false });

		// Act
		RemotePackageCreateResult result = _creator.Create(Request("Calls"));

		// Assert
		result.PackageName.Should().Be("UsrCalls", because: "the create request succeeded, so the package exists");
		result.IncompleteReason.Should().Contain("reading it back failed",
			because: "the caller must know the package exists although its state could not be read");
		result.InstallType.Should().BeNull(because: "nothing was read back");
	}

	[Test]
	[Description("Reports a package whose stored InstallType is not 0 as not editable.")]
	public void Create_ShouldReportNotEditable_WhenInstallTypeIsNotZero() {
		// Arrange
		ArrangeReadBack("UsrCalls", 1);

		// Act
		RemotePackageCreateResult result = _creator.Create(Request("Calls"));

		// Assert
		result.Editable.Should().BeFalse(because: "only InstallType 0 can receive design-time changes");
	}


	[Test]
	[Description("Reports an unknown outcome, not a refusal, when both the create request and the check after it fail in transport.")]
	public void Create_ShouldThrowOutcomeUnknown_WhenCreateAndCheckBothFailInTransport() {
		// Arrange
		_applicationClient.ExecuteNonReplayablePostRequest(CreateUrl, Arg.Any<string>())
			.Throws(new System.Net.Http.HttpRequestException("connection reset"));
		_applicationClient.ExecutePostRequest<PackagePropertiesResponse>(PropertiesUrl, Arg.Any<string>())
			.Throws(new System.Net.Http.HttpRequestException("connection reset"));

		// Act
		Action act = () => _creator.Create(Request("Calls"));

		// Assert
		act.Should().Throw<PackageCreationOutcomeUnknownException>(
				because: "the server may have stored the package, so reporting that nothing was created would be false")
			.WithMessage("*list-packages*");
	}

}
