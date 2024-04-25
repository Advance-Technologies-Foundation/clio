using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using Clio.Tests.Command;
using Clio.Tests.Infrastructure;
using Clio.Workspaces;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package
{
    internal class PackageCreator_Test : BaseClioModuleTests
    {


        [Test]
        public void Create_With() {
            PackageCreator creator = new PackageCreator(_container.Resolve<EnvironmentSettings>(), _container.Resolve<IWorkspace>(), _container.Resolve<IWorkspaceSolutionCreator>(),
                _container.Resolve<ITemplateProvider>(), _container.Resolve<IWorkspacePathBuilder>(),
                _container.Resolve<IStandalonePackageFileManager>(), _container.Resolve<IJsonConverter>(),
                _container.Resolve<IWorkingDirectoriesProvider>(), _container.Resolve<IFileSystem>());
            Assert.IsNotNull(creator);
        }

    }
}
