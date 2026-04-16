using System;
using System.IO.Abstractions.TestingHelpers;
using Clio.Tests.Infrastructure;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
public abstract class BaseClioModuleTests {
	#region Fields: Private

	private BindingsModule _bindingsModule;

	#endregion

	#region Fields: Protected

	protected IServiceProvider Container;

	protected EnvironmentSettings EnvironmentSettings = new() {
		Uri = "http://localhost",
		Login = "",
		Password = ""
	};

	protected MockFileSystem FileSystem;

	#endregion

	#region Methods: Protected

	protected virtual void AdditionalRegistrations(IServiceCollection containerBuilder) { }

	protected virtual MockFileSystem CreateFs() {
		return TestFileSystem.MockFileSystem();
	}

	#endregion

	#region Methods: Public

	[SetUp]
	public virtual void Setup() {
		FileSystem = CreateFs();
		_bindingsModule = new BindingsModule(FileSystem);
		Container = _bindingsModule.Register(EnvironmentSettings, AdditionalRegistrations);
	}


	[TearDown]
	public virtual void TearDown() {
	}

	#endregion
}
