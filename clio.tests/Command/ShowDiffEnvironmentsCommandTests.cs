using ATF.Repository.Providers;
using Autofac;
using Clio.Command;
using Clio.Common;
using Clio.Tests.Extensions;
using Clio.Tests.Infrastructure;
using DocumentFormat.OpenXml.Presentation;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using YamlDotNet.Serialization;

namespace Clio.Tests.Command
{
	[TestFixture]
	internal class ShowDiffEnvironmentsCommandTests : BaseCommandTests<ShowDiffEnvironmentsOptions>
	{
		protected override MockFileSystem CreateFs() {
			var mockFS = base.CreateFs();
			mockFS.MockExamplesFolder("odata_data_examples", "odata_data_examples");
			mockFS.MockExamplesFolder("diff-manifest");
			return mockFS;
		}

		[TestCase("packages-source-env-manifest.yaml","packages-target-env-manifest.yaml","packages-diff.yaml")]
		[TestCase("syssettings-source-env-manifest.yaml","syssettings-target-env-manifest.yaml","syssettings-diff.yaml")]
		[TestCase("features-source-env-manifest.yaml","features-target-env-manifest.yaml","features-diff.yaml")]
		public void MergeManifest_FindPackages(string sourceManifestFileName, string targetManifestFileName,string diffManifestFileName) {
			ILogger loggerMock = Substitute.For<ILogger>();
			var container = GetContainer();
			var environmentManager = container.Resolve<IEnvironmentManager>();
			var sourceManifest = environmentManager.LoadEnvironmentManifestFromFile(sourceManifestFileName);
			var targetManifest = environmentManager.LoadEnvironmentManifestFromFile(targetManifestFileName);
			EnvironmentManifest expectedDiffManifest = environmentManager.LoadEnvironmentManifestFromFile(diffManifestFileName);
			var actualDiffManifest = environmentManager.GetDiffManifest(sourceManifest, targetManifest);
			expectedDiffManifest.Should().BeEquivalentTo(actualDiffManifest);
		}

		
		private IContainer GetContainer() {
			return MockDataContainer.GetContainer(FileSystem);
		}

	}
}