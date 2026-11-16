using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using Clio.Requests;
using Clio.UserEnvironment;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// A package name, and in a versioned repository a package version, becomes a path segment under the repository and
/// under the environment package folder. These tests pin that a value which is not a plain name is refused before
/// the command probes, reads, deletes or links anything and before it writes to Creatio.
/// </summary>
[TestFixture]
[Property("Module", "Command")]
public class Link4RepoCommandPlainNameTests : BaseCommandTests<Link4RepoOptions> {

	#region Fields: Private

	private static readonly string EnvPkgPath = GetRootedPath("site", "Terrasoft.Configuration", "Pkg");
	private static readonly string RepoPath = GetRootedPath("work", "repo");

	private Clio.Common.IFileSystem _fileSystem = null!;
	private ILogger _logger = null!;
	private IApplicationPackageListProvider _packageListProvider = null!;
	private IJsonConverter _jsonConverter = null!;
	private ISysSettingsManager _sysSettingsManager = null!;
	private IPackageLockManager _packageLockManager = null!;
	private IFileDesignModePackages _fileDesignModePackages = null!;
	private RecordingLink4RepoCommand _command = null!;

	// Whether a descriptor.json is reported at the top level of the repository: true makes the repository flat.
	private bool _repoIsFlat;

	#endregion

	#region Setup

	[SetUp]
	public override void Setup() {
		base.Setup();
		_repoIsFlat = false;
		_logger = Substitute.For<ILogger>();
		_packageListProvider = Substitute.For<IApplicationPackageListProvider>();
		_jsonConverter = Substitute.For<IJsonConverter>();
		_sysSettingsManager = Substitute.For<ISysSettingsManager>();
		_packageLockManager = Substitute.For<IPackageLockManager>();
		_fileDesignModePackages = Substitute.For<IFileDesignModePackages>();
		_fileDesignModePackages.LoadPackagesToFileSystem().Returns(FileDesignModeLoadResult.Completed);
		_sysSettingsManager.GetSysSettingValueByCode("Maintainer").Returns("SiteMaintainer");
		_sysSettingsManager.UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>()).Returns(true);
		_jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(Arg.Any<string>())
			.Returns(new PackageDescriptorDto {
				Descriptor = new PackageDescriptor { Name = "FromFile", Maintainer = "FileMaintainer" }
			});

		_fileSystem = CreatePermissiveFileSystem();

