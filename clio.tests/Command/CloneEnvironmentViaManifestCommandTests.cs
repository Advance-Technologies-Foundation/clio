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
			IDataProvider provider = Substitute.For<IDataProvider>();
			CloneEnvironmentCommand cloneEnvironmentCommand = new CloneEnvironmentCommand(showDiffEnvironmentsCommand, applyEnvironmentManifestCommand, loggerMock, provider);
			
			var cloneEnvironmentCommandOptions = new CloneEnvironmentOptions() {
				Source = "sourceEnv",
				Target = "targetEnv"
			};


			cloneEnvironmentCommand.Execute(cloneEnvironmentCommandOptions);

			showDiffEnvironmentsCommand.Received(1)
				.Execute(Arg.Is<ShowDiffEnvironmentsOptions>( 
					arg => IsEqualEnvironmentOptions(cloneEnvironmentCommandOptions, arg)));
			applyEnvironmentManifestCommand.Received(1)
				.Execute(Arg.Is<ApplyEnvironmentManifestOptions>(
					arg => arg.Environment == cloneEnvironmentCommandOptions.Target));
		}

		private bool IsEqualEnvironmentOptions(ShowDiffEnvironmentsOptions expected, ShowDiffEnvironmentsOptions actual) {
			return expected.Source == actual.Source && expected.Target == actual.Target;
		}

	}

}
