using System;
using Autofac;
using Clio.Command;
using Clio.Common;
using Clio.Tests.Extensions;
using NSubstitute;
using NUnit.Framework;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using ATF.Repository.Providers;
using NSubstitute.ReceivedExtensions;
using Clio.Command.PackageCommand;
using System.Collections.Generic;
using System.IO.Compression;
using Common.Logging.Configuration;

namespace Clio.Tests.Command
{

	[TestFixture]
	internal class CloneEnvironmentsCommandTests : BaseCommandTests<CloneEnvironmentOptions>
	{
		[Test]
		public void CloneEnvironmentWithFeatureTest() {
			ILogger loggerMock = Substitute.For<ILogger>();
			ShowDiffEnvironmentsCommand showDiffEnvironmentsCommand = Substitute.For<ShowDiffEnvironmentsCommand>();
			ApplyEnvironmentManifestCommand applyEnvironmentManifestCommand = Substitute.For<ApplyEnvironmentManifestCommand>();
			PullPkgCommand pullPkgCommand = Substitute.For<PullPkgCommand>();
			PushPackageCommand pushPackageCommand = Substitute.For<PushPackageCommand>();
			IDataProvider provider = Substitute.For<IDataProvider>();
			IEnvironmentManager environmentManager = Substitute.For<IEnvironmentManager>();
			ICompressionUtilities compressionUtilities = Substitute.For<ICompressionUtilities>();
			IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
			IFileSystem fileSystem = Substitute.For<IFileSystem>();
			string tempPath = "TempPath";
			workingDirectoriesProvider.CreateTempDirectory().Returns(tempPath);
			environmentManager.LoadEnvironmentManifestFromFile(Arg.Any<string>()).Returns(new EnvironmentManifest() {
				Packages = new List<CreatioManifestPackage>() {
					new CreatioManifestPackage() { Name = "Package1" },
					new CreatioManifestPackage() { Name = "Package2" }
				}
			});
			CloneEnvironmentCommand cloneEnvironmentCommand = new CloneEnvironmentCommand(showDiffEnvironmentsCommand, 
				applyEnvironmentManifestCommand, pullPkgCommand, pushPackageCommand, environmentManager,loggerMock,
				provider, compressionUtilities, workingDirectoriesProvider, fileSystem);
			
			var cloneEnvironmentCommandOptions = new CloneEnvironmentOptions() {
				Source = "sourceEnv",
				Target = "targetEnv",
			};

			// Act
			cloneEnvironmentCommand.Execute(cloneEnvironmentCommandOptions);

			workingDirectoriesProvider.Received(1).CreateTempDirectory();

			showDiffEnvironmentsCommand.Received(1)
				.Execute(Arg.Is<ShowDiffEnvironmentsOptions>( 
					arg => IsEqualEnvironmentOptions(cloneEnvironmentCommandOptions, arg)
					&& arg.FileName == Path.Combine(tempPath,
						$"from_{cloneEnvironmentCommandOptions.Source}_to_{cloneEnvironmentCommandOptions.Target}.yaml")));

			fileSystem.Received(1).CreateDirectory(Path.Combine(tempPath, "SourceZipPackages"));
			pullPkgCommand.Received(1)
				.Execute(Arg.Is<PullPkgOptions>( arg => arg.Name == "Package1"
					&& arg.DestPath == Path.Combine(tempPath, "SourceZipPackages")));
			pullPkgCommand.Received(1)
				.Execute(Arg.Is<PullPkgOptions>(arg => arg.Name == "Package2" 
					&& arg.DestPath == Path.Combine(tempPath, "SourceZipPackages")));

			string sourceGzPackages = Path.Combine(tempPath, "SourceGzPackages");
			fileSystem.Received(1).CreateDirectory(sourceGzPackages);
			compressionUtilities.Received(1).Unzip(Path.Combine(tempPath, "SourceZipPackages", "Package1.zip"),
				sourceGzPackages);
			compressionUtilities.Received(1).Unzip(Path.Combine(tempPath, "SourceZipPackages", "Package2.zip"),
				sourceGzPackages);

			string commonPackagesZipPath = Path.Combine(tempPath,
				$"from_{cloneEnvironmentCommandOptions.Source}_to_{cloneEnvironmentCommandOptions.Target}.zip");
			compressionUtilities.Received(1).Zip(sourceGzPackages, commonPackagesZipPath);

			pushPackageCommand.Received(1)
				.Execute(Arg.Is<PushPkgOptions>(arg => arg.Environment == cloneEnvironmentCommandOptions.Target 
					&& arg.Name == commonPackagesZipPath));

			applyEnvironmentManifestCommand.Received(1)
				.Execute(Arg.Is<ApplyEnvironmentManifestOptions>(
					arg => arg.Environment == cloneEnvironmentCommandOptions.Target));

			workingDirectoriesProvider.Received(1).DeleteDirectoryIfExists(Arg.Is(tempPath));

		}


