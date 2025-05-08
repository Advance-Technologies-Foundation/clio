using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ATF.Repository.Providers;
using Autofac;
using Clio.Command;
using Clio.Common;
using Clio.Tests.Extensions;
using Clio.Tests.Infrastructure;
using DocumentFormat.OpenXml.Presentation;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using YamlDotNet.Serialization;

namespace Clio.Tests.Command;

[TestFixture]
internal class ShowDiffEnvironmentsCommandTests : BaseCommandTests<ShowDiffEnvironmentsOptions>
{
    protected override MockFileSystem CreateFs()
    {
        MockFileSystem mockFS = base.CreateFs();
        mockFS.MockExamplesFolder("odata_data_examples", "odata_data_examples");
        mockFS.MockExamplesFolder("diff-manifest");
        return mockFS;
    }

    [TestCase("packages-source-env-manifest.yaml", "packages-target-env-manifest.yaml", "packages-diff.yaml")]
    [TestCase("syssettings-source-env-manifest.yaml", "syssettings-target-env-manifest.yaml", "syssettings-diff.yaml")]
    [TestCase("features-source-env-manifest.yaml", "features-target-env-manifest.yaml", "features-diff.yaml")]
    public void MergeManifest_FindPackages(string sourceManifestFileName, string targetManifestFileName,
        string diffManifestFileName)
    {
        _ = Substitute.For<ILogger>();
        IContainer container = GetContainer();
        IEnvironmentManager environmentManager = container.Resolve<IEnvironmentManager>();
        EnvironmentManifest sourceManifest = environmentManager.LoadEnvironmentManifestFromFile(sourceManifestFileName);
        EnvironmentManifest targetManifest = environmentManager.LoadEnvironmentManifestFromFile(targetManifestFileName);
        EnvironmentManifest expectedDiffManifest =
            environmentManager.LoadEnvironmentManifestFromFile(diffManifestFileName);
        EnvironmentManifest actualDiffManifest = environmentManager.GetDiffManifest(sourceManifest, targetManifest);
        expectedDiffManifest.Should().BeEquivalentTo(actualDiffManifest);
    }

    private IContainer GetContainer() => MockDataContainer.GetContainer(fileSystem);
}
