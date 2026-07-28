using System;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.RelatedPages;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[NonParallelizable]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class GetRelatedPageAddonToolTests {
	private IRelatedPageAddonService _service = null!;
	private GetRelatedPageAddonOptions _resolverOptions;
	private IToolCommandResolver _commandResolver = null!;
	private GetRelatedPageAddonTool _tool = null!;

	[SetUp]
	public void SetUp() {
		ConsoleLogger.Instance.ClearMessages();
		_resolverOptions = null;

		// The environment-resolved command runs for real over a mocked service (no command subclass — sealed).
		_service = Substitute.For<IRelatedPageAddonService>();
		_service.Get(Arg.Any<RelatedPageAddonReadRequest>()).Returns(new RelatedPageAddonReadResult(
			"UsrDeliveryItem", "bb000000-0000-0000-0000-000000000002", "Custom",
			"aa000000-0000-0000-0000-000000000001", "RelatedPage", null, 1,
			new[] {
				new RelatedPageEntry("cc000000-0000-0000-0000-00000000000a", "UsrDeliveryItemFormPage",
					true, false, false, null, null, null)
			}));
		GetRelatedPageAddonCommand resolvedCommand = new(_service, ConsoleLogger.Instance);

		_commandResolver = Substitute.For<IToolCommandResolver>();
		_commandResolver.Resolve<GetRelatedPageAddonCommand>(Arg.Any<GetRelatedPageAddonOptions>())
			.Returns(call => {
				_resolverOptions = call.Arg<GetRelatedPageAddonOptions>();
				return resolvedCommand;
			});

		IRelatedPageAddonService defaultService = Substitute.For<IRelatedPageAddonService>();
		defaultService.Get(Arg.Any<RelatedPageAddonReadRequest>())
			.Returns(_ => throw new InvalidOperationException("the startup default command must not be invoked"));
		GetRelatedPageAddonCommand defaultCommand = new(defaultService, ConsoleLogger.Instance);

		_tool = new GetRelatedPageAddonTool(defaultCommand, ConsoleLogger.Instance, _commandResolver);
	}

	[TearDown]
	public void TearDown() => ConsoleLogger.Instance.ClearMessages();

	private static GetRelatedPageAddonArgs Args(
		string entitySchemaName = "UsrDeliveryItem",
		string packageName = "Custom",
		string environmentName = "dev") =>
		new(entitySchemaName, packageName, environmentName, null, null, null);

	[Test]
	[Description("Resolves the command for the requested environment, maps the args onto the options, and returns the resolved command's read response.")]
	public void GetRelatedPageAddon_ShouldResolveCommandAndReturnResponse_WhenEnvironmentProvided() {
		// Act
		GetRelatedPageAddonResponse response = _tool.GetRelatedPageAddon(Args());

		// Assert
		response.Success.Should().BeTrue(
			because: "the resolved command reports success");
		response.EntitySchemaName.Should().Be("UsrDeliveryItem",
			because: "the tool returns the resolved command's response built from the service read result");
		response.PageCount.Should().Be(1,
			because: "the decoded page count flows through the resolved command's response");

		_resolverOptions.Should().NotBeNull(
			because: "the environment-resolved command is invoked, not the startup default");
		_resolverOptions.EntitySchemaName.Should().Be("UsrDeliveryItem",
			because: "entity-schema-name maps onto the command options");
		_resolverOptions.PackageName.Should().Be("Custom",
			because: "package-name maps onto the command options");
		_resolverOptions.Environment.Should().Be("dev",
			because: "the requested environment-name is threaded onto the command options");
	}

	[Test]
	[Description("Rejects a blank entity-schema-name in the structured response without resolving a command.")]
	public void GetRelatedPageAddon_ShouldRejectMissingEntitySchemaName_WhenBlank() {
		// Act
		GetRelatedPageAddonResponse response = _tool.GetRelatedPageAddon(Args(entitySchemaName: " "));

		// Assert
		response.Success.Should().BeFalse(
			because: "a blank entity-schema-name is invalid input");
		response.Error.Should().Contain("entity-schema-name",
			because: "the error should name the missing field");
		_commandResolver.DidNotReceiveWithAnyArgs().Resolve<GetRelatedPageAddonCommand>(default!);
	}

	[Test]
	[Description("Rejects a blank package-name in the structured response without resolving a command.")]
	public void GetRelatedPageAddon_ShouldRejectMissingPackageName_WhenBlank() {
		// Act
		GetRelatedPageAddonResponse response = _tool.GetRelatedPageAddon(Args(packageName: " "));

		// Assert
		response.Success.Should().BeFalse(
			because: "a blank package-name is invalid input");
		response.Error.Should().Contain("package-name",
			because: "the error should name the missing field");
		_commandResolver.DidNotReceiveWithAnyArgs().Resolve<GetRelatedPageAddonCommand>(default!);
	}
}
