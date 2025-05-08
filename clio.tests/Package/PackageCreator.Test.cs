using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.Json;
using Autofac;
using Clio.Common;
using Clio.Package;
using Clio.Tests.Command;
using Clio.Tests.Extensions;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package;

[TestFixture]
internal class PackageCreatorTest : BaseClioModuleTests
{
    private const string PackagesPath = @"T:\\";
    private const string PackageNameOne = "TestPackageOne";
    private const string PackageNameTwo = "TestPackageTwo";
    private const string PackageNameThree = "TestPackageThree";

    private PackageCreator InitCreator() =>
        new(container.Resolve<EnvironmentSettings>(), container.Resolve<IWorkspace>(),
            container.Resolve<IWorkspaceSolutionCreator>(),
            container.Resolve<ITemplateProvider>(), container.Resolve<IWorkspacePathBuilder>(),
            container.Resolve<IStandalonePackageFileManager>(), container.Resolve<IJsonConverter>(),
            container.Resolve<IWorkingDirectoriesProvider>(), container.Resolve<IFileSystem>());

    protected override MockFileSystem CreateFs()
    {
        MockFileSystem x = (MockFileSystem)base.CreateFs();
        ILogger logger = Substitute.For<ILogger>();
        WorkingDirectoriesProvider wdp = new(logger, x);
        x.MockFolderWithDir(wdp.TemplateDirectory);
        return x;
    }

    [Test]
    public void Create_AddPackageToWorkspaceWithTwoApplication()
    {
        // Arrange
        PackageCreator creator = InitCreator();

        // Act
        creator.Create(PackagesPath, PackageNameOne, true);
        creator.Create(PackagesPath, PackageNameTwo, true);
        creator.Create(PackagesPath, PackageNameThree);

        // Assert
        string appDescriptorPathOne = Path.Combine(PackagesPath, PackageNameOne, "Files", "app-descriptor.json");
        string appDescriptorPathTwo = Path.Combine(PackagesPath, PackageNameTwo, "Files", "app-descriptor.json");
        string appDescriptorPathThree = Path.Combine(PackagesPath, PackageNameThree, "Files", "app-descriptor.json");

        fileSystem.File.Exists(appDescriptorPathOne).Should().BeTrue();
        fileSystem.File.Exists(appDescriptorPathTwo).Should().BeTrue();
        fileSystem.File.Exists(appDescriptorPathThree).Should().BeFalse();
    }

    [Test]
    public void Create_AddTwoApplicationsToWorkplace()
    {
        // Arrange
        PackageCreator creator = InitCreator();

        // Act
        creator.Create(PackagesPath, PackageNameOne, true);
        creator.Create(PackagesPath, PackageNameTwo, true);

        // Assert
        string appDescriptorPathOne = Path.Combine(PackagesPath, PackageNameOne, "Files", "app-descriptor.json");
        string appDescriptorPathTwo = Path.Combine(PackagesPath, PackageNameTwo, "Files", "app-descriptor.json");

        fileSystem.File.Exists(appDescriptorPathOne).Should().BeTrue();
        fileSystem.File.Exists(appDescriptorPathTwo).Should().BeTrue();
    }

    [Test]
    public void Create_AddTwoPackagesInEmptyWorkspaceByDefault()
    {
        // Arrange
        PackageCreator creator = InitCreator();

        // Act
        creator.Create(PackagesPath, PackageNameOne);
        creator.Create(PackagesPath, PackageNameTwo);

        // Assert
        string appDescriptorPathOne = Path.Combine(PackagesPath, PackageNameOne, "Files", "app-descriptor.json");
        string appDescriptorPathTwo = Path.Combine(PackagesPath, PackageNameTwo, "Files", "app-descriptor.json");

        fileSystem.File.Exists(appDescriptorPathOne).Should().BeFalse();
        fileSystem.File.Exists(appDescriptorPathTwo).Should().BeFalse();
    }

    [Test]
    public void Create_AddTwoPackagesWithoutApplication()
    {
        // Arrange
        PackageCreator creator = InitCreator();

        // Act
        creator.Create(PackagesPath, PackageNameOne, false);
        creator.Create(PackagesPath, PackageNameTwo, false);

        // Assert
        string appDescriptorPathOne = Path.Combine(PackagesPath, PackageNameOne, "Files", "app-descriptor.json");
        string appDescriptorPathTwo = Path.Combine(PackagesPath, PackageNameTwo, "Files", "app-descriptor.json");

        fileSystem.File.Exists(appDescriptorPathOne).Should().BeFalse();
        fileSystem.File.Exists(appDescriptorPathTwo).Should().BeFalse();
    }

