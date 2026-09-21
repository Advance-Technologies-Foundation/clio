using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.ObjectRights;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ObjectRights;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class ConnectedObjectsResolverTests {

	private IRemoteEntitySchemaColumnManager _columnManager;
	private ILogger _logger;
	private ConnectedObjectsResolver _resolver;

	[SetUp]
	public void SetUp() {
		_columnManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
		_logger = Substitute.For<ILogger>();
		_resolver = new ConnectedObjectsResolver(_columnManager, _logger);
	}

	[Test]
	[Description("Returns only the root object and does not read the schema when include-connected is false.")]
	public void Resolve_ShouldReturnRootOnly_WhenIncludeConnectedFalse() {
		// Act
		IReadOnlyList<string> result = _resolver.Resolve("UsrPortalSpike2", includeConnected: false, "Checking");

		// Assert
		result.Should().Equal(new[] { "UsrPortalSpike2" }, because: "without fan-out only the root is targeted");
		_columnManager.DidNotReceive().GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>());
	}

	[Test]
	[Description("Returns the root plus distinct OWN lookup targets, excluding inherited audit lookups and self-references.")]
	public void Resolve_ShouldReturnRootAndOwnLookups_WhenIncludeConnectedTrue() {
		// Arrange
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>())
			.Returns(Schema("UsrPortalSpike2",
				Column("UsrCategory", "own", "UsrPSCategory"),
				Column("UsrRegion", "own", "UsrPSRegion"),
				Column("UsrDup", "own", "UsrPSCategory"),      // duplicate reference — de-duped
				Column("UsrSelf", "own", "UsrPortalSpike2"),    // self reference — excluded
				Column("CreatedBy", "inherited", "Contact"),    // inherited audit lookup — excluded
				Column("UsrText", "own", null)));               // non-lookup — excluded

		// Act
		IReadOnlyList<string> result = _resolver.Resolve("UsrPortalSpike2", includeConnected: true, "Checking");

		// Assert
		result.Should().Equal(new[] { "UsrPortalSpike2", "UsrPSCategory", "UsrPSRegion" },
			because: "root first, then distinct own-lookup targets in order, without inherited/self/non-lookup columns");
	}

	[Test]
	[Description("Falls back to the root object with a warning when the schema read fails.")]
	public void Resolve_ShouldFallBackToRoot_WhenSchemaReadThrows() {
		// Arrange
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>())
			.Returns(_ => throw new InvalidOperationException("schema read failed"));

		// Act
		IReadOnlyList<string> result = _resolver.Resolve("UsrPortalSpike2", includeConnected: true, "Changing");

		// Assert
		result.Should().Equal(new[] { "UsrPortalSpike2" }, because: "a failed enumeration still checks the root");
		_logger.Received().WriteWarning(Arg.Is<string>(m =>
			m.Contains("Could not enumerate connected objects") && m.Contains("Changing")));
	}

	private static EntitySchemaPropertyColumnInfo Column(string name, string source, string referenceSchemaName) =>
		new(Name: name, UId: Guid.NewGuid(), Source: source, Title: null, Description: null,
			Type: referenceSchemaName is null ? "Text" : "Lookup", Required: false, Indexed: false,
			ReferenceSchemaName: referenceSchemaName);

	private static EntitySchemaPropertiesInfo Schema(string name, params EntitySchemaPropertyColumnInfo[] columns) =>
		new(Name: name, Title: null, Description: null, PackageName: name, ParentSchemaName: null,
			ExtendParent: false, PrimaryColumnName: null, PrimaryDisplayColumnName: null, OwnColumnCount: 0,
			InheritedColumnCount: 0, IndexesCount: null, TrackChangesInDb: false, DbView: false, SspAvailable: null,
			Virtual: false, UseRecordDeactivation: null, ShowInAdvancedMode: false, AdministratedByOperations: false,
			AdministratedByColumns: false, AdministratedByRecords: false, UseDenyRecordRights: null,
			UseLiveEditing: null, Columns: columns);
}
