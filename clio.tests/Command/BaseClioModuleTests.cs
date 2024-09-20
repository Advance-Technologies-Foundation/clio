using Autofac;
using Clio.Tests.Infrastructure;
using NUnit.Framework;
using System.IO.Abstractions.TestingHelpers;
using Terrasoft.Core.Configuration;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Tests.Command;

[TestFixture(Category = "UnitTests")]
public abstract class BaseClioModuleTests
{

	#region Setup/Teardown

	[SetUp]
	public virtual void Setup(){
		FileSystem = CreateFs();
		
		BindingsModule bindingModule = new(FileSystem);
		Container = bindingModule.Register(EnvironmentSettings, true, AdditionalRegistrations);
	}

	#endregion

	#region Fields: Protected

	protected MockFileSystem FileSystem;
	protected IContainer Container;
	protected EnvironmentSettings EnvironmentSettings = new() {
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