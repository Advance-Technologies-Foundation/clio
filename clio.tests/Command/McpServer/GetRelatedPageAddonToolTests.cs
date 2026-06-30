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
public class GetRelatedPageAddonToolTests {

	private static GetRelatedPageAddonArgs Args(
		string entitySchemaName = "UsrDeliveryItem",
		string packageName = "Custom",
		string environmentName = "dev") =>
		new(entitySchemaName, packageName, environmentName, null, null, null);

	[Test]
	[Description("Resolves the command for the requested environment and returns the resolved command's read response.")]
	public void GetRelatedPageAddon_ShouldResolveCommandAndReturnResponse_WhenEnvironmentProvided() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCommand resolved = new();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<GetRelatedPageAddonCommand>(Arg.Any<GetRelatedPageAddonOptions>()).Returns(resolved);
		GetRelatedPageAddonTool tool = new(new FakeCommand(), ConsoleLogger.Instance, resolver);

		// Act
		GetRelatedPageAddonResponse response = tool.GetRelatedPageAddon(Args());

		// Assert
		response.Success.Should().BeTrue(
			because: "the resolved command reports success");
		response.EntitySchemaName.Should().Be("UsrDeliveryItem",
			because: "the tool returns the resolved command's response built from the mapped options");
		resolved.Captured.Should().NotBeNull(
			because: "the environment-resolved command is invoked, not the startup default");
		resolved.Captured.EntitySchemaName.Should().Be("UsrDeliveryItem",
			because: "entity-schema-name maps onto the command options");
		resolved.Captured.PackageName.Should().Be("Custom",
			because: "package-name maps onto the command options");
		resolved.Captured.Environment.Should().Be("dev",
			because: "the requested environment-name is threaded onto the command options");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Rejects a blank entity-schema-name in the structured response without resolving a command.")]
	public void GetRelatedPageAddon_ShouldRejectMissingEntitySchemaName_WhenBlank() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		GetRelatedPageAddonTool tool = new(new FakeCommand(), ConsoleLogger.Instance, resolver);

		// Act
		GetRelatedPageAddonResponse response = tool.GetRelatedPageAddon(Args(entitySchemaName: " "));

		// Assert
		response.Success.Should().BeFalse(
			because: "a blank entity-schema-name is invalid input");
		response.Error.Should().Contain("entity-schema-name",
			because: "the error should name the missing field");
		// Input validation must short-circuit before any command resolution.
		resolver.DidNotReceiveWithAnyArgs().Resolve<GetRelatedPageAddonCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Rejects a blank package-name in the structured response without resolving a command.")]
	public void GetRelatedPageAddon_ShouldRejectMissingPackageName_WhenBlank() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		GetRelatedPageAddonTool tool = new(new FakeCommand(), ConsoleLogger.Instance, resolver);

		// Act
		GetRelatedPageAddonResponse response = tool.GetRelatedPageAddon(Args(packageName: " "));

		// Assert
		response.Success.Should().BeFalse(
			because: "a blank package-name is invalid input");
		response.Error.Should().Contain("package-name",
			because: "the error should name the missing field");
		// Input validation must short-circuit before any command resolution.
		resolver.DidNotReceiveWithAnyArgs().Resolve<GetRelatedPageAddonCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeCommand : GetRelatedPageAddonCommand {
		public GetRelatedPageAddonOptions Captured { get; private set; }

		public FakeCommand()
			: base(Substitute.For<IRelatedPageAddonService>(), ConsoleLogger.Instance) {
		}

		public override bool TryGet(GetRelatedPageAddonOptions options, out GetRelatedPageAddonResponse response) {
			Captured = options;
			response = new GetRelatedPageAddonResponse {
				Success = true,
				EntitySchemaName = options.EntitySchemaName,
				PackageName = options.PackageName,
				PageCount = 0
			};
			return true;
		}
	}
}
