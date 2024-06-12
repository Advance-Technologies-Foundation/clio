using Autofac;
using Clio.Command;
using Clio.Common;
using Clio.Tests.Extensions;
using NSubstitute;
using NUnit.Framework;
using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using ATF.Repository.Providers;
using NSubstitute.ReceivedExtensions;
using Clio.Command.PackageCommand;
using System.Collections.Generic;

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
			environmentManager.LoadEnvironmentManifestFromFile(Arg.Any<string>()).Returns(new EnvironmentManifest() {
				Packages = new List<CreatioManifestPackage>() {
					new CreatioManifestPackage() { Name = "Package1" },
					new CreatioManifestPackage() { Name = "Package2" }
				}
			});
			CloneEnvironmentCommand cloneEnvironmentCommand = new CloneEnvironmentCommand(showDiffEnvironmentsCommand, applyEnvironmentManifestCommand, pullPkgCommand, pushPackageCommand, environmentManager,loggerMock, provider);
			
			var cloneEnvironmentCommandOptions = new CloneEnvironmentOptions() {
				Source = "sourceEnv",
				Target = "targetEnv"
			};


			cloneEnvironmentCommand.Execute(cloneEnvironmentCommandOptions);

			showDiffEnvironmentsCommand.Received(1)
				.Execute(Arg.Is<ShowDiffEnvironmentsOptions>( 
					arg => IsEqualEnvironmentOptions(cloneEnvironmentCommandOptions, arg)));

			pullPkgCommand.Received(1)
				.Execute(Arg.Is<PullPkgOptions>( arg => arg.Name == "Package1"));
			pullPkgCommand.Received(1)
				.Execute(Arg.Is<PullPkgOptions>(arg => arg.Name == "Package2"));

			pushPackageCommand.Received(1)
				.Execute(Arg.Is<PushPkgOptions>(arg => arg.Environment == cloneEnvironmentCommandOptions.Target));

			applyEnvironmentManifestCommand.Received(1)
				.Execute(Arg.Is<ApplyEnvironmentManifestOptions>(
					arg => arg.Environment == cloneEnvironmentCommandOptions.Target));

		}


		private bool IsEqualEnvironmentOptions(ShowDiffEnvironmentsOptions expected, ShowDiffEnvironmentsOptions actual) {
			return expected.Source == actual.Source && expected.Target == actual.Target;
		}

	}

}
