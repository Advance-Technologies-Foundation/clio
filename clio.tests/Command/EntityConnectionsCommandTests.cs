namespace Clio.Tests.Command;

using System;
using System.Collections.Generic;
using System.Net.Http;
using Clio.Command;
using Clio.Command.EntityConnections;
using Clio.Common;
using CommandLine;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

internal static class EntityConnectionsCommandTestData {
	internal static EntityConnectionsReadResult State(params string[] names) => new(
		"UsrOrder", "bb000000-0000-0000-0000-000000000002", "Custom",
		[.. Array.ConvertAll(names, name => new EntityConnectionEntry(
			name, Guid.NewGuid().ToString("D"), name + " caption", "Contact", Array.IndexOf(names, name)))],
		new EntityConnectionsPackageLayer(["Owner"], ["Account"]),
		[]);
}

[TestFixture]
[Property("Module", "Command")]
public sealed class GetEntityConnectionsCommandTests : BaseCommandTests<GetEntityConnectionsOptions> {
	private IEntityConnectionsService _service = null!;
	private ILogger _logger = null!;
	private GetEntityConnectionsCommand _command = null!;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_service = Substitute.For<IEntityConnectionsService>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_service);
		containerBuilder.AddSingleton(_logger);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<GetEntityConnectionsCommand>();
	}

	[TearDown]
	public override void TearDown() {
		_service.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	private static GetEntityConnectionsOptions Options() => new() { EntitySchemaName = "UsrOrder", PackageName = "Custom" };

	[Test]
	[Description("TryGet maps the service result into the response and passes the target through.")]
	public void TryGet_ShouldMapResult_WhenServiceSucceeds() {
		// Arrange
		_service.Get(Arg.Any<EntityConnectionsReadRequest>()).Returns(EntityConnectionsCommandTestData.State("Owner", "Account"));

		// Act
		bool ok = _command.TryGet(Options(), out EntityConnectionsResponse response);

		// Assert
		ok.Should().BeTrue(because: "a successful read returns true");
		response.Success.Should().BeTrue(because: "the response reflects the successful read");
		response.Changed.Should().BeNull(because: "a read reports no changed flag");
		response.Connections.Should().HaveCount(2, because: "every effective connection is mapped");
		response.Connections[1].ColumnName.Should().Be("Account", because: "the order of the service result is kept");
		response.Connections[1].Position.Should().Be(1, because: "the position is mapped");
		response.PackageLayer.Items.Should().Equal(["Owner"], because: "the add-on items are mapped");
		response.PackageLayer.Removed.Should().Equal(["Account"], because: "the add-on removals are mapped");
		response.Warnings.Should().BeNull(because: "an empty warning list is omitted from the envelope");
		_service.Received(1).Get(Arg.Is<EntityConnectionsReadRequest>(request =>
			request.EntitySchemaName == "UsrOrder" && request.PackageName == "Custom"));
	}

	[Test]
	[Description("Execute returns 0 and logs the serialized response when the service succeeds.")]
	public void Execute_ShouldReturnZero_WhenServiceSucceeds() {
		// Arrange
		_service.Get(Arg.Any<EntityConnectionsReadRequest>()).Returns(EntityConnectionsCommandTestData.State("Owner"));

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(0, because: "a successful read maps to exit code 0");
		_logger.Received().WriteInfo(Arg.Is<string>(message => message.Contains("\"columnName\":\"Owner\"")));
	}

	[Test]
	[Description("Execute returns 1 and reports the error when the service fails.")]
	public void Execute_ShouldReturnOne_WhenServiceFails() {
		// Arrange
		_service.Get(Arg.Any<EntityConnectionsReadRequest>())
			.Returns(_ => throw new HttpRequestException("boom"));

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(1, because: "a failed read maps to exit code 1");
		_logger.Received().WriteInfo(Arg.Is<string>(message => message.Contains("boom")));
	}
}

[TestFixture]
[Property("Module", "Command")]
public sealed class UpdateEntityConnectionsCommandTests : BaseCommandTests<UpdateEntityConnectionsOptions> {
	private IEntityConnectionsService _service = null!;
	private ILogger _logger = null!;
	private UpdateEntityConnectionsCommand _command = null!;
	private EntityConnectionsUpdateRequest _captured;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_service = Substitute.For<IEntityConnectionsService>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_service);
		containerBuilder.AddSingleton(_logger);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_captured = null;
		_service.Update(Arg.Any<EntityConnectionsUpdateRequest>()).Returns(call => {
			_captured = call.Arg<EntityConnectionsUpdateRequest>();
			return new EntityConnectionsUpdateResult(EntityConnectionsCommandTestData.State("Owner"), true);
		});
		_command = Container.GetRequiredService<UpdateEntityConnectionsCommand>();
	}

	[TearDown]
	public override void TearDown() {
		_service.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[TestCase("Owner, Account ,Contact", new[] { "Owner", "Account", "Contact" })]
	[TestCase("", new string[0])]
	[Description("Execute splits the comma-separated --connections value into the ordered column list; a blank value removes every connection.")]
	public void Execute_ShouldSplitConnections_IntoOrderedList(string value, string[] expected) {
		// Arrange
		UpdateEntityConnectionsOptions options = new() {
			EntitySchemaName = "UsrOrder", PackageName = "Custom", Connections = value
		};

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the service accepted the update");
		_captured.Connections.Should().Equal(expected, because: "names are trimmed and keep their order");
		_logger.Received().WriteInfo(Arg.Is<string>(message => message.Contains("\"changed\":true")));
	}

	[Test]
	[Description("The CLI parser accepts an empty --connections value, so every connection can be removed from the command line.")]
	public void Parse_ShouldAcceptEmptyConnectionsValue() {
		// Act
		ParserResult<UpdateEntityConnectionsOptions> result = new Parser().ParseArguments<UpdateEntityConnectionsOptions>(
			["--entity-schema-name", "UsrOrder", "--package-name", "Custom", "--connections", ""]);

		// Assert
		result.Tag.Should().Be(ParserResultType.Parsed, because: "an empty list is a valid desired state");
		((Parsed<UpdateEntityConnectionsOptions>)result).Value.ParseConnections().Should().BeEmpty(
			because: "a blank value means no connections");
	}

	[Test]
	[Description("TryUpdate reports a refused update in the envelope and returns false.")]
	public void TryUpdate_ShouldReportError_WhenServiceRefuses() {
		// Arrange
		_service.Update(Arg.Any<EntityConnectionsUpdateRequest>())
			.Returns(_ => throw new ArgumentException("not a lookup"));

		// Act
		bool ok = _command.TryUpdate(
			new UpdateEntityConnectionsOptions { EntitySchemaName = "UsrOrder", PackageName = "Custom" },
			new List<string> { "Name" },
			out EntityConnectionsResponse response);

		// Assert
		ok.Should().BeFalse(because: "a refused update is a failure");
		response.Success.Should().BeFalse(because: "the envelope reports the failure");
		response.Error.Should().Be("not a lookup", because: "the service reason reaches the caller verbatim");
	}
}
