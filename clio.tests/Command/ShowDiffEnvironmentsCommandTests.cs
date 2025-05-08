using System.IO.Abstractions.TestingHelpers;
using Autofac;
using Clio.Command;
using Clio.Common;
using Clio.Tests.Extensions;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
internal class ShowDiffEnvironmentsCommandTests : BaseCommandTests<ShowDiffEnvironmentsOptions>
{

    #region Methods: Private

    private IContainer GetContainer()
    {
        return MockDataContainer.GetContainer(FileSystem);
    }

    #endregion

    #region Methods: Protected

    protected override MockFileSystem CreateFs()
    {
        MockFileSystem mockFS = base.CreateFs();
        mockFS.MockExamplesFolder("odata_data_examples", "odata_data_examples");
        mockFS.MockExamplesFolder("diff-manifest");
        return mockFS;
    }

    #endregion

    [TestCase("packages-source-env-manifest.yaml", "packages-target-env-manifest.yaml", "packages-diff.yaml")]
    [TestCase("syssettings-source-env-manifest.yaml", "syssettings-target-env-manifest.yaml", "syssettings-diff.yaml")]
    [TestCase("features-source-env-manifest.yaml", "features-target-env-manifest.yaml", "features-diff.yaml")]
    public void MergeManifest_FindPackages(string sourceManifestFileName, string targetManifestFileName,
        string diffManifestFileName)
    {
        ILogger loggerMock = Substitute.For<ILogger>();
        IContainer container = GetContainer();
        IEnvironmentManager environmentManager = container.Resolve<IEnvironmentManager>();
        EnvironmentManifest sourceManifest = environmentManager.LoadEnvironmentManifestFromFile(sourceManifestFileName);
        EnvironmentManifest targetManifest = environmentManager.LoadEnvironmentManifestFromFile(targetManifestFileName);
        EnvironmentManifest expectedDiffManifest
            = environmentManager.LoadEnvironmentManifestFromFile(diffManifestFileName);
        EnvironmentManifest actualDiffManifest = environmentManager.GetDiffManifest(sourceManifest, targetManifest);
        expectedDiffManifest.Should().BeEquivalentTo(actualDiffManifest);
    }

}
