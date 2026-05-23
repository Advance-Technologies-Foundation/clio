using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using Clio.UserEnvironment;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public class Link4RepoCommandPreparationTests : BaseCommandTests<Link4RepoOptions> {

	#region Fields: Private

	private ISettingsRepository _settingsRepository = null!;
	private Clio.Common.IFileSystem _fileSystem = null!;
	private IValidator<Link4RepoOptions> _validator = null!;
	private IApplicationPackageListProvider _packageListProvider = null!;
	private IJsonConverter _jsonConverter = null!;
	private ISysSettingsManager _sysSettingsManager = null!;
	private IPackageLockManager _packageLockManager = null!;
	private IFileDesignModePackages _fileDesignModePackages = null!;
	private TestablePrepLink4RepoCommand _command = null!;
	private MockFileSystem _mockFs = null!;

	#endregion

	#region Setup

	[SetUp]
	public override void Setup() {
		base.Setup();
		_mockFs = new MockFileSystem(new Dictionary<string, MockFileData>());
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_fileSystem = new Clio.Common.FileSystem(_mockFs);
		_validator = Substitute.For<IValidator<Link4RepoOptions>>();
		_validator.Validate(Arg.Any<Link4RepoOptions>()).Returns(new ValidationResult());
		_packageListProvider = Substitute.For<IApplicationPackageListProvider>();
		_jsonConverter = Substitute.For<IJsonConverter>();
		_sysSettingsManager = Substitute.For<ISysSettingsManager>();
		_packageLockManager = Substitute.For<IPackageLockManager>();
		_fileDesignModePackages = Substitute.For<IFileDesignModePackages>();
		_command = new TestablePrepLink4RepoCommand(
			ConsoleLogger.Instance,
			Substitute.For<IMediator>(),
			_settingsRepository,
			_fileSystem,
			new RfsEnvironment(
				Substitute.For<Clio.Common.IFileSystem>(),
				Substitute.For<IPackageUtilities>(),
				Substitute.For<ILogger>()),
			_validator,
			_packageListProvider,
			_jsonConverter,
			_sysSettingsManager,
			_packageLockManager,
			_fileDesignModePackages);
		ConsoleLogger.Instance.PreserveMessages = true;
		ConsoleLogger.Instance.ClearMessages();
	}

	[TearDown]
	public override void TearDown() {
		ConsoleLogger.Instance.ClearMessages();
		ConsoleLogger.Instance.PreserveMessages = false;
		base.TearDown();
	}

	#endregion

	#region Tests

	[Test]
	[Description("When packages are incomplete in Pkg, preparation runs: updates Maintainer, unlocks, runs 2fs")]
	public void Execute_Packages_IncompleteInPkg_RunsPreparation() {
		// Arrange
		string envPkg = GetRootedPath("env", "Pkg");
		string repoPath = GetRootedPath("repo");

		// Pkg folder has PkgA dir but NO descriptor.json → incomplete
		_mockFs.AddDirectory(Path.Combine(envPkg, "PkgA", "Files"));
		// Repo has PkgA with descriptor.json
		_mockFs.AddDirectory(Path.Combine(repoPath, "PkgA"));
		_mockFs.AddFile(Path.Combine(repoPath, "PkgA", "descriptor.json"), new MockFileData("{}"));

		_jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(
				Path.Combine(repoPath, "PkgA", "descriptor.json"))
			.Returns(new PackageDescriptorDto {
				Descriptor = new PackageDescriptor { Name = "PkgA", Maintainer = "MyCompany" }
			});

		_sysSettingsManager.GetSysSettingValueByCode("Maintainer").Returns("OldMaintainer");
		_sysSettingsManager.UpdateSysSetting("Maintainer", "MyCompany").Returns(true);

		Link4RepoOptions options = new() {
			EnvPkgPath = envPkg,
			RepoPath = repoPath,
			Packages = "PkgA"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0);
		_sysSettingsManager.Received(1).UpdateSysSetting("Maintainer", "MyCompany");
		_packageLockManager.Received(1).Unlock(Arg.Is<IEnumerable<string>>(
			x => x.Contains("PkgA")));
		_fileDesignModePackages.Received(1).LoadPackagesToFileSystem();
	}

	[Test]
	[Description("When packages are complete in Pkg (have descriptor.json), preparation is skipped")]
	public void Execute_Packages_CompleteInPkg_SkipsPreparation() {
		// Arrange
		string envPkg = GetRootedPath("env", "Pkg");
		string repoPath = GetRootedPath("repo");

		// Pkg folder has PkgA with descriptor.json → complete
		_mockFs.AddDirectory(Path.Combine(envPkg, "PkgA"));
		_mockFs.AddFile(Path.Combine(envPkg, "PkgA", "descriptor.json"), new MockFileData("{}"));
		_mockFs.AddDirectory(Path.Combine(repoPath, "PkgA"));

		Link4RepoOptions options = new() {
			EnvPkgPath = envPkg,
			RepoPath = repoPath,
			Packages = "PkgA"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0);
		_sysSettingsManager.DidNotReceive().GetSysSettingValueByCode(Arg.Any<string>());
		_packageLockManager.DidNotReceive().Unlock(Arg.Any<IEnumerable<string>>());
		_fileDesignModePackages.DidNotReceive().LoadPackagesToFileSystem();
	}

	[Test]
	[Description("When packages have different Maintainers in repo, returns error")]
	public void Execute_Packages_DifferentMaintainers_ReturnsError() {
		// Arrange
		string envPkg = GetRootedPath("env", "Pkg");
		string repoPath = GetRootedPath("repo");

		// Both packages incomplete
		_mockFs.AddDirectory(envPkg);
		_mockFs.AddDirectory(Path.Combine(repoPath, "PkgA"));
		_mockFs.AddFile(Path.Combine(repoPath, "PkgA", "descriptor.json"), new MockFileData("{}"));
		_mockFs.AddDirectory(Path.Combine(repoPath, "PkgB"));
		_mockFs.AddFile(Path.Combine(repoPath, "PkgB", "descriptor.json"), new MockFileData("{}"));

		_jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(
				Path.Combine(repoPath, "PkgA", "descriptor.json"))
			.Returns(new PackageDescriptorDto {
				Descriptor = new PackageDescriptor { Name = "PkgA", Maintainer = "CompanyA" }
			});
		_jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(
				Path.Combine(repoPath, "PkgB", "descriptor.json"))
			.Returns(new PackageDescriptorDto {
				Descriptor = new PackageDescriptor { Name = "PkgB", Maintainer = "CompanyB" }
			});

		Link4RepoOptions options = new() {
			EnvPkgPath = envPkg,
			RepoPath = repoPath,
			Packages = "PkgA,PkgB"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "because packages have different Maintainer values");
		_sysSettingsManager.DidNotReceive().UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>());
		_packageLockManager.DidNotReceive().Unlock(Arg.Any<IEnumerable<string>>());
	}

	[Test]
	[Description("When Maintainer matches SysSetting, it is not updated but unlock + 2fs still run")]
	public void Execute_Packages_MaintainerMatches_SkipsUpdate() {
		// Arrange
		string envPkg = GetRootedPath("env", "Pkg");
		string repoPath = GetRootedPath("repo");

		_mockFs.AddDirectory(envPkg);
		_mockFs.AddDirectory(Path.Combine(repoPath, "PkgA"));
		_mockFs.AddFile(Path.Combine(repoPath, "PkgA", "descriptor.json"), new MockFileData("{}"));

		_jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(
				Path.Combine(repoPath, "PkgA", "descriptor.json"))
			.Returns(new PackageDescriptorDto {
				Descriptor = new PackageDescriptor { Name = "PkgA", Maintainer = "SameMaintainer" }
			});

		_sysSettingsManager.GetSysSettingValueByCode("Maintainer").Returns("SameMaintainer");

		Link4RepoOptions options = new() {
			EnvPkgPath = envPkg,
			RepoPath = repoPath,
			Packages = "PkgA"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0);
		_sysSettingsManager.DidNotReceive().UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>());
		_packageLockManager.Received(1).Unlock(Arg.Any<IEnumerable<string>>());
		_fileDesignModePackages.Received(1).LoadPackagesToFileSystem();
	}

	[Test]
	[Description("When package folder is missing entirely from Pkg, preparation runs")]
	public void Execute_Packages_MissingFromPkg_RunsPreparation() {
		// Arrange
		string envPkg = GetRootedPath("env", "Pkg");
		string repoPath = GetRootedPath("repo");

		// Pkg folder exists but has NO PkgA subfolder
		_mockFs.AddDirectory(envPkg);
		_mockFs.AddDirectory(Path.Combine(repoPath, "PkgA"));
		_mockFs.AddFile(Path.Combine(repoPath, "PkgA", "descriptor.json"), new MockFileData("{}"));

		_jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(
				Path.Combine(repoPath, "PkgA", "descriptor.json"))
			.Returns(new PackageDescriptorDto {
				Descriptor = new PackageDescriptor { Name = "PkgA", Maintainer = "Dev" }
			});

		_sysSettingsManager.GetSysSettingValueByCode("Maintainer").Returns("Dev");

		Link4RepoOptions options = new() {
			EnvPkgPath = envPkg,
			RepoPath = repoPath,
			Packages = "PkgA"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0);
		_packageLockManager.Received(1).Unlock(Arg.Any<IEnumerable<string>>());
		_fileDesignModePackages.Received(1).LoadPackagesToFileSystem();
	}

	[Test]
	[Description("When --dry-run is set with --packages, summary is printed but no mutations happen")]
	public void Execute_Packages_DryRun_NoMutations() {
		// Arrange
		string envPkg = GetRootedPath("env", "Pkg");
		string repoPath = GetRootedPath("repo");

		// Pkg folder has PkgA dir but NO descriptor.json → incomplete
		_mockFs.AddDirectory(Path.Combine(envPkg, "PkgA", "Files"));
		_mockFs.AddDirectory(Path.Combine(repoPath, "PkgA"));
		_mockFs.AddFile(Path.Combine(repoPath, "PkgA", "descriptor.json"), new MockFileData("{}"));

		_jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(
				Path.Combine(repoPath, "PkgA", "descriptor.json"))
			.Returns(new PackageDescriptorDto {
				Descriptor = new PackageDescriptor { Name = "PkgA", Maintainer = "MyCompany" }
			});

		_sysSettingsManager.GetSysSettingValueByCode("Maintainer").Returns("OldMaintainer");

		Link4RepoOptions options = new() {
			EnvPkgPath = envPkg,
			RepoPath = repoPath,
			Packages = "PkgA",
			DryRun = true
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0);
		_sysSettingsManager.DidNotReceive().UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>());
		_packageLockManager.DidNotReceive().Unlock(Arg.Any<IEnumerable<string>>());
		_fileDesignModePackages.DidNotReceive().LoadPackagesToFileSystem();
		_command.CapturedSitePath.Should().BeNull("because dry-run should not create symlinks");
		var logMessages = ConsoleLogger.Instance.FlushAndSnapshotMessages()
			.Select(m => m.Value.ToString()).ToList();
		logMessages.Should().Contain(m => m.Contains("Preparation required:"));
		logMessages.Should().Contain(m => m.Contains("Maintainer"));
		logMessages.Should().Contain(m => m.Contains("[dry-run] No changes applied."));
	}

	[Test]
	[Description("When --skip-preparation is set, preparation is skipped and linking proceeds")]
	public void Execute_Packages_SkipPreparation_SkipsPreparation() {
		// Arrange
		string envPkg = GetRootedPath("env", "Pkg");
		string repoPath = GetRootedPath("repo");

		// Pkg folder has PkgA dir but NO descriptor.json → incomplete
		_mockFs.AddDirectory(Path.Combine(envPkg, "PkgA", "Files"));
		_mockFs.AddDirectory(Path.Combine(repoPath, "PkgA"));

		Link4RepoOptions options = new() {
			EnvPkgPath = envPkg,
			RepoPath = repoPath,
			Packages = "PkgA",
			SkipPreparation = true
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0);
		_sysSettingsManager.DidNotReceive().GetSysSettingValueByCode(Arg.Any<string>());
		_packageLockManager.DidNotReceive().Unlock(Arg.Any<IEnumerable<string>>());
		_fileDesignModePackages.DidNotReceive().LoadPackagesToFileSystem();
		_command.CapturedSitePath.Should().NotBeNull("because linking should still proceed");
	}

	[Test]
	[Description("Validation fails when both --unlocked and --packages are provided")]
	public void Validate_Unlocked_And_Packages_ReturnsError() {
		// Arrange
		var validator = new Link4RepoOptionsValidator();
		var options = new Link4RepoOptions {
			EnvPkgPath = GetRootedPath("env", "Pkg"),
			RepoPath = GetRootedPath("repo"),
			Unlocked = true,
			Packages = "PkgA",
			Environment = "dev"
		};

		// Act
		var result = validator.Validate(options);

		// Assert
		result.IsValid.Should().BeFalse();
		result.Errors.Should().Contain(e =>
			e.ErrorMessage.Contains("mutually exclusive"));
	}

	#endregion

	#region Helpers

	private static string GetRootedPath(params string[] parts) {
		string root = OperatingSystem.IsWindows()
			? @"C:\"
			: "/";
		return Path.Combine(new[] { root }.Concat(parts).ToArray());
	}

	#endregion

	#region TestableCommand

	private sealed class TestablePrepLink4RepoCommand : Link4RepoCommand {

		public string? CapturedSitePath { get; private set; }
		public string? CapturedPackages { get; private set; }

		public TestablePrepLink4RepoCommand(
			ILogger logger,
			IMediator mediator,
			ISettingsRepository settingsRepository,
			Clio.Common.IFileSystem fileSystem,
			RfsEnvironment rfsEnvironment,
			IValidator<Link4RepoOptions> validator,
			IApplicationPackageListProvider applicationPackageListProvider,
			IJsonConverter jsonConverter,
			ISysSettingsManager sysSettingsManager,
			IPackageLockManager packageLockManager,
			IFileDesignModePackages fileDesignModePackages)
			: base(logger, mediator, settingsRepository, fileSystem, rfsEnvironment, validator,
				applicationPackageListProvider, jsonConverter, sysSettingsManager, packageLockManager,
				fileDesignModePackages) {
		}

		protected override int HandleLinkWithDirPath(string sitePath, string repoPath, string packages) {
			CapturedSitePath = sitePath;
			CapturedPackages = packages;
			return 0;
		}
	}

	#endregion

}
