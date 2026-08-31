using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Tests.Command;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
public sealed class CreatioHostServiceTests : BaseClioModuleTests {
	private IProcessExecutor _processExecutor;
	private ICreatioHostEnvironmentStore _environmentStore;
	private ICreatioHostService _sut;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		_processExecutor = Substitute.For<IProcessExecutor>();
		_environmentStore = Substitute.For<ICreatioHostEnvironmentStore>();
		containerBuilder.AddSingleton(_processExecutor);
		containerBuilder.AddSingleton(_environmentStore);
	}

	[SetUp]
	public void SetUp() {
		_sut = Container.GetRequiredService<ICreatioHostService>();
	}

	[TearDown]
	public override void TearDown() {
		_processExecutor.ClearReceivedCalls();
		_environmentStore.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Passes sensitive dotnet host configuration through the child process environment without adding it to command arguments.")]
	public async Task StartInBackground_ShouldPassEnvironmentVariablesToChildHost() {
		// Arrange
		_processExecutor.FireAndForgetAsync(Arg.Any<ProcessExecutionOptions>()).Returns(
			Task.FromResult(new ProcessLaunchResult { Started = true, ProcessId = 42 }));
		IReadOnlyDictionary<string, string> environmentVariables = new Dictionary<string, string> {
			["Kestrel__Endpoints__Https__Certificate__Password"] = "secret"
		};

		// Act
		int? processId = _sut.StartInBackground("/tmp/creatio", environmentVariables);

		// Assert
		processId.Should().Be(42,
			because: "the host process identifier must be returned after a successful detached launch");
		await _processExecutor.Received(1).FireAndForgetAsync(Arg.Is<ProcessExecutionOptions>(options =>
			options.Arguments == "Terrasoft.WebHost.dll"
			&& options.EnvironmentVariables["Kestrel__Endpoints__Https__Certificate__Password"] == "secret"
			&& !options.Arguments.Contains("secret")
			&& options.ClearInheritedEnvironment
			&& options.InheritedEnvironmentVariableAllowlist.Contains("PATH")
			&& !options.InheritedEnvironmentVariableAllowlist.Contains("Kestrel__Endpoints__Https__Certificate__Password")));
	}

	[Test]
	[Description("Restores persisted dotnet host environment values when a later background start omits explicit values.")]
	public async Task StartInBackground_ShouldLoadPersistedEnvironmentVariables_WhenNotSupplied() {
		// Arrange
		_processExecutor.FireAndForgetAsync(Arg.Any<ProcessExecutionOptions>()).Returns(
			Task.FromResult(new ProcessLaunchResult { Started = true, ProcessId = 43 }));
		IReadOnlyDictionary<string, string> persistedEnvironment = new Dictionary<string, string> {
			["Kestrel__Endpoints__Https__Certificate__Password"] = "persisted-secret"
		};
		_environmentStore.Load("/tmp/creatio").Returns(persistedEnvironment);

		// Act
		int? processId = _sut.StartInBackground("/tmp/creatio");

		// Assert
		processId.Should().Be(43,
			because: "a later start must return the detached host process identifier after restoring its saved environment");
		_environmentStore.Received(1).Load("/tmp/creatio");
		await _processExecutor.Received(1).FireAndForgetAsync(Arg.Is<ProcessExecutionOptions>(options =>
			options.EnvironmentVariables["Kestrel__Endpoints__Https__Certificate__Password"] == "persisted-secret"));
	}

	[Test]
	[Description("Delegates deployment host environment persistence to the protected environment store.")]
	public void PersistEnvironmentVariables_ShouldDelegateToStore() {
		// Arrange
		IReadOnlyDictionary<string, string> environmentVariables = new Dictionary<string, string> {
			["Kestrel__Endpoints__Https__Certificate__Password"] = "secret"
		};

		// Act
		_sut.PersistEnvironmentVariables("/tmp/creatio", environmentVariables);

		// Assert
		_environmentStore.Received(1).Save("/tmp/creatio", environmentVariables);
	}
}
