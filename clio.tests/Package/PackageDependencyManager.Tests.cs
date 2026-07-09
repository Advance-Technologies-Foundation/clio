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
	private string _loadRequestBody;

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
		_loadRequestBody = null;
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
			.ExecutePostRequest<PackagePropertiesResponse>(
				Arg.Any<string>(), Arg.Do<string>(body => _loadRequestBody = body))
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

	[Test]
	[Description("Sends the target package UId to GetPackageProperties as a bare quoted JSON GUID (wire-contract guard).")]
	public void AddDependencies_ShouldSendBareQuotedGuid_WhenLoadingPackageProperties() {
		// Arrange
		ArrangeInstalledPackages();
		ArrangeGetPackageProperties(new WorkspacePackageDto { UId = _targetUId, Name = TargetPackageName });
		ArrangeSavePackageProperties(new SavePackagePropertiesResponse { Success = true });

		// Act
		_manager.AddDependencies(TargetPackageName, [new PackageDependencySpec(DependencyPackageName)]);

		// Assert
		_loadRequestBody.Should().Be(JsonConvert.SerializeObject(_targetUId),
			because: "GetPackageProperties expects the package UId serialized as a bare quoted JSON string, "
				+ "so the wire contract reverse-engineered from the NUI client must stay pinned");
	}

	[Test]
	[Description("Surfaces the server error message when GetPackageProperties reports a failure with error detail.")]
	public void AddDependencies_ShouldThrowServerError_WhenLoadFailsWithErrorInfo() {
		// Arrange
		ArrangeInstalledPackages();
		_applicationClient
			.ExecutePostRequest<PackagePropertiesResponse>(Arg.Any<string>(), Arg.Any<string>())
			.Returns(new PackagePropertiesResponse {
				Success = false,
				ErrorInfo = new Clio.Common.Responses.ErrorInfo { Message = "Access denied" }
			});

		// Act
		Action act = () =>
			_manager.AddDependencies(TargetPackageName, [new PackageDependencySpec(DependencyPackageName)]);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*Access denied*",
				because: "a failed load carrying server error detail must surface that detail to the user");
	}

	[Test]
	[Description("Throws a descriptive error (not an NRE) when GetPackageProperties fails with a null ErrorInfo.")]
	public void AddDependencies_ShouldThrowDescriptiveError_WhenLoadFailsWithNullErrorInfo() {
		// Arrange
		ArrangeInstalledPackages();
		_applicationClient
			.ExecutePostRequest<PackagePropertiesResponse>(Arg.Any<string>(), Arg.Any<string>())
			.Returns(new PackagePropertiesResponse { Success = false, ErrorInfo = null });

		// Act
		Action act = () =>
			_manager.AddDependencies(TargetPackageName, [new PackageDependencySpec(DependencyPackageName)]);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage($"*{TargetPackageName}*",
				because: "a failed load with no server error detail must produce an actionable message "
					+ "instead of a bare NullReferenceException from dereferencing ErrorInfo.Message");
	}

	[Test]
	[Description("Removes a present dependency by name and persists the package without it (ENG-91314).")]
	public void RemoveDependencies_ShouldDropDependency_WhenDependencyIsPresent() {
		// Arrange
		ArrangeInstalledPackages();
		ArrangeGetPackageProperties(new WorkspacePackageDto {
			UId = _targetUId,
			Name = TargetPackageName,
			DependsOnPackages = [new WorkspacePackageDto { UId = _dependencyUId, Name = DependencyPackageName }]
		});
		ArrangeSavePackageProperties(new SavePackagePropertiesResponse { Success = true });

		// Act
		IReadOnlyList<string> result = _manager.RemoveDependencies(TargetPackageName, [DependencyPackageName]);

		// Assert
		result.Should().NotContain(DependencyPackageName,
			because: "the resulting dependency list must no longer include the removed package");
		WorkspacePackageDto savedPackage = DeserializeSavedPackage();
		savedPackage.DependsOnPackages.Should().NotContain(dependency => dependency.UId == _dependencyUId,
			because: "the saved package must drop the removed dependency from dependsOnPackages");
	}

	[Test]
	[Description("Matches the dependency name case-insensitively so casing differences still remove it (ENG-91314).")]
	public void RemoveDependencies_ShouldMatchNameCaseInsensitively_WhenCasingDiffers() {
		// Arrange
		ArrangeInstalledPackages();
		ArrangeGetPackageProperties(new WorkspacePackageDto {
			UId = _targetUId,
			Name = TargetPackageName,
			DependsOnPackages = [new WorkspacePackageDto { UId = _dependencyUId, Name = DependencyPackageName }]
		});
		ArrangeSavePackageProperties(new SavePackagePropertiesResponse { Success = true });

		// Act
		_manager.RemoveDependencies(TargetPackageName, [DependencyPackageName.ToUpperInvariant()]);

		// Assert
		WorkspacePackageDto savedPackage = DeserializeSavedPackage();
		savedPackage.DependsOnPackages.Should().BeEmpty(
			because: "dependency names must be matched case-insensitively when removing");
	}

	[Test]
	[Description("Does not call SavePackageProperties when no dependency matched, and leaves an existing non-targeted dependency intact, so a no-op removal stays cheap and side-effect free (ENG-91314).")]
	public void RemoveDependencies_ShouldNotSaveAndPreserveExisting_WhenNoDependencyMatched() {
		// Arrange — package HAS a dependency, but it is not the one being removed, so the no-op path must
		// neither persist nor drop the surviving dependency.
		ArrangeInstalledPackages();
		ArrangeGetPackageProperties(new WorkspacePackageDto {
			UId = _targetUId,
			Name = TargetPackageName,
			DependsOnPackages = [new WorkspacePackageDto { UId = _dependencyUId, Name = DependencyPackageName }]
		});
		ArrangeSavePackageProperties(new SavePackagePropertiesResponse { Success = true });

		// Act
		IReadOnlyList<string> result = _manager.RemoveDependencies(TargetPackageName, ["NotADependency"]);

		// Assert
		result.Should().ContainSingle().Which.Should().Be(DependencyPackageName,
			because: "a dependency that was not targeted must survive a no-op removal");
		_applicationClient.DidNotReceive().ExecutePostRequest<SavePackagePropertiesResponse>(
			Arg.Any<string>(), Arg.Any<string>());
		_savedRequestBody.Should().BeNull(
			because: "removing an absent dependency is a no-op and must not persist the package");
	}

	[Test]
	[Description("Removes only the targeted dependency and preserves the others, proving the removal is selective rather than a blanket clear (ENG-91314).")]
	public void RemoveDependencies_ShouldRemoveOnlyTargetedDependency_WhenPackageHasSeveral() {
		// Arrange — two dependencies present; remove exactly one and assert the other survives in both the
		// returned list and the persisted DTO. This pins selectivity: a "clear all when anything matches"
		// regression would fail here while passing the single-dependency tests.
		Guid survivorUId = Guid.NewGuid();
		const string survivorName = "CrtCase";
		ArrangeInstalledPackages();
		ArrangeGetPackageProperties(new WorkspacePackageDto {
			UId = _targetUId,
			Name = TargetPackageName,
			DependsOnPackages = [
				new WorkspacePackageDto { UId = _dependencyUId, Name = DependencyPackageName },
				new WorkspacePackageDto { UId = survivorUId, Name = survivorName }
			]
		});
		ArrangeSavePackageProperties(new SavePackagePropertiesResponse { Success = true });

		// Act
		IReadOnlyList<string> result = _manager.RemoveDependencies(TargetPackageName, [DependencyPackageName]);

		// Assert
		result.Should().ContainSingle().Which.Should().Be(survivorName,
			because: "only the targeted dependency must be removed; the untargeted one must remain");
		WorkspacePackageDto savedPackage = DeserializeSavedPackage();
		savedPackage.DependsOnPackages.Should().ContainSingle(dependency => dependency.UId == survivorUId,
			because: "the persisted package must keep the untargeted dependency");
		savedPackage.DependsOnPackages.Should().NotContain(dependency => dependency.UId == _dependencyUId,
			because: "the persisted package must drop only the targeted dependency");
	}

	[Test]
	[Description("Throws a descriptive error when the target package is not installed in the environment (ENG-91314).")]
	public void RemoveDependencies_ShouldThrow_WhenTargetPackageNotFound() {
		// Arrange
		_packageListProvider.GetPackages("{}").Returns([
			CreatePackageInfo(DependencyPackageName, _dependencyUId, "8.2.1.999")
		]);

		// Act
		Action act = () => _manager.RemoveDependencies(TargetPackageName, [DependencyPackageName]);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage($"*{TargetPackageName}*not found*",
				because: "a missing target package must produce an actionable error");
	}

	[Test]
	[Description("Throws when no dependency name is supplied so an empty removal request fails fast (ENG-91314).")]
	public void RemoveDependencies_ShouldThrow_WhenNoDependencyNameSupplied() {
		// Arrange
		ArrangeInstalledPackages();

		// Act
		Action act = () => _manager.RemoveDependencies(TargetPackageName, [" "]);

		// Assert
		act.Should().Throw<ArgumentException>(
			because: "at least one non-empty dependency name must be specified to remove");
	}

}
