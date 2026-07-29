using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Unit tests for the optional <see cref="DataBindingColumnPolicy"/> on
/// <c>DataBindingDbService.BuildSaveSchemaDataRequest</c>: with no policy the payload must stay
/// key-on-Id / no-force-update (the shape every existing caller relies on), and with a policy the
/// named columns must be emitted as keys / force-updated.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class BuildSaveSchemaDataRequestPolicyTests {

	private static readonly PackageRef Package = new(Guid.Parse("1d07fd0e-2ca4-4d20-93b4-eb5a795ea03f"), "UsrPkg");

	private static DataBindingDbSchema BuildSysSettingsValueSchema() {
		List<DataBindingSchemaColumn> columns = [
			new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"), "Id", 0, null),
			new(Guid.Parse("00000000-0000-0000-0000-0000000000a2"), "SysSettings", 10, "SysSettings"),
			new(Guid.Parse("00000000-0000-0000-0000-0000000000a3"), "SysAdminUnit", 10, "SysAdminUnit"),
			new(Guid.Parse("00000000-0000-0000-0000-0000000000a4"), "TextValue", 1, null),
			new(Guid.Parse("00000000-0000-0000-0000-0000000000a5"), "BinaryValue", 13, null)
		];
		return new DataBindingDbSchema(
			Guid.Parse("00000000-0000-0000-0000-0000000000ff"), "SysSettingsValue",
			columns.Select(c => c.Name).ToList(), columns);
	}

	private static (bool IsKey, bool IsForceUpdate) ColumnFlags(string requestBody, string columnName) {
		using JsonDocument document = JsonDocument.Parse(requestBody);
		JsonElement column = document.RootElement.GetProperty("columns").EnumerateArray()
			.Single(c => c.GetProperty("name").GetString() == columnName);
		return (column.GetProperty("isKey").GetBoolean(), column.GetProperty("isForceUpdate").GetBoolean());
	}

	/// <summary>Every column name whose <paramref name="flagName"/> flag is set in the delivered payload.</summary>
	private static string[] ColumnsWhereFlagIsSet(string body, string flagName) {
		using JsonDocument document = JsonDocument.Parse(body);
		return document.RootElement
			.GetProperty("columns")
			.EnumerateArray()
			.Where(column => column.GetProperty(flagName).GetBoolean())
			.Select(column => column.GetProperty("name").GetString() ?? string.Empty)
			.ToArray();
	}

	[Test]
	[Description("With no column policy every column but the primary Id is a non-key, matching the insert-only shape every existing caller (lookup registration, create/upsert/remove) relies on.")]
	public void BuildSaveSchemaDataRequest_Should_Key_Only_On_Id_When_No_Policy() {
		// Arrange
		DataBindingDbSchema schema = BuildSysSettingsValueSchema();

		// Act
		string body = DataBindingDbService.BuildSaveSchemaDataRequest(
			Package, "ClioBranding_Logos", "SysSettingsValue", schema, ["11111111-1111-1111-1111-111111111111"]);

		// Assert
		ColumnsWhereFlagIsSet(body, "isKey").Should().BeEquivalentTo(["Id"],
			because: "the whole key set — not merely the presence of Id — is what every existing caller (lookup registration, create/upsert/remove) relies on; a regression that keyed additional columns would silently widen their install-time match");
	}

	[Test]
	[Description("With no column policy the primary Id is never force-updated, so the default binding stays insert-only for clio-generated rows.")]
	public void BuildSaveSchemaDataRequest_Should_Not_ForceUpdate_When_No_Policy() {
		// Arrange
		DataBindingDbSchema schema = BuildSysSettingsValueSchema();

		// Act
		string body = DataBindingDbService.BuildSaveSchemaDataRequest(
			Package, "ClioBranding_Logos", "SysSettingsValue", schema, ["11111111-1111-1111-1111-111111111111"]);

		// Assert
		ColumnsWhereFlagIsSet(body, "isForceUpdate").Should().BeEmpty(
			because: "no column at all may be force-updated without a policy; asserting only one column would let a regression that force-updated every other column pass unnoticed");
	}

	[Test]
	[Description("A policy that names natural key columns marks exactly those columns as keys so the binding merges on install by its natural key.")]
	public void BuildSaveSchemaDataRequest_Should_Mark_Policy_Key_Columns_As_Keys() {
		// Arrange
		DataBindingDbSchema schema = BuildSysSettingsValueSchema();
		DataBindingColumnPolicy policy = new(["SysSettings", "SysAdminUnit"], ["TextValue", "BinaryValue"]);

		// Act
		string body = DataBindingDbService.BuildSaveSchemaDataRequest(
			Package, "ClioBranding_Logos", "SysSettingsValue", schema,
			["11111111-1111-1111-1111-111111111111"], null, policy);

		// Assert
		ColumnFlags(body, "SysSettings").IsKey.Should().BeTrue(
			because: "the natural key column named in the policy must be emitted as a binding key");
	}

	[Test]
	[Description("A policy that lists force-update columns marks exactly those columns as force-updated so an install overwrites the target value instead of leaving it.")]
	public void BuildSaveSchemaDataRequest_Should_Mark_Policy_ForceUpdate_Columns() {
		// Arrange
		DataBindingDbSchema schema = BuildSysSettingsValueSchema();
		DataBindingColumnPolicy policy = new(["SysSettings", "SysAdminUnit"], ["TextValue", "BinaryValue"]);

		// Act
		string body = DataBindingDbService.BuildSaveSchemaDataRequest(
			Package, "ClioBranding_Logos", "SysSettingsValue", schema,
			["11111111-1111-1111-1111-111111111111"], null, policy);

		// Assert
		ColumnFlags(body, "BinaryValue").IsForceUpdate.Should().BeTrue(
			because: "the value column named in the policy must be force-updated so branding actually overwrites the target");
	}

	[Test]
	[Description("A policy key column that is not the primary Id demotes Id to a non-key, so the merge happens on the natural key rather than the environment-random Id.")]
	public void BuildSaveSchemaDataRequest_Should_Demote_Id_When_Policy_Keys_Elsewhere() {
		// Arrange
		DataBindingDbSchema schema = BuildSysSettingsValueSchema();
		DataBindingColumnPolicy policy = new(["SysSettings", "SysAdminUnit"], ["TextValue", "BinaryValue"]);

		// Act
		string body = DataBindingDbService.BuildSaveSchemaDataRequest(
			Package, "ClioBranding_Logos", "SysSettingsValue", schema,
			["11111111-1111-1111-1111-111111111111"], null, policy);

		// Assert
		ColumnFlags(body, "Id").IsKey.Should().BeFalse(
			because: "when the policy keys on natural columns the environment-random Id must not be a key");
	}

	[Test]
	[Description("A policy that references a column absent from the projected schema fails fast with the offending column named, instead of posting an opaque SaveSchema payload the platform would reject.")]
	public void BuildSaveSchemaDataRequest_Should_Throw_When_Policy_Column_Missing() {
		// Arrange
		DataBindingDbSchema schema = BuildSysSettingsValueSchema();
		DataBindingColumnPolicy policy = new(["SysSettings", "MissingColumn"], ["TextValue"]);

		// Act
		Action act = () => DataBindingDbService.BuildSaveSchemaDataRequest(
			Package, "ClioBranding_Logos", "SysSettingsValue", schema, [], null, policy);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "a policy column not present in the projected schema must be rejected before the remote call")
			.WithMessage("*MissingColumn*");
	}

	[Test]
	[Description("A column cannot be both a key and force-updated, because a key is a match column; the policy is rejected with the offending column named.")]
	public void BuildSaveSchemaDataRequest_Should_Throw_When_Key_Is_Also_ForceUpdated() {
		// Arrange
		DataBindingDbSchema schema = BuildSysSettingsValueSchema();
		DataBindingColumnPolicy policy = new(["SysSettings"], ["SysSettings"]);

		// Act
		Action act = () => DataBindingDbService.BuildSaveSchemaDataRequest(
			Package, "ClioBranding_Logos", "SysSettingsValue", schema, [], null, policy);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "force-updating a key column is contradictory and must be rejected")
			.WithMessage("*SysSettings*");
	}

	[Test]
	[Description("A policy with no key column is rejected, because a binding must match the target row on at least one column.")]
	public void BuildSaveSchemaDataRequest_Should_Throw_When_No_Key_Column() {
		// Arrange
		DataBindingDbSchema schema = BuildSysSettingsValueSchema();
		DataBindingColumnPolicy policy = new([], ["TextValue"]);

		// Act
		Action act = () => DataBindingDbService.BuildSaveSchemaDataRequest(
			Package, "ClioBranding_Logos", "SysSettingsValue", schema, [], null, policy);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "a binding must declare at least one key column to match the target row on install");
	}
}