		[Test]
		public void CloneEnvironmentWithWorkingDirectoryTest() {
			ILogger loggerMock = Substitute.For<ILogger>();
			ShowDiffEnvironmentsCommand showDiffEnvironmentsCommand = Substitute.For<ShowDiffEnvironmentsCommand>();
			ApplyEnvironmentManifestCommand applyEnvironmentManifestCommand = Substitute.For<ApplyEnvironmentManifestCommand>();
			PullPkgCommand pullPkgCommand = Substitute.For<PullPkgCommand>();
			PushPackageCommand pushPackageCommand = Substitute.For<PushPackageCommand>();
			IDataProvider provider = Substitute.For<IDataProvider>();
			IEnvironmentManager environmentManager = Substitute.For<IEnvironmentManager>();
			ICompressionUtilities compressionUtilities = Substitute.For<ICompressionUtilities>();
			IWorkingDirectoriesProvider workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
			IFileSystem fileSystem = Substitute.For<IFileSystem>();
			string workingDirectory = "WorkingDirectory";
			workingDirectoriesProvider.CreateTempDirectory().Returns(workingDirectory);
			environmentManager.LoadEnvironmentManifestFromFile(Arg.Any<string>()).Returns(new EnvironmentManifest() {
				Packages = new List<CreatioManifestPackage>() {
					new CreatioManifestPackage() { Name = "Package1" },
					new CreatioManifestPackage() { Name = "Package2" }
				}
			});
			CloneEnvironmentCommand cloneEnvironmentCommand = new CloneEnvironmentCommand(showDiffEnvironmentsCommand,
				applyEnvironmentManifestCommand, pullPkgCommand, pushPackageCommand, environmentManager, loggerMock,
				provider, compressionUtilities, workingDirectoriesProvider, fileSystem);
			var cloneEnvironmentCommandOptions = new CloneEnvironmentOptions() {
				Source = "sourceEnv",
				Target = "targetEnv",
				WorkingDirectory = workingDirectory
			};

			// Act
			cloneEnvironmentCommand.Execute(cloneEnvironmentCommandOptions);

			workingDirectoriesProvider.DidNotReceive().CreateTempDirectory();

			showDiffEnvironmentsCommand.Received(1)
				.Execute(Arg.Is<ShowDiffEnvironmentsOptions>(
					arg => IsEqualEnvironmentOptions(cloneEnvironmentCommandOptions, arg)
					&& arg.FileName == Path.Combine(workingDirectory,
						$"from_{cloneEnvironmentCommandOptions.Source}_to_{cloneEnvironmentCommandOptions.Target}.yaml")));

			fileSystem.Received(1).CreateDirectory(Path.Combine(workingDirectory, "SourceZipPackages"));
			pullPkgCommand.Received(1)
				.Execute(Arg.Is<PullPkgOptions>(arg => arg.Name == "Package1"
					&& arg.DestPath == Path.Combine(workingDirectory, "SourceZipPackages")));
			pullPkgCommand.Received(1)
				.Execute(Arg.Is<PullPkgOptions>(arg => arg.Name == "Package2"
					&& arg.DestPath == Path.Combine(workingDirectory, "SourceZipPackages")));

			string sourceGzPackages = Path.Combine(workingDirectory, "SourceGzPackages");
			fileSystem.Received(1).CreateDirectory(sourceGzPackages);
			compressionUtilities.Received(1).Unzip(Path.Combine(workingDirectory, "SourceZipPackages", "Package1.zip"),
				sourceGzPackages);
			compressionUtilities.Received(1).Unzip(Path.Combine(workingDirectory, "SourceZipPackages", "Package2.zip"),
				sourceGzPackages);

			string commonPackagesZipPath = Path.Combine(workingDirectory,
				$"from_{cloneEnvironmentCommandOptions.Source}_to_{cloneEnvironmentCommandOptions.Target}.zip");
			compressionUtilities.Received(1).Zip(sourceGzPackages, commonPackagesZipPath);

			pushPackageCommand.Received(1)
				.Execute(Arg.Is<PushPkgOptions>(arg => arg.Environment == cloneEnvironmentCommandOptions.Target
					&& arg.Name == commonPackagesZipPath));

			applyEnvironmentManifestCommand.Received(1)
				.Execute(Arg.Is<ApplyEnvironmentManifestOptions>(
					arg => arg.Environment == cloneEnvironmentCommandOptions.Target));

			workingDirectoriesProvider.DidNotReceive().DeleteDirectoryIfExists(Arg.Is(workingDirectory));

		}


		private bool IsEqualEnvironmentOptions(ShowDiffEnvironmentsOptions expected, ShowDiffEnvironmentsOptions actual) {
			return expected.Source == actual.Source && expected.Target == actual.Target;
		}

	}

}
