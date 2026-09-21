using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.ObjectRights;
using Clio.Common;
using Clio.Common.ObjectRights;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ObjectRights;

[TestFixture]
[Property("Module", "Command")]
public class GetObjectRightsCommandTests : BaseCommandTests<GetObjectRightsOptions> {

	private GetObjectRightsCommand _command;
	private IObjectRightsReader _rightsReader;
	private IRemoteEntitySchemaColumnManager _columnManager;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<GetObjectRightsCommand>();
	}

	public override void TearDown() {
		_rightsReader.ClearReceivedCalls();
		_columnManager.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_rightsReader = Substitute.For<IObjectRightsReader>();
		_columnManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient(_ => _rightsReader);
		containerBuilder.AddTransient(_ => _columnManager);
		containerBuilder.AddTransient(_ => _logger);
	}

	// Builds a merged-schema snapshot exposing one lookup column per referenced schema name.
	private static EntitySchemaPropertiesInfo SchemaWithLookups(string schemaName, params string[] referenceSchemaNames) {
		List<EntitySchemaPropertyColumnInfo> columns = new();
		foreach (string reference in referenceSchemaNames) {
			columns.Add(new EntitySchemaPropertyColumnInfo(
				Name: reference + "Col", UId: Guid.NewGuid(), Source: "own", Title: null, Description: null,
				Type: "Lookup", Required: false, Indexed: false, ReferenceSchemaName: reference));
		}
		return new EntitySchemaPropertiesInfo(
			Name: schemaName, Title: null, Description: null, PackageName: schemaName, ParentSchemaName: null,
			ExtendParent: false, PrimaryColumnName: null, PrimaryDisplayColumnName: null, OwnColumnCount: 0,
			InheritedColumnCount: 0, IndexesCount: null, TrackChangesInDb: false, DbView: false, SspAvailable: null,
			Virtual: false, UseRecordDeactivation: null, ShowInAdvancedMode: false, AdministratedByOperations: false,
			AdministratedByColumns: false, AdministratedByRecords: false, UseDenyRecordRights: null,
			UseLiveEditing: null, Columns: columns);
	}

	[Test]
	[Description("Returns 0 and reports no grant needed when the root and every connected lookup already grant read/create/edit to All external users.")]
	public void Execute_ShouldReportNoGrantNeeded_WhenAllObjectsGranted() {
		// Arrange
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>())
			.Returns(SchemaWithLookups("UsrPortalSpike2", "UsrPSCategory"));
		_rightsReader.GetObjectRights("UsrPortalSpike2", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrPortalSpike2", "Portal Spike 2", true,
				new ExternalUsersOperationRights(true, true, true, false)));
		_rightsReader.GetObjectRights("UsrPSCategory", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrPSCategory", "PS Category", true,
				new ExternalUsersOperationRights(true, true, true, false)));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrPortalSpike2" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a completed read returns exit code 0");
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("No grant needed")));
		_logger.DidNotReceive().WriteWarning(Arg.Is<string>(m => m.Contains("needing a grant")));
	}

	[Test]
	[Description("Lists a connected object that lacks external access and points at set-object-rights.")]
	public void Execute_ShouldListMissingObject_WhenConnectedLookupNotGranted() {
		// Arrange
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>())
			.Returns(SchemaWithLookups("UsrPortalSpike2", "UsrPSCategory"));
		_rightsReader.GetObjectRights("UsrPortalSpike2", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrPortalSpike2", "Portal Spike 2", true,
				new ExternalUsersOperationRights(true, true, true, false)));
		_rightsReader.GetObjectRights("UsrPSCategory", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrPSCategory", "PS Category", true, null));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrPortalSpike2" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a read that finds gaps still completes with exit code 0");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("needing a grant") && m.Contains("UsrPSCategory")));
	}

	[Test]
	[Description("Reports an object that is not administered by operation permissions as available to all.")]
	public void Execute_ShouldReportAvailable_WhenNotAdministratedByOperations() {
		// Arrange
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>())
			.Returns(SchemaWithLookups("UsrOpen"));
		_rightsReader.GetObjectRights("UsrOpen", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrOpen", "Open", false, null));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrOpen" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the read completed");
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("available to all")));
		_logger.Received().WriteInfo(Arg.Is<string>(m => m.Contains("No grant needed")));
	}

	[Test]
	[Description("Returns a friendly error and reads nothing when --entity-schema-name is empty.")]
	public void Execute_ShouldReturnError_WhenEntitySchemaNameMissing() {
		// Arrange
		GetObjectRightsOptions options = new() { EntitySchemaName = "  " };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a missing entity schema name is an input error");
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("entity-schema-name")));
		_rightsReader.DidNotReceive().GetObjectRights(Arg.Any<string>(), Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("Checks the root only (with a warning) when connected-object enumeration fails.")]
	public void Execute_ShouldCheckRootOnly_WhenColumnEnumerationThrows() {
		// Arrange
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>())
			.Returns(_ => throw new InvalidOperationException("schema read failed"));
		_rightsReader.GetObjectRights("UsrPortalSpike2", Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrPortalSpike2", "Portal Spike 2", true,
				new ExternalUsersOperationRights(true, true, true, false)));
		GetObjectRightsOptions options = new() { EntitySchemaName = "UsrPortalSpike2" };

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "root-only fallback still completes the read");
		_logger.Received().WriteWarning(Arg.Is<string>(m => m.Contains("Could not enumerate connected objects")));
		_rightsReader.Received(1).GetObjectRights("UsrPortalSpike2", Arg.Any<CreatioRequestOptions>());
	}
}
