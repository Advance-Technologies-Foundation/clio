using System;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Command;
using Clio.Common;
using Clio.CreatioModel;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class CompileConfigurationCommandTestCase : BaseCommandTests<CompileConfigurationOptions>
{
	private readonly IServiceUrlBuilder _serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
	private readonly IDataProvider _dataProvider = Substitute.For<IDataProvider>();
	private readonly IApplicationClient _applicationClient = Substitute.For<IApplicationClient>();

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton(_serviceUrlBuilder);
		containerBuilder.AddSingleton(_dataProvider);
		containerBuilder.AddSingleton(_applicationClient);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_serviceUrlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>())
			.Returns("http://test/ServiceModel/CompilationService.svc/Compile");
	}

	[TearDown]
	public override void TearDown() {
		_serviceUrlBuilder.ClearReceivedCalls();
		_dataProvider.ClearReceivedCalls();
		_applicationClient.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Verifies that the command completes successfully without ObjectDisposedException when background thread is monitoring compilation history")]
	public void Execute_CompletesWithoutObjectDisposedException_WhenBackgroundThreadIsRunning() {
		// Arrange
		CompileConfigurationCommand command = Container.GetRequiredService<CompileConfigurationCommand>();
		CompileConfigurationOptions options = new() {
			All = false
		};

		// Setup successful response - this ensures Execute completes quickly
		string successResponse = "{\"success\":true,\"buildResult\":0,\"errorInfo\":{\"errorCode\":null,\"message\":null}}";
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
			.Returns(successResponse);

		// Act & Assert
		// The fix ensures thread.Join() is called before CancellationTokenSource is disposed
		// Without the fix, this would throw ObjectDisposedException
		Action act = () => command.Execute(options);
		
		act.Should().NotThrow<ObjectDisposedException>(
			because: "the background thread should complete via Join() before CancellationTokenSource is disposed");
	}
}
