using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command;
using Clio.Command.EntityConnections;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[NonParallelizable]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class EntityConnectionsToolTests {
	private const string TenantKey = "https://tenant-under-test.creatio.com|connections";

	private IEntityConnectionsService _service = null!;
	private IToolCommandResolver _commandResolver = null!;
	private EnvironmentOptions _resolverOptions;
	private EntityConnectionsReadRequest _capturedRead;
	private EntityConnectionsUpdateRequest _capturedUpdate;
	private GetEntityConnectionsTool _getTool = null!;
	private UpdateEntityConnectionsTool _updateTool = null!;

	[SetUp]
	public void SetUp() {
		ConsoleLogger.Instance.ClearMessages();
		_resolverOptions = null;
		_capturedRead = null;
		_capturedUpdate = null;
		_service = Substitute.For<IEntityConnectionsService>();
		_service.Get(Arg.Any<EntityConnectionsReadRequest>()).Returns(call => {
			_capturedRead = call.Arg<EntityConnectionsReadRequest>();
			return EntityConnectionsCommandTestData.State("Owner");
		});
		_service.Update(Arg.Any<EntityConnectionsUpdateRequest>()).Returns(call => {
			_capturedUpdate = call.Arg<EntityConnectionsUpdateRequest>();
			return new EntityConnectionsUpdateResult(EntityConnectionsCommandTestData.State("Account", "Owner"), true);
		});

		_commandResolver = Substitute.For<IToolCommandResolver>();
		_commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns(TenantKey);
		_commandResolver.Resolve<GetEntityConnectionsCommand>(Arg.Any<GetEntityConnectionsOptions>())
			.Returns(call => {
				_resolverOptions = call.Arg<GetEntityConnectionsOptions>();
				return new GetEntityConnectionsCommand(_service, ConsoleLogger.Instance);
			});
		_commandResolver.Resolve<UpdateEntityConnectionsCommand>(Arg.Any<UpdateEntityConnectionsOptions>())
			.Returns(call => {
				_resolverOptions = call.Arg<UpdateEntityConnectionsOptions>();
				return new UpdateEntityConnectionsCommand(_service, ConsoleLogger.Instance);
			});

		IEntityConnectionsService startupService = Substitute.For<IEntityConnectionsService>();
		startupService.Get(Arg.Any<EntityConnectionsReadRequest>())
			.Returns(_ => throw new InvalidOperationException("the startup default command must not be invoked"));
		startupService.Update(Arg.Any<EntityConnectionsUpdateRequest>())
			.Returns(_ => throw new InvalidOperationException("the startup default command must not be invoked"));
		_getTool = new GetEntityConnectionsTool(
			new GetEntityConnectionsCommand(startupService, ConsoleLogger.Instance), ConsoleLogger.Instance, _commandResolver);
		_updateTool = new UpdateEntityConnectionsTool(
			new UpdateEntityConnectionsCommand(startupService, ConsoleLogger.Instance), ConsoleLogger.Instance, _commandResolver);
	}

	[TearDown]
	public void TearDown() {
		ConsoleLogger.Instance.ClearMessages();
		_commandResolver.ClearReceivedCalls();
	}

	[Test]
	[Description("The tool names are the canonical CLI verbs.")]
	public void ToolNames_ShouldMatchCliVerbs() {
		// Assert
		GetEntityConnectionsTool.ToolName.Should().Be("get-entity-connections",
			because: "a CLI verb and its MCP tool share one name");
		UpdateEntityConnectionsTool.ToolName.Should().Be("update-entity-connections",
			because: "a CLI verb and its MCP tool share one name");
	}

	[Test]
	[Description("get-entity-connections resolves the command for the requested environment, maps every argument, and returns the read response.")]
	public void GetEntityConnections_ShouldResolveCommandAndMapArgs_WhenEnvironmentProvided() {
		// Act
		EntityConnectionsResponse response = _getTool.GetEntityConnections(new GetEntityConnectionsArgs("UsrOrder", "Custom", "dev"));

		// Assert
		response.Success.Should().BeTrue(because: "the resolved command reports success");
		response.Connections.Should().ContainSingle(connection => connection.ColumnName == "Owner",
			because: "the response comes from the environment-resolved command");
		_resolverOptions.Should().NotBeNull(because: "the environment-resolved command runs, not the startup default");
		_resolverOptions.Environment.Should().Be("dev", because: "environment-name maps onto the options");
		_capturedRead.Should().Be(new EntityConnectionsReadRequest("Custom", "UsrOrder"),
			because: "entity-schema-name and package-name reach the service");
	}

	[Test]
	[Description("get-entity-connections maps the direct-connection fallback arguments onto the options.")]
	public void GetEntityConnections_ShouldMapDirectConnectionArgs_WhenUriProvided() {
		// Act
		_getTool.GetEntityConnections(new GetEntityConnectionsArgs("UsrOrder", "Custom", null, "http://host", "u", "p"));

		// Assert
		_resolverOptions.Uri.Should().Be("http://host", because: "uri maps onto the options");
		_resolverOptions.Login.Should().Be("u", because: "login maps onto the options");
		_resolverOptions.Password.Should().Be("p", because: "password maps onto the options");
		_resolverOptions.Environment.Should().BeNull(because: "an omitted environment-name stays unset");
	}

	[TestCase(" ", "Custom", "entity-schema-name")]
	[TestCase("UsrOrder", "", "package-name")]
	[Description("get-entity-connections rejects a blank target in the structured response without resolving a command.")]
	public void GetEntityConnections_ShouldRejectBlankTarget_WithoutResolving(string entity, string package, string field) {
		// Act
		EntityConnectionsResponse response = _getTool.GetEntityConnections(new GetEntityConnectionsArgs(entity, package, "dev"));

		// Assert
		response.Success.Should().BeFalse(because: "a blank target fails validation");
		response.Error.Should().Contain(field, because: "the missing field is named");
		_commandResolver.DidNotReceive().Resolve<GetEntityConnectionsCommand>(Arg.Any<GetEntityConnectionsOptions>());
	}

	[Test]
	[Description("get-entity-connections refuses a misspelled argument instead of silently ignoring it.")]
	public void GetEntityConnections_ShouldRefuseUnknownArgument() {
		// Arrange
		GetEntityConnectionsArgs args = new("UsrOrder", "Custom", "dev") {
			ExtensionData = new Dictionary<string, JsonElement> { ["environment"] = JsonSerializer.SerializeToElement("dev") }
		};

		// Act
		EntityConnectionsResponse response = _getTool.GetEntityConnections(args);

		// Assert
		response.Success.Should().BeFalse(because: "an argument the tool does not bind must not be dropped silently");
		response.Error.Should().Contain("environment-name", because: "the alias is mapped to its canonical name");
		_commandResolver.DidNotReceive().Resolve<GetEntityConnectionsCommand>(Arg.Any<GetEntityConnectionsOptions>());
	}

	[Test]
	[Description("update-entity-connections passes the desired list to the service as given, in order, and returns changed=true.")]
	public void UpdateEntityConnections_ShouldPassListAsGiven_AndReportChanged() {
		// Act
		EntityConnectionsResponse response = _updateTool.UpdateEntityConnections(
			new UpdateEntityConnectionsArgs("UsrOrder", "Custom", ["Account", "Owner"], "dev"));

		// Assert
		response.Success.Should().BeTrue(because: "the resolved command reports success");
		response.Changed.Should().BeTrue(because: "the service result reports a write");
		_resolverOptions.Environment.Should().Be("dev", because: "environment-name maps onto the options");
		_capturedUpdate.PackageName.Should().Be("Custom", because: "package-name reaches the service");
		_capturedUpdate.EntitySchemaName.Should().Be("UsrOrder", because: "entity-schema-name reaches the service");
		_capturedUpdate.Connections.Should().Equal(["Account", "Owner"], because: "the desired order is kept as given");
	}

	[Test]
	[Description("update-entity-connections accepts an empty list, which removes every connection.")]
	public void UpdateEntityConnections_ShouldAcceptEmptyList() {
		// Act
		EntityConnectionsResponse response = _updateTool.UpdateEntityConnections(
			new UpdateEntityConnectionsArgs("UsrOrder", "Custom", [], "dev"));

		// Assert
		response.Success.Should().BeTrue(because: "an empty list is a valid desired state");
		_capturedUpdate.Connections.Should().BeEmpty(because: "the empty list reaches the service unchanged");
	}

	[Test]
	[Description("update-entity-connections rejects a missing connections list without resolving a command.")]
	public void UpdateEntityConnections_ShouldRejectMissingList_WithoutResolving() {
		// Act
		EntityConnectionsResponse response = _updateTool.UpdateEntityConnections(
			new UpdateEntityConnectionsArgs("UsrOrder", "Custom", null, "dev"));

		// Assert
		response.Success.Should().BeFalse(because: "the full desired list is required");
		response.Error.Should().Contain("connections", because: "the missing field is named");
		_commandResolver.DidNotReceive().Resolve<UpdateEntityConnectionsCommand>(Arg.Any<UpdateEntityConnectionsOptions>());
	}

	[Test]
	[Description("update-entity-connections refuses an unknown argument such as a convenience 'add' list.")]
	public void UpdateEntityConnections_ShouldRefuseUnknownArgument() {
		// Arrange
		UpdateEntityConnectionsArgs args = new("UsrOrder", "Custom", ["Owner"], "dev") {
			ExtensionData = new Dictionary<string, JsonElement> { ["add"] = JsonSerializer.SerializeToElement(new[] { "Account" }) }
		};

		// Act
		EntityConnectionsResponse response = _updateTool.UpdateEntityConnections(args);

		// Assert
		response.Success.Should().BeFalse(because: "a dropped 'add' would silently remove connections instead");
		response.Error.Should().Contain("'add'", because: "the unknown argument is named");
		_service.DidNotReceive().Update(Arg.Any<EntityConnectionsUpdateRequest>());
	}

	[Test]
	[Description("update-entity-connections reports a service refusal in the structured response.")]
	public void UpdateEntityConnections_ShouldReportServiceRefusal() {
		// Arrange
		_service.Update(Arg.Any<EntityConnectionsUpdateRequest>())
			.Returns(_ => throw new ArgumentException("Only lookup columns can be connections"));

		// Act
		EntityConnectionsResponse response = _updateTool.UpdateEntityConnections(
			new UpdateEntityConnectionsArgs("UsrOrder", "Custom", ["Name"], "dev"));

		// Assert
		response.Success.Should().BeFalse(because: "the refusal is a failure");
		response.Error.Should().Contain("lookup", because: "the service reason reaches the caller");
	}
}
