using Autofac;
using Clio.Tests.Infrastructure;
using NUnit.Framework;
using System.IO.Abstractions.TestingHelpers;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Tests.Command;

[TestFixture]
public abstract class BaseClioModuleTests
{

	#region Setup/Teardown

	[SetUp]
	public virtual void Setup(){
		_fileSystem = CreateFs();
		BindingsModule bindingModule = new(_fileSystem);
		_container = bindingModule.Register(_environmentSettings, true, AdditionalRegistrations);
	}

	#endregion

	#region Fields: Protected

	protected MockFileSystem _fileSystem;
	protected IContainer _container;
	protected EnvironmentSettings _environmentSettings = new() {
		Uri = "http://localhost",
		Login = "",
		Password = ""
	};

	#endregion

	#region Methods: Protected

	protected virtual MockFileSystem CreateFs(){
		return TestFileSystem.MockFileSystem();
	}

	protected virtual void AdditionalRegistrations(ContainerBuilder containerBuilder) {
	}

	#endregion

}