		IValidator<Link4RepoOptions> validator = Substitute.For<IValidator<Link4RepoOptions>>();
		validator.Validate(Arg.Any<Link4RepoOptions>()).Returns(new ValidationResult());
		_command = new RecordingLink4RepoCommand(
			_logger,
			Substitute.For<IIisScanner>(),
			Substitute.For<ISettingsRepository>(),
			_fileSystem,
			new RfsEnvironment(
				Substitute.For<Clio.Common.IFileSystem>(),
				Substitute.For<IPackageUtilities>(),
				Substitute.For<ILogger>()),
			validator,
			_packageListProvider,
			_jsonConverter,
			_sysSettingsManager,
			_packageLockManager,
			_fileDesignModePackages);
	}

	[TearDown]
	public override void TearDown() {
		_fileSystem.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		_packageListProvider.ClearReceivedCalls();
		_jsonConverter.ClearReceivedCalls();
		_sysSettingsManager.ClearReceivedCalls();
		_packageLockManager.ClearReceivedCalls();
		_fileDesignModePackages.ClearReceivedCalls();
		base.TearDown();
	}

	#endregion

	#region Tests: --packages

	[TestCaseSource(nameof(HostileNames))]
	[Description("A --packages value that is not a plain name is refused before any file probe, descriptor read, Creatio call or link.")]
	public void Execute_Packages_NotAPlainName_IsRefusedBeforeAnythingIsTouched(string hostileName) {
		// Arrange
		_repoIsFlat = true;
		Link4RepoOptions options = new() {
			EnvPkgPath = EnvPkgPath,
			RepoPath = RepoPath,
			Packages = $"PkgA,{hostileName}"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "a package name that is not a plain name must fail the command");
		_logger.Received(1).WriteError(Arg.Is<string>(m =>
			m.Contains("--packages") && m.Contains($"'{hostileName}'") && m.Contains("not a plain package name")));
		_fileSystem.ReceivedCalls().Should().BeEmpty(
			because: "the name is refused before the first file-system call, so no folder or descriptor outside the repository is probed");
		_jsonConverter.ReceivedCalls().Should().BeEmpty(
			because: "no descriptor.json may be read for a refused name");
		_sysSettingsManager.ReceivedCalls().Should().BeEmpty(
			because: "the Maintainer setting must not be read or written for a refused name");
		_packageLockManager.ReceivedCalls().Should().BeEmpty(
			because: "no package may be unlocked for a refused name");
		_fileDesignModePackages.ReceivedCalls().Should().BeEmpty(
			because: "no export to the file system may start for a refused name");
		_command.LinkWithDirPathCalls.Should().BeEmpty(
			because: "nothing may be linked when one of the requested names is refused");
	}

	[TestCaseSource(nameof(HostileNames))]
	[Description("With --skip-preparation and --dry-run a --packages value that is not a plain name is still refused before anything is touched.")]
	public void Execute_Packages_NotAPlainName_IsRefusedWithSkipPreparationAndDryRun(string hostileName) {
		// Arrange
		Link4RepoOptions skipPreparation = new() {
			EnvPkgPath = EnvPkgPath,
			RepoPath = RepoPath,
			Packages = hostileName,
			SkipPreparation = true
		};
		Link4RepoOptions dryRun = new() {
			EnvPkgPath = EnvPkgPath,
			RepoPath = RepoPath,
			Packages = hostileName,
			SkipPreparation = true,
			DryRun = true
		};

		// Act
		int skipPreparationResult = _command.Execute(skipPreparation);
		int dryRunResult = _command.Execute(dryRun);

		// Assert
		skipPreparationResult.Should().Be(1,
			because: "skipping preparation must not let a name that is not a plain name reach the linking step");
		dryRunResult.Should().Be(1,
			because: "a dry run must report the refusal instead of announcing a link that would never be made");
		_fileSystem.ReceivedCalls().Should().BeEmpty(
			because: "the name is refused before the first file-system call");
		_command.LinkWithDirPathCalls.Should().BeEmpty(
			because: "nothing may be linked for a refused name");
	}

	[Test]
	[Description("Ordinary package names in --packages, with inner dots, underscores and surrounding spaces in the list, are prepared and linked as before.")]
	public void Execute_Packages_PlainNames_ArePreparedAndLinked() {
		// Arrange
		_repoIsFlat = true;
		Link4RepoOptions options = new() {
			EnvPkgPath = EnvPkgPath,
			RepoPath = RepoPath,
			Packages = "PkgA, Pkg.B_1 ,Crt-Base2"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "plain package names must keep working");
		_logger.DidNotReceive().WriteError(Arg.Any<string>());
		_sysSettingsManager.Received(1).UpdateSysSetting("Maintainer", "FileMaintainer");
		_packageLockManager.Received(1).Unlock(Arg.Is<IEnumerable<string>>(names =>
			names.SequenceEqual(new[] { "PkgA", "Pkg.B_1", "Crt-Base2" })));
		_command.LinkWithDirPathCalls.Should().ContainSingle(
				because: "the linking step runs once for the whole list")
			.Which.Should().Be((EnvPkgPath, RepoPath, "PkgA, Pkg.B_1 ,Crt-Base2"),
				because: "the package selector reaches the linking step unchanged");
	}

	[Test]
	[Description("The * selector is not treated as a package name and reaches the linking step.")]
	public void Execute_Packages_Star_IsNotRefused() {
		// Arrange
		Link4RepoOptions options = new() {
			EnvPkgPath = EnvPkgPath,
			RepoPath = RepoPath,
			Packages = "*"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "* selects every package of the repository and is not a folder name");
		_command.LinkWithDirPathCalls.Should().ContainSingle(
				because: "the * selector is linked by the linking step")
			.Which.packages.Should().Be("*", because: "the selector reaches the linking step unchanged");
	}

	#endregion

	#region Tests: --unlocked

	[TestCaseSource(nameof(HostileNames))]
	[Description("--unlocked on a versioned repository refuses a package name returned by the environment that is not a plain name, before the first delete or link, even when it follows valid packages.")]
	public void Execute_Unlocked_Versioned_EnvironmentPackageNameNotPlain_NothingDeletedOrLinked(string hostileName) {
		// Arrange
		_packageListProvider.GetPackages(Arg.Any<string>()).Returns(new List<PackageInfo> {
			CreatePackageInfo("PkgA", "1.2.3"),
			CreatePackageInfo(hostileName, "1.0.0"),
			CreatePackageInfo("PkgC", "8.1.0.5")
		});
		Link4RepoOptions options = CreateUnlockedOptions();

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "a package name from the environment that is not a plain name must fail the command");
		_logger.Received(1).WriteError(Arg.Is<string>(m =>
			m.Contains("The environment returned a package name that is not a plain name")
			&& m.Contains($"'{hostileName}'")
			&& m.Contains("Nothing was deleted or linked.")));
		AssertNothingDeletedOrLinked();
		FileSystemPathArguments().Should().NotContain(path => path.Contains(hostileName, StringComparison.Ordinal),
			because: "the refused name must never be joined into a path or probed on disk");
	}

	[TestCaseSource(nameof(HostileVersions))]
	[Description("--unlocked on a versioned repository refuses a package version returned by the environment that is not a plain name, before the first delete or link, even when it follows valid packages.")]
	public void Execute_Unlocked_Versioned_EnvironmentPackageVersionNotPlain_NothingDeletedOrLinked(
		string hostileVersion) {
		// Arrange
		_packageListProvider.GetPackages(Arg.Any<string>()).Returns(new List<PackageInfo> {
			CreatePackageInfo("PkgA", "1.2.3"),
			CreatePackageInfo("PkgB", hostileVersion)
		});
		Link4RepoOptions options = CreateUnlockedOptions();

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "a package version from the environment that is not a plain name must fail the command");
		_logger.Received(1).WriteError(Arg.Is<string>(m =>
			m.Contains("The environment returned a package version that is not a plain name")
			&& m.Contains($"'{hostileVersion}'")
			&& m.Contains("'PkgB'")
			&& m.Contains("Nothing was deleted or linked.")));
		AssertNothingDeletedOrLinked();
		FileSystemPathArguments().Should().NotContain(path => path.Contains(hostileVersion, StringComparison.Ordinal),
			because: "the refused version must never be joined into a path or probed on disk");
	}

	[TestCaseSource(nameof(HostileNames))]
	[Description("--unlocked --dry-run on a versioned repository refuses a package name that is not a plain name without probing it on disk.")]
	public void Execute_Unlocked_DryRun_EnvironmentPackageNameNotPlain_IsNotProbed(string hostileName) {
		// Arrange
		_packageListProvider.GetPackages(Arg.Any<string>()).Returns(new List<PackageInfo> {
			CreatePackageInfo("PkgA", "1.2.3"),
			CreatePackageInfo(hostileName, "1.0.0")
		});
		Link4RepoOptions options = CreateUnlockedOptions();
		options.DryRun = true;

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "a dry run must report the refusal the real run would make");
		AssertNothingDeletedOrLinked();
		FileSystemPathArguments().Should().NotContain(path => path.Contains(hostileName, StringComparison.Ordinal),
			because: "a dry run must not probe a path built from a refused name either");
	}

	[TestCaseSource(nameof(HostileNames))]
	[Description("--unlocked on a flat repository refuses a package name returned by the environment that is not a plain name and links nothing.")]
	public void Execute_Unlocked_Flat_EnvironmentPackageNameNotPlain_NothingLinked(string hostileName) {
		// Arrange
		_repoIsFlat = true;
		_packageListProvider.GetPackages(Arg.Any<string>()).Returns(new List<PackageInfo> {
			CreatePackageInfo("PkgA", "1.2.3"),
			CreatePackageInfo(hostileName, "1.0.0")
		});
		Link4RepoOptions options = CreateUnlockedOptions();

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "a package name from the environment that is not a plain name must fail the command");
		AssertNothingDeletedOrLinked();
	}

	[Test]
	[Description("--unlocked on a versioned repository still replaces the environment folder with a link for ordinary names and dotted versions.")]
	public void Execute_Unlocked_Versioned_PlainNamesAndVersions_AreLinked() {
		// Arrange
		_packageListProvider.GetPackages(Arg.Any<string>()).Returns(new List<PackageInfo> {
			CreatePackageInfo("PkgA", "1.2.3"),
			CreatePackageInfo("Pkg.B_1", "8.1.0.5")
		});
		Link4RepoOptions options = CreateUnlockedOptions();

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "plain package names and dotted versions must keep working");
		_logger.DidNotReceive().WriteError(Arg.Any<string>());
		_fileSystem.Received(1).DeleteDirectory(Path.Combine(EnvPkgPath, "PkgA"), true);
		_fileSystem.Received(1).CreateDirectorySymLink(
			Path.Combine(EnvPkgPath, "PkgA"), Path.Combine(RepoPath, "PkgA", "main", "1.2.3"));
		_fileSystem.Received(1).DeleteDirectory(Path.Combine(EnvPkgPath, "Pkg.B_1"), true);
		_fileSystem.Received(1).CreateDirectorySymLink(
			Path.Combine(EnvPkgPath, "Pkg.B_1"), Path.Combine(RepoPath, "Pkg.B_1", "main", "8.1.0.5"));
	}

	[Test]
	[Description("--unlocked on a flat repository does not check versions, which are not path segments there, and links the packages found in the repository.")]
	public void Execute_Unlocked_Flat_VersionIsNotAPathSegment_IsLinked() {
		// Arrange
		_repoIsFlat = true;
		_packageListProvider.GetPackages(Arg.Any<string>()).Returns(new List<PackageInfo> {
			CreatePackageInfo("PkgA", "1.2.3/rc")
		});
		Link4RepoOptions options = CreateUnlockedOptions();

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a flat repository has no version folders, so the version is never joined into a path");
		_command.LinkWithDirPathCalls.Should().ContainSingle(
				because: "the package found in the repository is handed to the linking step")
			.Which.Should().Be((EnvPkgPath, RepoPath, "PkgA"),
				because: "only the package name is used to link in a flat repository");
	}

	#endregion

	#region Helpers

	private static IEnumerable<string> HostileNames() {
		yield return "../../Outside";
		yield return "..";
		yield return "sub/Pkg";
		yield return @"sub\Pkg";
		yield return GetRootedPath("rooted", "Outside");
	}

	private static IEnumerable<string> HostileVersions() {
		return HostileNames().Append(@"..\..\Outside").Append("1.0.0.");
	}

	private static string GetRootedPath(params string[] parts) {
		string root = OperatingSystem.IsWindows() ? @"C:\" : "/";
		return Path.Combine(new[] { root }.Concat(parts).ToArray());
	}

	private static Link4RepoOptions CreateUnlockedOptions() {
		return new Link4RepoOptions {
			EnvPkgPath = EnvPkgPath,
			RepoPath = RepoPath,
			Unlocked = true,
			Environment = "dev"
		};
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

	/// <summary>
	/// A file system where every folder exists, which is the worst case for a name that leaves its root: a probe of
	/// <c>&lt;root&gt;/../../Outside</c> answers "exists", so unguarded code goes on to read, delete and link it.
	/// The repository has the package folders <c>PkgA</c> and <c>Pkg.B_1</c>, each with the branch <c>main</c>; it is
	/// versioned unless <see cref="_repoIsFlat"/> is set. No package in the environment folder has a descriptor.json.
	/// </summary>
	private Clio.Common.IFileSystem CreatePermissiveFileSystem() {
		Clio.Common.IFileSystem fileSystem = Substitute.For<Clio.Common.IFileSystem>();
		fileSystem.Combine(Arg.Any<string[]>()).Returns(call => Path.Combine(call.Arg<string[]>()));
		fileSystem.IsPathRooted(Arg.Any<string>()).Returns(call => Path.IsPathRooted(call.Arg<string>()));
		fileSystem.GetFullPath(Arg.Any<string>()).Returns(call => call.Arg<string>());
		fileSystem.ExistsDirectory(Arg.Any<string>()).Returns(call =>
			!string.Equals(call.Arg<string>(), Path.Combine(RepoPath, "packages"), StringComparison.Ordinal));
		fileSystem.ExistsFile(Arg.Any<string>()).Returns(call => {
			string path = call.Arg<string>();
			if (path.StartsWith(EnvPkgPath, StringComparison.Ordinal)) {
				return false;
			}
			bool isTopLevelRepoDescriptor = string.Equals(
				Path.GetDirectoryName(Path.GetDirectoryName(path)), RepoPath, StringComparison.Ordinal);
			return !isTopLevelRepoDescriptor || _repoIsFlat;
		});
		fileSystem.GetDirectories(Arg.Any<string>()).Returns(call => {
			string path = call.Arg<string>();
			return string.Equals(path, RepoPath, StringComparison.Ordinal)
				? new[] { Path.Combine(RepoPath, "PkgA"), Path.Combine(RepoPath, "Pkg.B_1") }
				: new[] { Path.Combine(path, "main") };
		});
		fileSystem.GetDirectoryInfo(Arg.Any<string>()).Returns(call => {
			System.IO.Abstractions.IDirectoryInfo info = Substitute.For<System.IO.Abstractions.IDirectoryInfo>();
			info.Name.Returns(Path.GetFileName(call.Arg<string>()));
			return info;
		});
		fileSystem.ClearReceivedCalls();
		return fileSystem;
	}

	private IEnumerable<string> FileSystemPathArguments() {
		return _fileSystem.ReceivedCalls()
			.SelectMany(call => call.GetArguments())
			.SelectMany(argument => argument switch {
				string path => new[] { path },
				string[] paths => paths,
				_ => Array.Empty<string>()
			});
	}

	private void AssertNothingDeletedOrLinked() {
		_fileSystem.DidNotReceive().DeleteDirectory(Arg.Any<string>(), Arg.Any<bool>());
		_fileSystem.DidNotReceive().DeleteDirectory(Arg.Any<string>());
		_fileSystem.DidNotReceive().DeleteDirectoryIfExists(Arg.Any<string>());
		_fileSystem.DidNotReceive().SafeDeleteDirectory(Arg.Any<string>());
		_fileSystem.DidNotReceive().CreateDirectorySymLink(Arg.Any<string>(), Arg.Any<string>());
		_command.LinkWithDirPathCalls.Should().BeEmpty(
			because: "the linking step must not run when the environment returned a value that is not a plain name");
		_sysSettingsManager.ReceivedCalls().Should().BeEmpty(
			because: "the --unlocked flow never writes a setting, refused or not");
		_packageLockManager.ReceivedCalls().Should().BeEmpty(
			because: "the --unlocked flow never unlocks a package, refused or not");
	}

	#endregion

	#region TestableCommand

	/// <summary>
	/// Runs the real command, including the real versioned linking, and records the hand-off to
	/// <see cref="RfsEnvironment"/>, which works on the real disk and is therefore replaced.
	/// </summary>
	private sealed class RecordingLink4RepoCommand : Link4RepoCommand {

		public List<(string sitePath, string repoPath, string packages)> LinkWithDirPathCalls { get; } = [];

		public RecordingLink4RepoCommand(
			ILogger logger,
			IIisScanner iisScanner,
			ISettingsRepository settingsRepository,
			Clio.Common.IFileSystem fileSystem,
			RfsEnvironment rfsEnvironment,
			IValidator<Link4RepoOptions> validator,
			IApplicationPackageListProvider applicationPackageListProvider,
			IJsonConverter jsonConverter,
			ISysSettingsManager sysSettingsManager,
			IPackageLockManager packageLockManager,
			IFileDesignModePackages fileDesignModePackages)
			: base(logger, iisScanner, settingsRepository, fileSystem, rfsEnvironment, validator,
				applicationPackageListProvider, jsonConverter, sysSettingsManager, packageLockManager,
				fileDesignModePackages) {
		}

		protected override int HandleLinkWithDirPath(string sitePath, string repoPath, string packages) {
			LinkWithDirPathCalls.Add((sitePath, repoPath, packages));
			return 0;
		}
	}

	#endregion

}
