using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Common.EntitySchema;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public sealed class DataBindingSchemaClientTests {
	[Test]
	[Category("Unit")]
	[Description("Builds the binding schema from the shared runtime-schema reader while preserving UIds, integer data value types, the primary column UId, and the resolved primary display column name.")]
	public void Fetch_Should_Build_DataBindingSchema_From_Shared_Runtime_Reader() {
		// Arrange
		IRuntimeEntitySchemaReader runtimeEntitySchemaReader = Substitute.For<IRuntimeEntitySchemaReader>();
		runtimeEntitySchemaReader.GetByName("Contact").Returns(
			new RuntimeEntitySchemaResult(
				Guid.Parse("11111111-1111-1111-1111-111111111111"),
				"Contact",
				Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
				"Name",
				Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
				[
					new RuntimeEntitySchemaColumnResult(
						Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
						"Id",
						null,
						null,
						0,
						true,
						false,
						null),
					new RuntimeEntitySchemaColumnResult(
						Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
						"Name",
						"Full name",
						null,
						1,
						true,
						false,
						null),
					new RuntimeEntitySchemaColumnResult(
						Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
						"Account",
						"Account",
						null,
						10,
						false,
						true,
						"Account")
				]));
		DataBindingSchemaClient client = new(runtimeEntitySchemaReader);

		// Act
		DataBindingSchema result = client.Fetch("Contact");

		// Assert
		result.UId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"),
			because: "the binding schema should preserve the runtime schema identity");
		result.PrimaryColumnUId.Should().Be(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
			because: "the binding schema should preserve the runtime primary column UId");
		result.PrimaryDisplayColumnName.Should().Be("Name",
			because: "the binding schema should preserve the resolved primary display column name");
		result.Columns.Should().HaveCount(3, because: "the binding client should preserve the full runtime column set, including inherited columns");
		result.Columns.Should().Contain(column =>
				column.Name == "Account"
				&& column.UId == Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")
				&& column.DataValueType == 10
				&& column.ReferenceSchemaName == "Account",
			because: "the binding schema should preserve UId, integer data value type, and reference schema values from the shared runtime model");
	}

	[Test]
	[Category("Unit")]
	[Description("Fails clearly when the shared runtime reader returns a schema without a primary column UId.")]
	public void Fetch_Should_Fail_When_PrimaryColumnUId_Is_Empty() {
		// Arrange
		IRuntimeEntitySchemaReader runtimeEntitySchemaReader = Substitute.For<IRuntimeEntitySchemaReader>();
		runtimeEntitySchemaReader.GetByName("Contact").Returns(
			new RuntimeEntitySchemaResult(
				Guid.Parse("11111111-1111-1111-1111-111111111111"),
				"Contact",
				Guid.Empty,
				"Name",
				null,
				new List<RuntimeEntitySchemaColumnResult>()));
		DataBindingSchemaClient client = new(runtimeEntitySchemaReader);

		// Act
		Action act = () => client.Fetch("Contact");

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "schemas without a primary column cannot be converted into data-binding schemas")
			.WithMessage("*does not expose a primary column*");
	}
}
