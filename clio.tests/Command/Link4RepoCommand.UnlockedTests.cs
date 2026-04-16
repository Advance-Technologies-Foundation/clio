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
public class Link4RepoCommandUnlockedTests : BaseCommandTests<Link4RepoOptions> {

	#region Fields: Private

	private ISettingsRepository _settingsRepository = null!;
	private Clio.Common.IFileSystem _fileSystem = null!;
	private IValidator<Link4RepoOptions> _validator = null!;
	private IApplicationPackageListProvider _packageListProvider = null!;
	private IJsonConverter _jsonConverter = null!;
	private TestableUnlockedLink4RepoCommand _command = null!;
	private MockFileSystem _mockFs = null!;

	#endregion

	#region Methods: Public

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
		_command = new TestableUnlockedLink4RepoCommand(
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
			Substitute.For<ISysSettingsManager>(),
			Substitute.For<IPackageLockManager>(),
			Substitute.For<IFileDesignModePackages>());
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
	[Description("Unlocked with flat repo links unlocked packages via HandleLinkWithDirPath")]
	public void Execute_Unlocked_FlatRepo_LinksUnlockedPackages() {
		// Arrange
		string envPkg = GetRootedPath("env", "Pkg");
		string repoPath = GetRootedPath("repo");

		_mockFs.AddDirectory(envPkg);
		_mockFs.AddDirectory(Path.Combine(repoPath, "PkgA"));
		_mockFs.AddFile(Path.Combine(repoPath, "PkgA", "descriptor.json"), new MockFileData("{}"));
		_mockFs.AddDirectory(Path.Combine(repoPath, "PkgB"));
		_mockFs.AddFile(Path.Combine(repoPath, "PkgB", "descriptor.json"), new MockFileData("{}"));

		_packageListProvider.GetPackages(Arg.Any<string>())
			.Returns(new List<PackageInfo> {
				CreatePackageInfo("PkgA", "1.0.0"),
				CreatePackageInfo("PkgB", "2.0.0")
			});

		Link4RepoOptions options = new() {
			EnvPkgPath = envPkg,
			RepoPath = repoPath,
			Unlocked = true,
			Environment = "dev"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because unlocked packages should be linked successfully");
		_command.CapturedSitePath.Should().Be(envPkg);
		_command.CapturedPackages.Should().Contain("PkgA").And.Contain("PkgB");
	}

	[Test]
	[Description("Unlocked with versioned repo links packages by matching version across branches")]
	public void Execute_Unlocked_VersionedRepo_LinksWithVersionMatch() {
		// Arrange
		string envPkg = GetRootedPath("env", "Pkg");
		string repoPath = GetRootedPath("store");

		_mockFs.AddDirectory(envPkg);
		// Versioned structure: store/PkgA/main/1.0.0.0/ (no descriptor.json at store/PkgA/)
		_mockFs.AddDirectory(Path.Combine(repoPath, "PkgA", "main", "1.0.0.0"));

		_packageListProvider.GetPackages(Arg.Any<string>())
			.Returns(new List<PackageInfo> {
				CreatePackageInfo("PkgA", "1.0.0.0")
			});

		Link4RepoOptions options = new() {
			EnvPkgPath = envPkg,
			RepoPath = repoPath,
			Unlocked = true,
			Environment = "dev"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because versioned package should be linked successfully");
		_command.VersionedLinkCalls.Should().HaveCount(1);
		_command.VersionedLinkCalls[0].packageName.Should().Be("PkgA");
		_command.VersionedLinkCalls[0].versionPath.Should().Contain("1.0.0.0");
	}

	[Test]
	[Description("Unlocked skips packages not found in repository and still returns success")]
	public void Execute_Unlocked_SkipsPackagesNotInRepo() {
		// Arrange
		string envPkg = GetRootedPath("env", "Pkg");
		string repoPath = GetRootedPath("repo");

		_mockFs.AddDirectory(envPkg);
		_mockFs.AddDirectory(Path.Combine(repoPath, "PkgA"));
		_mockFs.AddFile(Path.Combine(repoPath, "PkgA", "descriptor.json"), new MockFileData("{}"));

		_packageListProvider.GetPackages(Arg.Any<string>())
			.Returns(new List<PackageInfo> {
				CreatePackageInfo("PkgA", "1.0.0"),
				CreatePackageInfo("PkgMissing", "1.0.0")
			});

		Link4RepoOptions options = new() {
			EnvPkgPath = envPkg,
			RepoPath = repoPath,
			Unlocked = true,
			Environment = "dev"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because partial linking should still succeed");
		_command.CapturedPackages.Should().Contain("PkgA");
		_command.CapturedPackages.Should().NotContain("PkgMissing");
	}

	[Test]
	[Description("Unlocked with no unlocked packages returns success without linking")]
	public void Execute_Unlocked_NoUnlockedPackages_ReturnsZero() {
		// Arrange
		string envPkg = GetRootedPath("env", "Pkg");
		string repoPath = GetRootedPath("repo");

		_mockFs.AddDirectory(envPkg);
		_mockFs.AddDirectory(repoPath);

		_packageListProvider.GetPackages(Arg.Any<string>())
			.Returns(new List<PackageInfo>());

		Link4RepoOptions options = new() {
			EnvPkgPath = envPkg,
			RepoPath = repoPath,
			Unlocked = true,
			Environment = "dev"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because no unlocked packages means nothing to link");
		_command.CapturedSitePath.Should().BeNull("because no linking should occur");
	}

	[Test]
	[Description("Unlocked fails validation when no environment credentials are provided")]
	public void Execute_Unlocked_ValidationFails_WithoutEnvironment() {
		// Arrange
		_validator.Validate(Arg.Any<Link4RepoOptions>()).Returns(new ValidationResult(
			new[] {
				new ValidationFailure("Environment",
					"When --unlocked is set, environment name (-e) or URI (-u) must be provided")
			}));

		Link4RepoOptions options = new() {
			EnvPkgPath = GetRootedPath("env", "Pkg"),
			RepoPath = GetRootedPath("repo"),
			Unlocked = true
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "because validation should fail without environment credentials");
		_packageListProvider.DidNotReceive().GetPackages(Arg.Any<string>());
	}

	[Test]
	[Description("Without --unlocked the existing behavior is unchanged and provider is not called")]
	public void Execute_WithoutUnlocked_ExistingBehavior_Unchanged() {
		// Arrange
		string envPkg = GetRootedPath("env", "Pkg");
		string repoPath = GetRootedPath("repo");
		_mockFs.AddDirectory(envPkg);

		Link4RepoOptions options = new() {
			EnvPkgPath = envPkg,
			RepoPath = repoPath,
			Packages = "PkgA,PkgB"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because existing linking flow should work as before");
		_packageListProvider.DidNotReceive().GetPackages(Arg.Any<string>());
		_command.CapturedSitePath.Should().Be(envPkg);
		_command.CapturedPackages.Should().Be("PkgA,PkgB");
	}

	[Test]
	[Description("Detects flat repo structure when descriptor.json exists in first package directory")]
	public void Execute_Unlocked_DetectsFlatRepoStructure() {
		// Arrange
		string envPkg = GetRootedPath("env", "Pkg");
		string repoPath = GetRootedPath("repo");

		_mockFs.AddDirectory(envPkg);
		_mockFs.AddDirectory(Path.Combine(repoPath, "PkgA"));
		_mockFs.AddFile(Path.Combine(repoPath, "PkgA", "descriptor.json"), new MockFileData("{}"));

		_packageListProvider.GetPackages(Arg.Any<string>())
			.Returns(new List<PackageInfo> {
				CreatePackageInfo("PkgA", "1.0.0")
			});

		Link4RepoOptions options = new() {
			EnvPkgPath = envPkg,
			RepoPath = repoPath,
			Unlocked = true,
			Environment = "dev"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0);
		_command.CapturedSitePath.Should().NotBeNull("because flat repo should use HandleLinkWithDirPath");
		_command.VersionedLinkCalls.Should().BeEmpty("because flat repo should not use versioned linking");
	}

	[Test]
	[Description("Detects versioned repo structure when first package has branch subdirectories")]
	public void Execute_Unlocked_DetectsVersionedRepoStructure() {
		// Arrange
		string envPkg = GetRootedPath("env", "Pkg");
		string repoPath = GetRootedPath("store");

		_mockFs.AddDirectory(envPkg);
		_mockFs.AddDirectory(Path.Combine(repoPath, "PkgA", "main", "1.0.0"));

		_packageListProvider.GetPackages(Arg.Any<string>())
			.Returns(new List<PackageInfo> {
				CreatePackageInfo("PkgA", "1.0.0")
			});

		Link4RepoOptions options = new() {
			EnvPkgPath = envPkg,
			RepoPath = repoPath,
			Unlocked = true,
			Environment = "dev"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0);
		_command.CapturedSitePath.Should().BeNull("because versioned repo should not use flat HandleLinkWithDirPath");
		_command.VersionedLinkCalls.Should().NotBeEmpty("because versioned repo should use versioned linking");
	}

	[Test]
	[Description("When --dry-run is set with --unlocked, package list is queried but no linking happens")]
	public void Execute_Unlocked_DryRun_NoLinking() {
		// Arrange
		string envPkg = GetRootedPath("env", "Pkg");
		string repoPath = GetRootedPath("repo");

		_mockFs.AddDirectory(envPkg);
		_mockFs.AddDirectory(Path.Combine(repoPath, "PkgA"));
		_mockFs.AddFile(Path.Combine(repoPath, "PkgA", "descriptor.json"), new MockFileData("{}"));

		_packageListProvider.GetPackages(Arg.Any<string>())
			.Returns(new List<PackageInfo> {
				CreatePackageInfo("PkgA", "1.0.0"),
				CreatePackageInfo("PkgB", "2.0.0")
			});

		Link4RepoOptions options = new() {
			EnvPkgPath = envPkg,
			RepoPath = repoPath,
			Unlocked = true,
			Environment = "dev",
			DryRun = true
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because dry-run should succeed without mutations");
		_packageListProvider.Received(1).GetPackages(Arg.Any<string>());
		_command.CapturedSitePath.Should().BeNull("because dry-run should not create symlinks");
		_command.VersionedLinkCalls.Should().BeEmpty("because dry-run should not link");
		var logMessages = ConsoleLogger.Instance.FlushAndSnapshotMessages()
			.Select(m => m.Value.ToString()).ToList();
		logMessages.Should().Contain(m => m.Contains("[dry-run]") && m.Contains("unlocked package(s)"));
		logMessages.Should().Contain(m => m.Contains("Repository structure:"));
		logMessages.Should().Contain(m => m.Contains("[dry-run] No changes applied."));
	}

	#endregion

	#region Helpers

	private static string GetRootedPath(params string[] parts) {
		string root = OperatingSystem.IsWindows()
			? @"C:\"
			: "/";
		return Path.Combine(new[] { root }.Concat(parts).ToArray());
	}

	private static PackageInfo CreatePackageInfo(string name, string version) {
		PackageDescriptor descriptor = new() {
			Name = name,
			PackageVersion = version,
			Maintainer = "Test",
			UId = Guid.NewGuid()
		};
		return new PackageInfo(descriptor, string.Empty, []);
	}

	#endregion

	#region TestableCommand

	private sealed class TestableUnlockedLink4RepoCommand : Link4RepoCommand {

		public string? CapturedSitePath { get; private set; }
		public string? CapturedPackages { get; private set; }
		public List<(string packageName, string versionPath)> VersionedLinkCalls { get; } = [];

		public TestableUnlockedLink4RepoCommand(
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

		protected override int HandleUnlockedVersionedRepo(string envPkgPath, string repoPath,
			List<PackageInfo> unlockedPackages) {
			foreach (PackageInfo pkg in unlockedPackages) {
				string packageName = pkg.Descriptor.Name;
				string packageVersion = pkg.Descriptor.PackageVersion;
				// Simulate finding the version path
				string versionPath = Path.Combine(repoPath, packageName, "main", packageVersion);
				VersionedLinkCalls.Add((packageName, versionPath));
			}
			return 0;
		}
	}

	#endregion

}
