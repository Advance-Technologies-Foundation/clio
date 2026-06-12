using System;
using System.Collections.Generic;
using System.Linq;
using Clio;
using Clio.Common;
using Clio.Package;
using Clio.Package.Responses;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package;

[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public class PackageDependencyManagerTests
{

	#region Constants: Private

	private const string TargetPackageName = "MyApp";
	private const string DependencyPackageName = "CrtLeadOppMgmtApp";

	#endregion

	#region Fields: Private

	private IApplicationPackageListProvider _packageListProvider;
	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ILogger _logger;
	private PackageDependencyManager _manager;
	private Guid _targetUId;
	private Guid _dependencyUId;
	private string _savedRequestBody;

	#endregion

	#region Setup/Teardown

	[SetUp]
	public void Init() {
		_packageListProvider = Substitute.For<IApplicationPackageListProvider>();
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build(Arg.Any<string>()).Returns(callInfo => $"https://stub{callInfo.Arg<string>()}");
		_manager = new PackageDependencyManager(_packageListProvider, _applicationClient, _serviceUrlBuilder, _logger);
		_targetUId = Guid.NewGuid();
		_dependencyUId = Guid.NewGuid();
		_savedRequestBody = null;
	}

	#endregion

	#region Methods: Private

	private static PackageInfo CreatePackageInfo(string name, Guid uId, string version) =>
		new(new PackageDescriptor { Name = name, UId = uId, PackageVersion = version }, string.Empty, []);

	private void ArrangeInstalledPackages() {
		_packageListProvider.GetPackages("{}").Returns([
			CreatePackageInfo(TargetPackageName, _targetUId, "1.0.0.0"),
			CreatePackageInfo(DependencyPackageName, _dependencyUId, "8.2.1.999")
		]);
	}

	private void ArrangeGetPackageProperties(WorkspacePackageDto package) {
		_applicationClient
			.ExecutePostRequest<PackagePropertiesResponse>(Arg.Any<string>(), Arg.Any<string>())
			.Returns(new PackagePropertiesResponse { Success = true, Package = package });
	}

	private void ArrangeSavePackageProperties(SavePackagePropertiesResponse response) {
		_applicationClient
			.ExecutePostRequest<SavePackagePropertiesResponse>(
				Arg.Any<string>(), Arg.Do<string>(body => _savedRequestBody = body))
			.Returns(response);
	}

	private WorkspacePackageDto DeserializeSavedPackage() =>
		JsonConvert.DeserializeObject<WorkspacePackageDto>(_savedRequestBody);

	#endregion

	[Test]
	[Description("Adds a new dependency and persists the package with the dependency UId in dependsOnPackages.")]
	public void AddDependencies_ShouldAppendDependency_WhenDependencyIsNotYetPresent() {
		// Arrange
		ArrangeInstalledPackages();
		ArrangeGetPackageProperties(new WorkspacePackageDto { UId = _targetUId, Name = TargetPackageName });
		ArrangeSavePackageProperties(new SavePackagePropertiesResponse { Success = true });

		// Act
		IReadOnlyList<string> result =
			_manager.AddDependencies(TargetPackageName, [new PackageDependencySpec(DependencyPackageName)]);

		// Assert
		result.Should().Contain(DependencyPackageName,
			because: "the resulting dependency list must include the newly added package");
		WorkspacePackageDto savedPackage = DeserializeSavedPackage();
		savedPackage.DependsOnPackages.Should().ContainSingle(dependency => dependency.UId == _dependencyUId,
			because: "the saved package must carry the dependency identified by its UId");
	}

	[Test]
	[Description("Defaults the dependency version to the installed version of the dependency package when omitted.")]
	public void AddDependencies_ShouldUseInstalledVersion_WhenVersionIsOmitted() {
		// Arrange
		ArrangeInstalledPackages();
		ArrangeGetPackageProperties(new WorkspacePackageDto { UId = _targetUId, Name = TargetPackageName });
		ArrangeSavePackageProperties(new SavePackagePropertiesResponse { Success = true });

		// Act
		_manager.AddDependencies(TargetPackageName, [new PackageDependencySpec(DependencyPackageName)]);

		// Assert
		WorkspacePackageDto savedPackage = DeserializeSavedPackage();
		savedPackage.DependsOnPackages.Single().Version.Should().Be("8.2.1.999",
			because: "the omitted version must fall back to the installed dependency package version");
	}

	[Test]
	[Description("Does not duplicate a dependency that already exists on the package (idempotent).")]
	public void AddDependencies_ShouldBeIdempotent_WhenDependencyAlreadyPresent() {
		// Arrange
		ArrangeInstalledPackages();
		ArrangeGetPackageProperties(new WorkspacePackageDto {
			UId = _targetUId,
			Name = TargetPackageName,
			DependsOnPackages = [new WorkspacePackageDto { UId = _dependencyUId, Name = DependencyPackageName }]
		});
		ArrangeSavePackageProperties(new SavePackagePropertiesResponse { Success = true });

		// Act
		_manager.AddDependencies(TargetPackageName, [new PackageDependencySpec(DependencyPackageName)]);

		// Assert
		WorkspacePackageDto savedPackage = DeserializeSavedPackage();
		savedPackage.DependsOnPackages.Count(dependency => dependency.UId == _dependencyUId).Should().Be(1,
			because: "re-adding an existing dependency must not create a duplicate entry");
	}

	[Test]
	[Description("Throws a descriptive error when the target package is not installed in the environment.")]
	public void AddDependencies_ShouldThrow_WhenTargetPackageNotFound() {
		// Arrange
		_packageListProvider.GetPackages("{}").Returns([
			CreatePackageInfo(DependencyPackageName, _dependencyUId, "8.2.1.999")
		]);

		// Act
		Action act = () => _manager.AddDependencies(TargetPackageName, [new PackageDependencySpec(DependencyPackageName)]);

		// Assert
		act.Should().Throw<Exception>()
			.WithMessage($"*{TargetPackageName}*not found*",
				because: "a missing target package must produce an actionable error");
	}

	[Test]
	[Description("Throws a descriptive error when a requested dependency package is not installed.")]
	public void AddDependencies_ShouldThrow_WhenDependencyPackageNotFound() {
		// Arrange
		_packageListProvider.GetPackages("{}").Returns([
			CreatePackageInfo(TargetPackageName, _targetUId, "1.0.0.0")
		]);
		ArrangeGetPackageProperties(new WorkspacePackageDto { UId = _targetUId, Name = TargetPackageName });

		// Act
		Action act = () => _manager.AddDependencies(TargetPackageName, [new PackageDependencySpec(DependencyPackageName)]);

		// Assert
		act.Should().Throw<Exception>()
			.WithMessage($"*{DependencyPackageName}*not found*",
				because: "a missing dependency package must produce an actionable error");
	}

	[Test]
	[Description("Surfaces server validation errors when SavePackageProperties reports a failure.")]
	public void AddDependencies_ShouldThrowWithValidationErrors_WhenSaveFails() {
		// Arrange
		ArrangeInstalledPackages();
		ArrangeGetPackageProperties(new WorkspacePackageDto { UId = _targetUId, Name = TargetPackageName });
		ArrangeSavePackageProperties(new SavePackagePropertiesResponse {
			Success = false,
			ErrorInfo = new Clio.Common.Responses.ErrorInfo { Message = "Save failed" },
			ValidationErrors = [
				new PackageValidationErrorDto { PackageName = TargetPackageName, ItemName = "X", Message = "bad" }
			]
		});

		// Act
		Action act = () => _manager.AddDependencies(TargetPackageName, [new PackageDependencySpec(DependencyPackageName)]);

		// Assert
		act.Should().Throw<Exception>()
			.WithMessage("*Save failed*Validation errors*bad*",
				because: "save failures must surface both the error message and the validation details");
	}

	[Test]
	[Description("Preserves server-owned AdditionalData fields when round-tripping through SavePackageProperties.")]
	public void AddDependencies_ShouldRoundTripAdditionalData_WhenPackageHasExtraFields() {
		// Arrange
		WorkspacePackageDto package = new() { UId = _targetUId, Name = TargetPackageName };
		package.AdditionalData = new Dictionary<string, JToken> {
			["description"] = "keep me",
			["installBehavior"] = 1
		};
		ArrangeInstalledPackages();
		ArrangeGetPackageProperties(package);
		ArrangeSavePackageProperties(new SavePackagePropertiesResponse { Success = true });

		// Act
		_manager.AddDependencies(TargetPackageName, [new PackageDependencySpec(DependencyPackageName)]);

		// Assert
		WorkspacePackageDto saved = DeserializeSavedPackage();
		saved.AdditionalData["description"].Value<string>().Should().Be("keep me",
			because: "server-owned fields must round-trip so the save does not wipe package metadata");
		saved.AdditionalData["installBehavior"].Value<int>().Should().Be(1,
			because: "server-owned fields must round-trip so the save does not wipe package metadata");
	}

}
