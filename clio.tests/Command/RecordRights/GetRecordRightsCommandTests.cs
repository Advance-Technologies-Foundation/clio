using System.Collections.Generic;
using Clio.Command.RecordRights;
using Clio.Common;
using Clio.Common.RecordRights;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.RecordRights;

[TestFixture]
[Property("Module", "Command")]
public class GetRecordRightsCommandTests : BaseCommandTests<GetRecordRightsOptions> {

	private GetRecordRightsCommand _command;
	private ICreatioRightsClient _rightsClient;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		// Resolve the SUT from the container so the real BindingsModule wiring is exercised.
		_command = Container.GetRequiredService<GetRecordRightsCommand>();
	}

	public override void TearDown() {
		_rightsClient.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_rightsClient = Substitute.For<ICreatioRightsClient>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient(_ => _rightsClient);
		containerBuilder.AddTransient(_ => _logger);
	}

	[Test]
	[Description("Reads rights via the convention-resolved table name and prints each grant when the record has rights.")]
	public void Execute_ShouldReadAndPrintRights_WhenEntityTargetGiven() {
		// Arrange
		_rightsClient.GetRecordRights("SysContactRight", "rec-1", Arg.Any<CreatioRequestOptions>())
			.Returns(new List<RecordRightRow> {
				new() {
					Id = "row-1", Operation = 1, RightLevel = 2,
					SysAdminUnit = new SysAdminUnitRef { Value = "g-1", DisplayValue = "Everyone" }
				}
			});
		GetRecordRightsOptions options = new() { Entity = "Contact", RecordId = "rec-1" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a successful read returns exit code 0");
		_rightsClient.Received(1).GetRecordRights("SysContactRight", "rec-1", Arg.Any<CreatioRequestOptions>());
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("edit") && m.Contains("delegated") && m.Contains("Everyone")));
	}

	[Test]
	[Description("Reports no rights and returns success when the record has none.")]
	public void Execute_ShouldReportNoRights_WhenRecordHasNone() {
		// Arrange
		_rightsClient.GetRecordRights("SysContactRight", "rec-1", Arg.Any<CreatioRequestOptions>())
			.Returns(new List<RecordRightRow>());
		GetRecordRightsOptions options = new() { Entity = "Contact", RecordId = "rec-1" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "an empty rights set is a successful read");
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("No record rights")));
	}

	[TestCase("Contact", "SysContactRight")]
	[TestCase("SysSchemaAdminUnit", "SysSchemaAdminUnitRight")]
	[Description("Derives the rights-table name via the Sys<Entity>Right convention and passes it to the client: business entities are Sys-prefixed, Sys* entities are not double-prefixed.")]
	public void Execute_ShouldQueryConventionTableName_WhenEntityGiven(string entity, string expectedTable) {
		// Arrange
		_rightsClient.GetRecordRights(expectedTable, "rec-1", Arg.Any<CreatioRequestOptions>())
			.Returns(new List<RecordRightRow>());
		GetRecordRightsOptions options = new() { Entity = entity, RecordId = "rec-1" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a successful read returns exit code 0");
		_rightsClient.Received(1).GetRecordRights(expectedTable, "rec-1", Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("Returns a friendly error and exit code 1 when the rights client throws.")]
	public void Execute_ShouldReturnError_WhenRightsClientThrows() {
		// Arrange
		_rightsClient.GetRecordRights("SysContactRight", "rec-1", Arg.Any<CreatioRequestOptions>())
			.Returns(_ => throw new System.InvalidOperationException("boom"));
		GetRecordRightsOptions options = new() { Entity = "Contact", RecordId = "rec-1" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a service failure is surfaced as a non-zero exit code");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("boom")));
	}
}
