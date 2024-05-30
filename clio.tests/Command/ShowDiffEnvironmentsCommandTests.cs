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


		[Test]
		public void Execute_ShouldReturnZero() {
			ILogger loggerMock = Substitute.For<ILogger>();
			var container = GetContainer();
			ShowDiffEnvironmentsCommand diffCommand = new(container.Resolve<IEnvironmentManager>(), container.Resolve<IDataProvider>(), loggerMock);
			diffCommand.Execute(new ShowDiffEnvironmentsOptions() {
				Source = "SourceEnv",
				Origin = "OriginEnv"
			});
			Assert.AreEqual("{}", _fileSystem.File.ReadAllText("diff-SourceEnv-OriginEnv.yaml").Trim());
		}

		[Test]
		public void MergeManifest_FindPackages() {
			ILogger loggerMock = Substitute.For<ILogger>();
			var container = GetContainer();
			var environmentManager = container.Resolve<IEnvironmentManager>();
			var sourceManifestFileName = "packages-source-env-manifest.yaml";
			var targetManifestFileName = "packages-target-env-manifest.yaml";
			var diffManifestFileName = "packages-diff.yaml";
			var sourceManifest = environmentManager.GetEnvironmentFromManifest(sourceManifestFileName);
			var targetManifest = environmentManager.GetEnvironmentFromManifest(targetManifestFileName);
			var expectedDiffManifest = environmentManager.GetEnvironmentFromManifest(diffManifestFileName);
			var actualDiffManifest = environmentManager.GetDiffManifest(sourceManifest, targetManifest);
			Assert.AreEqual(expectedDiffManifest, actualDiffManifest);
		}

		private IContainer GetContainer() {
			return MockDataContainer.GetContainer(_fileSystem);
		}

	}
}