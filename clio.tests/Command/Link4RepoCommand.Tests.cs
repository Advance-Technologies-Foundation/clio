using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
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
public class Link4RepoCommandTests : BaseCommandTests<Link4RepoOptions> {

	#region Fields: Private

	private ISettingsRepository _settingsRepository = null!;
	private Clio.Common.IFileSystem _fileSystem = null!;
	private IValidator<Link4RepoOptions> _validator = null!;
	private TestableLink4RepoCommand _command = null!;

	#endregion

	#region Methods: Public

	[SetUp]
	public override void Setup() {
		base.Setup();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_fileSystem = new Clio.Common.FileSystem(new MockFileSystem(new Dictionary<string, MockFileData>()));
		_validator = Substitute.For<IValidator<Link4RepoOptions>>();
		_validator.Validate(Arg.Any<Link4RepoOptions>()).Returns(new ValidationResult());
		_command = new TestableLink4RepoCommand(
			ConsoleLogger.Instance,
			Substitute.For<IMediator>(),
			_settingsRepository,
			_fileSystem,
			new RfsEnvironment(
				Substitute.For<Clio.Common.IFileSystem>(),
				Substitute.For<IPackageUtilities>(),
				Substitute.For<ILogger>()),
			_validator,
			Substitute.For<IApplicationPackageListProvider>(),
			Substitute.For<IJsonConverter>(),
			Substitute.For<ISysSettingsManager>(),
			Substitute.For<IPackageLockManager>(),
			Substitute.For<IFileDesignModePackages>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[TearDown]
	public override void TearDown() {
		ConsoleLogger.Instance.ClearMessages();
		base.TearDown();
	}

	#endregion

	#region Tests

	[Test]
	[Description("Uses the registered EnvironmentPath NET8 package folder on every platform before falling back to Windows IIS discovery.")]
	public void Execute_Should_Use_Registered_NetCore_EnvironmentPath_When_Package_Folder_Exists() {
		// Arrange
		string envRoot = GetRootedPath("creatio");
		string pkgPath = Path.Combine(envRoot, "Terrasoft.Configuration", "Pkg");
		_fileSystem.CreateDirectory(pkgPath);
		_settingsRepository.IsEnvironmentExists("dev").Returns(true);
		_settingsRepository.GetEnvironment("dev").Returns(new EnvironmentSettings {
			EnvironmentPath = envRoot,
			IsNetCore = true
		});

		Link4RepoOptions options = new() {
			Environment = "dev",
			RepoPath = GetRootedPath("repo"),
			Packages = "*"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because a registered NET8 environment with EnvironmentPath should resolve locally on any OS");
		_command.CapturedSitePath.Should().Be(pkgPath,
			"because link-from-repository should target Terrasoft.Configuration/Pkg for NET8 environments");
	}

	[Test]
	[Description("Uses the registered EnvironmentPath classic package folder on every platform before falling back to Windows IIS discovery.")]
	public void Execute_Should_Use_Registered_NetFramework_EnvironmentPath_When_Package_Folder_Exists() {
		// Arrange
		string envRoot = GetRootedPath("creatio");
		string pkgPath = Path.Combine(envRoot, "Terrasoft.WebApp", "Terrasoft.Configuration", "Pkg");
		_fileSystem.CreateDirectory(pkgPath);
		_settingsRepository.IsEnvironmentExists("dev").Returns(true);
		_settingsRepository.GetEnvironment("dev").Returns(new EnvironmentSettings {
			EnvironmentPath = envRoot,
			IsNetCore = false
		});

		Link4RepoOptions options = new() {
			Environment = "dev",
			RepoPath = GetRootedPath("repo"),
			Packages = "*"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, "because classic local environments should resolve their WebApp package path from EnvironmentPath");
		_command.CapturedSitePath.Should().Be(pkgPath,
			"because link-from-repository should target Terrasoft.WebApp/Terrasoft.Configuration/Pkg for classic environments");
	}

	[Test]
	[Description("Reports a local-path error on non-Windows platforms when the registered environment has no resolvable package folder.")]
	public void Execute_Should_Report_Missing_Local_Path_On_NonWindows_When_EnvironmentPath_Cannot_Be_Resolved() {
		if (OperatingSystem.IsWindows()) {
			return;
		}

		// Arrange
		_settingsRepository.IsEnvironmentExists("dev").Returns(true);
		_settingsRepository.GetEnvironment("dev").Returns(new EnvironmentSettings {
			EnvironmentPath = GetRootedPath("creatio"),
			IsNetCore = true
		});

		Link4RepoOptions options = new() {
			Environment = "dev",
			RepoPath = GetRootedPath("repo"),
			Packages = "*"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, "because non-Windows environment-name resolution depends on a valid local EnvironmentPath package folder");
		_command.CapturedSitePath.Should().BeNull(
			"because the command should not proceed to link packages when no local package folder can be resolved");
	}

	
	[Test]
	[Description("Treats a relative env package path as a direct filesystem path so link-from-repository works on macOS and Linux without forcing an absolute path.")]
	public void TryResolveDirectoryPath_Should_Return_FullPath_For_Relative_Directory_Path() {
		// Arrange
		string currentDirectory = GetRootedPath("repo");
		string relativePath = Path.Combine("..", "..", "Projects", "Creatio", "creatio_app", "gartner",
			"Terrasoft.Configuration", "Pkg");
		string expectedPath = Path.Combine(GetRootedPath("Projects"), "Creatio", "creatio_app", "gartner",
			"Terrasoft.Configuration", "Pkg");
		MockFileSystem msFileSystem = new(new Dictionary<string, MockFileData>(), currentDirectory);
		msFileSystem.AddDirectory(expectedPath);
		Clio.Common.IFileSystem fileSystem = new FileSystem(msFileSystem);

		// Act
		bool result = Link4RepoCommand.TryResolveDirectoryPath(
			relativePath,
			fileSystem,
			out string resolvedPath);

		// Assert
		result.Should().BeTrue(
			because: "relative env package paths should be handled as direct paths instead of being mistaken for environment names");
		resolvedPath.Should().Be(
			expectedPath,
			because: "the command should normalize the relative path before passing it to the linking flow");
	}

	[Test]
	[Description("Leaves plain environment names unresolved so the Windows-only registered-environment flow keeps working as before.")]
	public void TryResolveDirectoryPath_Should_Return_False_For_Plain_Environment_Name() {
		// Arrange
		MockFileSystem msFileSystem = new(new Dictionary<string, MockFileData>(), GetRootedPath("repo"));
		Clio.Common.IFileSystem fileSystem = new FileSystem(msFileSystem);

		// Act
		bool result = Link4RepoCommand.TryResolveDirectoryPath("dev", fileSystem, out string resolvedPath);

		// Assert
		result.Should().BeFalse(
			because: "plain environment names should still go through the registered-environment resolution branch");
		resolvedPath.Should().BeNull(
			because: "no filesystem path should be produced for a plain environment name");
	}
	#endregion

	#region Methods: Private

	private static string GetRootedPath(string leaf) {
		return OperatingSystem.IsWindows()
			? Path.Combine(@"C:\", leaf)
			: Path.Combine(Path.DirectorySeparatorChar.ToString(), leaf);
	}

	private sealed class TestableLink4RepoCommand : Link4RepoCommand {
		public string? CapturedSitePath { get; private set; }

		public TestableLink4RepoCommand(
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
			return 0;
		}
	}

	#endregion

}
