using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Autofac;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using Clio.Tests.Command;
using Clio.Tests.Extensions;
using Clio.Tests.Infrastructure;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Tests.Package
{
    internal class PackageCreator_Test : BaseClioModuleTests
    {

        protected override IFileSystem CreateFs(){
            var x =  (MockFileSystem)base.CreateFs();
            var logger = Substitute.For<ILogger>();
            var wdp = new WorkingDirectoriesProvider(logger);
            
            x.MockFolder(wdp.TemplateDirectory);
            return x;
        }

        [Test]
        public void Create_With() {

            //Arrange
            PackageCreator creator = new PackageCreator(_container.Resolve<EnvironmentSettings>(), _container.Resolve<IWorkspace>(), _container.Resolve<IWorkspaceSolutionCreator>(),
                _container.Resolve<ITemplateProvider>(), _container.Resolve<IWorkspacePathBuilder>(),
                _container.Resolve<IStandalonePackageFileManager>(), _container.Resolve<IJsonConverter>(),
                _container.Resolve<IWorkingDirectoriesProvider>(), _container.Resolve<Clio.Common.IFileSystem>());

            string packagesPath= @"T:\\";
            string packageName ="TestPackage";

            //Act
            creator.Create(packagesPath,packageName, true);

            //Assert
            string appDescriptorContent = _fileSystem.File.ReadAllText(Path.Combine(packagesPath,packageName,"Files","app-descriptor.json"));
            var appDescriptor = JsonSerializer.Deserialize<AppDescriptorJson>(appDescriptorContent);
            
            appDescriptor.Name.Should().Be(packageName);
            appDescriptor.Code.Should().Be(packageName);
            appDescriptor.Color.Should().Be("#FFAC07");
            appDescriptor.Maintainer.Should().Be("Customer");
            appDescriptor.Version.Should().Be("0.1.0");
            appDescriptor.Packages.Should().HaveCount(1);
            appDescriptor.Packages.First().Name.Should().Be(packageName);
        }

        [Test]
        public void Create_TwoPackages() {

            //Arrange
            PackageCreator creator = new PackageCreator(_container.Resolve<EnvironmentSettings>(), _container.Resolve<IWorkspace>(), _container.Resolve<IWorkspaceSolutionCreator>(),
                _container.Resolve<ITemplateProvider>(), _container.Resolve<IWorkspacePathBuilder>(),
                _container.Resolve<IStandalonePackageFileManager>(), _container.Resolve<IJsonConverter>(),
                _container.Resolve<IWorkingDirectoriesProvider>(), _container.Resolve<Clio.Common.IFileSystem>());

            string packagesPath = @"T:\\";
            string packageNameOne = "TestPackageOne";
            string packageNameTwo = "TestPackageTwo";

            //Act
            creator.Create(packagesPath, packageNameOne, true);
            creator.Create(packagesPath, packageNameTwo);

            //Assert
            string appDescriptorPathOne = Path.Combine(packagesPath, packageNameOne, "Files", "app-descriptor.json");
            string appDescriptorPathTwo = Path.Combine(packagesPath, packageNameTwo, "Files", "app-descriptor.json");

            _fileSystem.File.Exists(appDescriptorPathOne).Should().BeTrue();
            _fileSystem.File.Exists(appDescriptorPathTwo).Should().BeFalse();

            string appDescriptorContent = _fileSystem.File.ReadAllText(appDescriptorPathOne);
            var appDescriptor = JsonSerializer.Deserialize<AppDescriptorJson>(appDescriptorContent);
            appDescriptor.Packages.Count().Should().Be(2);
        }

        [Test]
        public void Create_AddTwoPackagesInEmptyWorkspaceByDefault() {

            //Arrange
            PackageCreator creator = new PackageCreator(_container.Resolve<EnvironmentSettings>(), _container.Resolve<IWorkspace>(), _container.Resolve<IWorkspaceSolutionCreator>(),
                _container.Resolve<ITemplateProvider>(), _container.Resolve<IWorkspacePathBuilder>(),
                _container.Resolve<IStandalonePackageFileManager>(), _container.Resolve<IJsonConverter>(),
                _container.Resolve<IWorkingDirectoriesProvider>(), _container.Resolve<Clio.Common.IFileSystem>());

            string packagesPath = @"T:\\";
            string packageNameOne = "TestPackageOne";
            string packageNameTwo = "TestPackageTwo";

            //Act
            creator.Create(packagesPath, packageNameOne);
            creator.Create(packagesPath, packageNameTwo);

            //Assert
            string appDescriptorPathOne = Path.Combine(packagesPath, packageNameOne, "Files", "app-descriptor.json");
            string appDescriptorPathTwo = Path.Combine(packagesPath, packageNameTwo, "Files", "app-descriptor.json");

            _fileSystem.File.Exists(appDescriptorPathOne).Should().BeFalse();
            _fileSystem.File.Exists(appDescriptorPathTwo).Should().BeFalse();
        }

        [Test]
        public void Create_AddPackageToWorkspaceWithTwoApplication() {

            //Arrange
            PackageCreator creator = new PackageCreator(_container.Resolve<EnvironmentSettings>(), _container.Resolve<IWorkspace>(), _container.Resolve<IWorkspaceSolutionCreator>(),
                _container.Resolve<ITemplateProvider>(), _container.Resolve<IWorkspacePathBuilder>(),
                _container.Resolve<IStandalonePackageFileManager>(), _container.Resolve<IJsonConverter>(),
                _container.Resolve<IWorkingDirectoriesProvider>(), _container.Resolve<Clio.Common.IFileSystem>());

            string packagesPath = @"T:\\";
            string packageNameOne = "TestPackageOne";
            string packageNameTwo = "TestPackageTwo";
            string packageNameThree = "TestPackageThree";

            //Act
            creator.Create(packagesPath, packageNameOne, true);
            creator.Create(packagesPath, packageNameTwo, true);
            creator.Create(packagesPath, packageNameThree);

            //Assert
            string appDescriptorPathOne = Path.Combine(packagesPath, packageNameOne, "Files", "app-descriptor.json");
            string appDescriptorPathTwo = Path.Combine(packagesPath, packageNameTwo, "Files", "app-descriptor.json");
            string appDescriptorPathThree = Path.Combine(packagesPath, packageNameThree, "Files", "app-descriptor.json");

            _fileSystem.File.Exists(appDescriptorPathOne).Should().BeTrue();
            _fileSystem.File.Exists(appDescriptorPathTwo).Should().BeTrue();
            _fileSystem.File.Exists(appDescriptorPathThree).Should().BeFalse();
        }

        [Test]
        public void Create_AddTwoApplicationsToWorkplace() {

            //Arrange
            PackageCreator creator = new PackageCreator(_container.Resolve<EnvironmentSettings>(), _container.Resolve<IWorkspace>(), _container.Resolve<IWorkspaceSolutionCreator>(),
                _container.Resolve<ITemplateProvider>(), _container.Resolve<IWorkspacePathBuilder>(),
                _container.Resolve<IStandalonePackageFileManager>(), _container.Resolve<IJsonConverter>(),
                _container.Resolve<IWorkingDirectoriesProvider>(), _container.Resolve<Clio.Common.IFileSystem>());

            string packagesPath = @"T:\\";
            string packageNameOne = "TestPackageOne";
            string packageNameTwo = "TestPackageTwo";

            //Act
            creator.Create(packagesPath, packageNameOne, true);
            creator.Create(packagesPath, packageNameTwo, true);

            //Assert
            string appDescriptorPathOne = Path.Combine(packagesPath, packageNameOne, "Files", "app-descriptor.json");
            string appDescriptorPathTwo = Path.Combine(packagesPath, packageNameTwo, "Files", "app-descriptor.json");

            _fileSystem.File.Exists(appDescriptorPathOne).Should().BeTrue();
            _fileSystem.File.Exists(appDescriptorPathTwo).Should().BeTrue();
        }

        [Test]
        public void Create_AddTwoPackagesWithoutApplication() {

            //Arrange
            PackageCreator creator = new PackageCreator(_container.Resolve<EnvironmentSettings>(), _container.Resolve<IWorkspace>(), _container.Resolve<IWorkspaceSolutionCreator>(),
                _container.Resolve<ITemplateProvider>(), _container.Resolve<IWorkspacePathBuilder>(),
                _container.Resolve<IStandalonePackageFileManager>(), _container.Resolve<IJsonConverter>(),
                _container.Resolve<IWorkingDirectoriesProvider>(), _container.Resolve<Clio.Common.IFileSystem>());

            string packagesPath = @"T:\\";
            string packageNameOne = "TestPackageOne";
            string packageNameTwo = "TestPackageTwo";

            //Act
            creator.Create(packagesPath, packageNameOne, false);
            creator.Create(packagesPath, packageNameTwo, false);

            //Assert
            string appDescriptorPathOne = Path.Combine(packagesPath, packageNameOne, "Files", "app-descriptor.json");
            string appDescriptorPathTwo = Path.Combine(packagesPath, packageNameTwo, "Files", "app-descriptor.json");

            _fileSystem.File.Exists(appDescriptorPathOne).Should().BeFalse();
            _fileSystem.File.Exists(appDescriptorPathTwo).Should().BeFalse();
        }

        [Test]
        public void Create_ThrowExceptionIfPackageExists() {

            //Arrange
            PackageCreator creator = new PackageCreator(_container.Resolve<EnvironmentSettings>(), _container.Resolve<IWorkspace>(), _container.Resolve<IWorkspaceSolutionCreator>(),
                _container.Resolve<ITemplateProvider>(), _container.Resolve<IWorkspacePathBuilder>(),
                _container.Resolve<IStandalonePackageFileManager>(), _container.Resolve<IJsonConverter>(),
                _container.Resolve<IWorkingDirectoriesProvider>(), _container.Resolve<Clio.Common.IFileSystem>());

            string packagesPath = @"T:\\";
            string packageNameOne = "TestPackageOne";

            //Act
            creator.Create(packagesPath, packageNameOne, false);
            Assert.Throws<InvalidOperationException>(() => creator.Create(packagesPath, packageNameOne, false));

        }

        [Test]
        public void Create_RewritePackageIfPackageWithSameNameExistsOnDescriptor() {

            //Arrange
            PackageCreator creator = new PackageCreator(_container.Resolve<EnvironmentSettings>(), _container.Resolve<IWorkspace>(), _container.Resolve<IWorkspaceSolutionCreator>(),
                _container.Resolve<ITemplateProvider>(), _container.Resolve<IWorkspacePathBuilder>(),
                _container.Resolve<IStandalonePackageFileManager>(), _container.Resolve<IJsonConverter>(),
                _container.Resolve<IWorkingDirectoriesProvider>(), _container.Resolve<Clio.Common.IFileSystem>());

            string packagesPath = @"T:\\";
            string packageNameOne = "TestPackageOne";
            string packageNameTwo = "TestPackageTwo";

            //Act
            creator.Create(packagesPath, packageNameOne, true);
            creator.Create(packagesPath, packageNameTwo);
            _fileSystem.Directory.Delete(Path.Combine(packagesPath, packageNameTwo), true);
            creator.Create(packagesPath, packageNameTwo);
            string appDescriptorPathOne = Path.Combine(packagesPath, packageNameOne, "Files", "app-descriptor.json");
            string appDescriptorContent = _fileSystem.File.ReadAllText(appDescriptorPathOne);
            var appDescriptor = JsonSerializer.Deserialize<AppDescriptorJson>(appDescriptorContent);
            appDescriptor.Packages.Count().Should().Be(2);
        }

        [Test]
        public void Create_RewritePackageIfPackagesWithSameNamesExistsOnDescriptor() {

            //Arrange
            PackageCreator creator = new PackageCreator(_container.Resolve<EnvironmentSettings>(), _container.Resolve<IWorkspace>(), _container.Resolve<IWorkspaceSolutionCreator>(),
                _container.Resolve<ITemplateProvider>(), _container.Resolve<IWorkspacePathBuilder>(),
                _container.Resolve<IStandalonePackageFileManager>(), _container.Resolve<IJsonConverter>(),
                _container.Resolve<IWorkingDirectoriesProvider>(), _container.Resolve<Clio.Common.IFileSystem>());

            string packagesPath = @"T:\\";
            string packageNameOne = "TestPackageOne";
            string packageNameTwo = "TestPackageTwo";

            //Act
            creator.Create(packagesPath, packageNameOne, true);
            creator.Create(packagesPath, packageNameTwo);
            _fileSystem.Directory.Delete(Path.Combine(packagesPath, packageNameTwo), true);
            string appDescriptorPathOne = Path.Combine(packagesPath, packageNameOne, "Files", "app-descriptor.json");
            string appDescriptorContent = _fileSystem.File.ReadAllText(appDescriptorPathOne);
            var appDescriptor = JsonSerializer.Deserialize<AppDescriptorJson>(appDescriptorContent);
            appDescriptor.Packages.Add(new Clio.Package.Package() { Name = packageNameTwo, UId = Guid.NewGuid().ToString() });
            creator.SaveAppDescriptorToFile(appDescriptor, appDescriptorPathOne);
            creator.Create(packagesPath, packageNameTwo);
            appDescriptorContent = _fileSystem.File.ReadAllText(appDescriptorPathOne);
            appDescriptor = JsonSerializer.Deserialize<AppDescriptorJson>(appDescriptorContent);
            appDescriptor.Packages.Count().Should().Be(2);
        }

    }
}
