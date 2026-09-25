using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.ObjectRights;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ObjectRights;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class ConnectedObjectsResolverTests {

	private IRemoteEntitySchemaColumnManager _columnManager;
	private ConnectedObjectsResolver _resolver;

	[SetUp]
	public void SetUp() {
		_columnManager = Substitute.For<IRemoteEntitySchemaColumnManager>();
		_resolver = new ConnectedObjectsResolver(_columnManager);
	}

	[Test]
	[Description("Returns only the root object and does not read the schema when include-connected is false.")]
	public void Resolve_ShouldReturnRootOnly_WhenIncludeConnectedFalse() {
		// Act
		ConnectedObjectsResolution result = _resolver.Resolve("UsrPortalSpike2", includeConnected: false);

		// Assert
		result.Objects.Should().Equal(new[] { "UsrPortalSpike2" }, because: "without fan-out only the root is targeted");
		result.EnumerationError.Should().BeNull(because: "nothing was enumerated, so nothing could fail");
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
		ConnectedObjectsResolution result = _resolver.Resolve("UsrPortalSpike2", includeConnected: true);

		// Assert
		result.Objects.Should().Equal(new[] { "UsrPortalSpike2", "UsrPSCategory", "UsrPSRegion" },
			because: "root first, then distinct own-lookup targets in order, without inherited/self/non-lookup columns");
		result.Excluded.Should().BeEmpty(because: "none of the lookups is a security or system object");
	}

	[Test]
	[Description("Security and system lookups (role/user directory, schema metadata, rights tables) are excluded from the fan-out and reported instead.")]
	public void Resolve_ShouldExcludeSecurityAndSystemObjects_FromTheFanOut() {
		// Arrange
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>())
			.Returns(Schema("UsrPortalSpike2",
				Column("UsrCategory", "own", "UsrPSCategory"),
				Column("UsrRole", "own", "SysAdminUnit"),
				Column("UsrUserRole", "own", "SysUserInRole"),
				Column("UsrSchema", "own", "SysSchema"),
				Column("UsrRight", "own", "SysContactRight"),
				Column("UsrContact", "own", "Contact")));

		// Act
		ConnectedObjectsResolution result = _resolver.Resolve("UsrPortalSpike2", includeConnected: true);

		// Assert
		result.Objects.Should().Equal(new[] { "UsrPortalSpike2", "Contact", "UsrPSCategory" },
			because: "an ordinary lookup is still fanned out to, a security or system object never is");
		result.Excluded.Should().BeEquivalentTo(new[] { "SysAdminUnit", "SysContactRight", "SysSchema", "SysUserInRole" },
			because: "the skipped objects are reported so the caller can warn about them");
	}

	[Test]
	[Description("A security or system object named as the ROOT is still targeted: the exclusion applies to the fan-out only.")]
	public void Resolve_ShouldKeepSystemRoot_WhenNamedExplicitly() {
		// Act
		ConnectedObjectsResolution result = _resolver.Resolve("SysAdminUnit", includeConnected: false);

		// Assert
		result.Objects.Should().Equal(new[] { "SysAdminUnit" },
			because: "naming the object yourself is the explicit way to grant it");
	}

	[Test]
	[Description("A failed schema read is reported as an enumeration error with the root alone; it does not throw.")]
	public void Resolve_ShouldReportEnumerationError_WhenSchemaReadThrows() {
		// Arrange
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>())
			.Returns(_ => throw new InvalidOperationException("schema read failed"));

		// Act
		ConnectedObjectsResolution result = _resolver.Resolve("UsrPortalSpike2", includeConnected: true);

		// Assert
		result.Objects.Should().Equal(new[] { "UsrPortalSpike2" }, because: "only the root is known");
		result.EnumerationError.Should().Be("schema read failed",
			because: "the caller must know the connected set is unknown, not empty");
	}

	[TestCase("SysPackageSchemaData")]
	[TestCase("SysSettingsValue")]
	[TestCase("UsrOrderRights")]
	[TestCase("sysadminunit")]
	[TestCase("SYSUSERINROLE")]
	[Description("Every excluded family is matched — SysPackage*, SysSettings*, the Rights suffix — and matching ignores case.")]
	public void Resolve_ShouldExcludeEveryFamily_CaseInsensitive(string referenced) {
		// Arrange
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>())
			.Returns(Schema("UsrOrder", Column("UsrRef", "own", referenced)));

		// Act
		ConnectedObjectsResolution result = _resolver.Resolve("UsrOrder", includeConnected: true);

		// Assert
		result.Objects.Should().Equal(new[] { "UsrOrder" }, because: $"'{referenced}' is a security or system object");
		result.Excluded.Should().Equal(new[] { referenced }, because: "the exclusion is reported");
	}

	[Test]
	[Description("The own-column source is matched without regard to case.")]
	public void Resolve_ShouldTreatSourceCaseInsensitively() {
		// Arrange
		_columnManager.GetSchemaProperties(Arg.Any<GetEntitySchemaPropertiesOptions>())
			.Returns(Schema("UsrOrder", Column("UsrStatus", "OWN", "UsrStatus")));

		// Act
		ConnectedObjectsResolution result = _resolver.Resolve("UsrOrder", includeConnected: true);

		// Assert
		result.Objects.Should().Equal(new[] { "UsrOrder", "UsrStatus" }, because: "'OWN' is an own column");
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
