using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class GenerateSourceCodeCommandTestCase : BaseCommandTests<GenerateSourceCodeOptions>
{

	private readonly IServiceUrlBuilder _serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
	private readonly IApplicationClient _applicationClient = Substitute.For<IApplicationClient>();

	private const string SuccessResponse = "{\"success\":true,\"errorInfo\":{\"errorCode\":null,\"message\":null}}";
	private const string FailureResponse = "{\"success\":false,\"errorInfo\":{\"errorCode\":\"500\",\"message\":\"Internal server error\"}}";

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton(_serviceUrlBuilder);
		containerBuilder.AddSingleton(_applicationClient);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_serviceUrlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>())
			.Returns("http://test/ServiceModel/WorkspaceExplorerService.svc/GenerateAllSchemasSources");
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(SuccessResponse);
	}

	[TearDown]
	public override void TearDown() {
		_serviceUrlBuilder.ClearReceivedCalls();
		_applicationClient.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Verifies that the command calls GenerateAllSchemasSources by default")]
	public void Execute_CallsGenerateAllSchemasSources_WhenNoFlagsProvided() {
		GenerateSourceCodeCommand command = Container.GetRequiredService<GenerateSourceCodeCommand>();
		GenerateSourceCodeOptions options = new() { Modified = false, Background = false };

		int result = command.Execute(options);

		result.Should().Be(0);
		_serviceUrlBuilder.Received().Build(ServiceUrlBuilder.KnownRoute.GenerateAllSchemasSources);
	}

	[Test]
	[Description("Verifies that the command calls GenerateModifiedSchemasSources when --modified flag is set")]
	public void Execute_CallsGenerateModifiedSchemasSources_WhenModifiedFlagIsSet() {
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GenerateModifiedSchemasSources)
			.Returns("http://test/ServiceModel/WorkspaceExplorerService.svc/GenerateModifiedSchemasSources");
		GenerateSourceCodeCommand command = Container.GetRequiredService<GenerateSourceCodeCommand>();
		GenerateSourceCodeOptions options = new() { Modified = true, Background = false };

		int result = command.Execute(options);

		result.Should().Be(0);
		_serviceUrlBuilder.Received().Build(ServiceUrlBuilder.KnownRoute.GenerateModifiedSchemasSources);
	}

	[Test]
	[Description("Verifies that the command calls GenerateAllSchemasSourcesInBackground when --background flag is set")]
	public void Execute_CallsGenerateAllSchemasSourcesInBackground_WhenBackgroundFlagIsSet() {
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GenerateAllSchemasSourcesInBackground)
			.Returns("http://test/ServiceModel/WorkspaceExplorerService.svc/GenerateAllSchemasSourcesInBackground");
		GenerateSourceCodeCommand command = Container.GetRequiredService<GenerateSourceCodeCommand>();
		GenerateSourceCodeOptions options = new() { Modified = false, Background = true };

		int result = command.Execute(options);

		result.Should().Be(0);
		_serviceUrlBuilder.Received().Build(ServiceUrlBuilder.KnownRoute.GenerateAllSchemasSourcesInBackground);
	}

	[Test]
	[Description("Verifies that the command calls GenerateRequiredSchemasSources when --required flag is set")]
	public void Execute_CallsGenerateRequiredSchemasSources_WhenRequiredFlagIsSet() {
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GenerateRequiredSchemasSources)
			.Returns("http://test/ServiceModel/WorkspaceExplorerService.svc/GenerateRequiredSchemasSources");
		GenerateSourceCodeCommand command = Container.GetRequiredService<GenerateSourceCodeCommand>();
		GenerateSourceCodeOptions options = new() { Required = true };

		int result = command.Execute(options);

		result.Should().Be(0);
		_serviceUrlBuilder.Received().Build(ServiceUrlBuilder.KnownRoute.GenerateRequiredSchemasSources);
	}

	[Test]
	[Description("Verifies that the command returns 1 when server reports failure")]
	public void Execute_ReturnsOne_WhenServerReportsFailure() {
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(FailureResponse);
		GenerateSourceCodeCommand command = Container.GetRequiredService<GenerateSourceCodeCommand>();
		GenerateSourceCodeOptions options = new() { Modified = false, Background = false };

		int result = command.Execute(options);

		result.Should().Be(1);
	}

}