    [Test]
    public void Create_RewritePackageIfPackagesWithSameNamesExistsOnDescriptor()
    {
        // Arrange
        PackageCreator creator = InitCreator();

        // Act
        creator.Create(PackagesPath, PackageNameOne, true);
        creator.Create(PackagesPath, PackageNameTwo);
        fileSystem.Directory.Delete(Path.Combine(PackagesPath, PackageNameTwo), true);
        string appDescriptorPathOne = Path.Combine(PackagesPath, PackageNameOne, "Files", "app-descriptor.json");
        string appDescriptorContent = fileSystem.File.ReadAllText(appDescriptorPathOne);
        AppDescriptorJson appDescriptor = JsonSerializer.Deserialize<AppDescriptorJson>(appDescriptorContent);
        appDescriptor.Packages.Add(new Clio.Package.Package { Name = PackageNameTwo, UId = Guid.NewGuid().ToString() });
        creator.SaveAppDescriptorToFile(appDescriptor, appDescriptorPathOne);
        creator.Create(PackagesPath, PackageNameTwo);
        appDescriptorContent = fileSystem.File.ReadAllText(appDescriptorPathOne);
        appDescriptor = JsonSerializer.Deserialize<AppDescriptorJson>(appDescriptorContent);
        appDescriptor.Packages.Count().Should().Be(2);
    }

    [Test]
    public void Create_RewritePackageIfPackageWithSameNameExistsOnDescriptor()
    {
        // Arrange
        PackageCreator creator = InitCreator();

        // Act
        creator.Create(PackagesPath, PackageNameOne, true);
        creator.Create(PackagesPath, PackageNameTwo);
        fileSystem.Directory.Delete(Path.Combine(PackagesPath, PackageNameTwo), true);
        creator.Create(PackagesPath, PackageNameTwo);
        string appDescriptorPathOne = Path.Combine(PackagesPath, PackageNameOne, "Files", "app-descriptor.json");
        string appDescriptorContent = fileSystem.File.ReadAllText(appDescriptorPathOne);
        AppDescriptorJson appDescriptor = JsonSerializer.Deserialize<AppDescriptorJson>(appDescriptorContent);
        appDescriptor.Packages.Count().Should().Be(2);
    }

    [Test]
    public void Create_ThrowExceptionIfPackageExists()
    {
        // Arrange
        PackageCreator creator = InitCreator();

        // Act
        creator.Create(PackagesPath, PackageNameOne, false);
        Assert.Throws<InvalidOperationException>(() => creator.Create(PackagesPath, PackageNameOne, false));
    }

    [Test]
    public void Create_TwoPackages()
    {
        // Arrange
        PackageCreator creator = InitCreator();

        // Act
        creator.Create(PackagesPath, PackageNameOne, true);
        creator.Create(PackagesPath, PackageNameTwo);

        // Assert
        string appDescriptorPathOne = Path.Combine(PackagesPath, PackageNameOne, "Files", "app-descriptor.json");
        string appDescriptorPathTwo = Path.Combine(PackagesPath, PackageNameTwo, "Files", "app-descriptor.json");

        fileSystem.File.Exists(appDescriptorPathOne).Should().BeTrue();
        fileSystem.File.Exists(appDescriptorPathTwo).Should().BeFalse();

        string appDescriptorContent = fileSystem.File.ReadAllText(appDescriptorPathOne);
        AppDescriptorJson appDescriptor = JsonSerializer.Deserialize<AppDescriptorJson>(appDescriptorContent);
        appDescriptor.Packages.Count().Should().Be(2);
    }

    [Test]
    public void Create_With()
    {
        // Arrange
        PackageCreator creator = InitCreator();

        // Act
        creator.Create(PackagesPath, PackageNameOne, true);

        // Assert
        string appDescriptorContent
            = fileSystem.File.ReadAllText(Path.Combine(PackagesPath, PackageNameOne, "Files", "app-descriptor.json"));
        AppDescriptorJson appDescriptor = JsonSerializer.Deserialize<AppDescriptorJson>(appDescriptorContent);

        appDescriptor.Name.Should().Be(PackageNameOne);
        appDescriptor.Code.Should().Be(PackageNameOne);
        appDescriptor.Color.Should().Be("#FFAC07");
        appDescriptor.Maintainer.Should().Be("Customer");
        appDescriptor.Version.Should().Be("0.1.0");
        appDescriptor.Packages.Should().HaveCount(1);
        appDescriptor.Packages.First().Name.Should().Be(PackageNameOne);
    }
}
