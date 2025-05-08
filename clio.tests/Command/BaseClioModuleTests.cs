using System.IO.Abstractions.TestingHelpers;
using Autofac;
using Clio.Tests.Infrastructure;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture(Category = "UnitTests")]
public abstract class BaseClioModuleTests
{
    [SetUp]
    public virtual void Setup()
    {
        fileSystem = CreateFs();

        BindingsModule bindingModule = new(fileSystem);
        container = bindingModule.Register(environmentSettings, AdditionalRegistrations);
    }

    protected MockFileSystem fileSystem;
    protected IContainer container;

    protected EnvironmentSettings environmentSettings =
        new() { Uri = "http://localhost", Login = string.Empty, Password = string.Empty };

    protected virtual MockFileSystem CreateFs() => TestFileSystem.MockFileSystem();

    protected virtual void AdditionalRegistrations(ContainerBuilder containerBuilder)
    {
    }
}
