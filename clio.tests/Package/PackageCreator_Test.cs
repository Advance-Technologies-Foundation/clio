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
    }
}
