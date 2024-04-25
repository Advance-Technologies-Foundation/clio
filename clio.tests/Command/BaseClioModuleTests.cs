using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using ATF.Repository.Mock;
using Autofac;
using Clio.Command;
using Clio.Common;
using Clio.Tests.Infrastructure;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using YamlDotNet.Serialization;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Tests.Command
{

    [TestFixture]
    public abstract class BaseClioModuleTests
    {

        #region Setup/Teardown

        [SetUp]
        public virtual void Setup() {
            _fileSystem = CreateFs();
            BindingsModule bindingModule = new(_fileSystem);
            _container = bindingModule.Register(_environmentSettings, true);
        }

        #endregion

        #region Fields: Private

        protected IFileSystem _fileSystem;
        protected IContainer _container;
        protected EnvironmentSettings _environmentSettings = new EnvironmentSettings();

        #endregion

        protected virtual IFileSystem CreateFs() {
            return TestFileSystem.MockFileSystem();
        }

    }
}