using System;
using Clio.Common.RecordRights;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.RecordRights;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class RecordRightsConverterTests {

	#region Enum parsing

	[Test]
	[Description("Parses each human operation name case-insensitively to its wire value.")]
	public void ParseOperation_ShouldReturnWireValue_WhenNameIsKnown() {
		// Arrange & Act
		int read = RecordRightsConverter.ParseOperation("read");
		int edit = RecordRightsConverter.ParseOperation("EDIT");
		int delete = RecordRightsConverter.ParseOperation("  Delete ");

		// Assert
		read.Should().Be(0, because: "'read' maps to wire value 0");
		edit.Should().Be(1, because: "parsing is case-insensitive");
		delete.Should().Be(2, because: "surrounding whitespace is trimmed");
	}

	[Test]
	[Description("Renders each operation wire value back to its lower-case human name.")]
	public void OperationToName_ShouldRoundTrip_WhenValueIsKnown() {
		// Arrange & Act
		string read = RecordRightsConverter.OperationToName(0);
		string edit = RecordRightsConverter.OperationToName(1);
		string delete = RecordRightsConverter.OperationToName(2);

		// Assert
		read.Should().Be("read", because: "wire value 0 renders as 'read'");
		edit.Should().Be("edit", because: "wire value 1 renders as 'edit'");
		delete.Should().Be("delete", because: "wire value 2 renders as 'delete'");
	}

	[Test]
	[Description("Parses each human level name case-insensitively to its wire value and renders it back.")]
	public void ParseLevel_ShouldRoundTrip_WhenNameIsKnown() {
		// Arrange & Act
		int granted = RecordRightsConverter.ParseLevel("GRANTED");
		int delegated = RecordRightsConverter.ParseLevel("delegated");
		string grantedName = RecordRightsConverter.LevelToName(1);

		// Assert
		granted.Should().Be(1, because: "'granted' maps to RightLevel 1");
		delegated.Should().Be(2, because: "'delegated' maps to RightLevel 2");
		grantedName.Should().Be("granted", because: "RightLevel 1 renders as 'granted'");
	}

	[Test]
	[Description("Rejects an unknown operation name with a clear argument error naming the offending value.")]
	public void ParseOperation_ShouldThrow_WhenNameIsUnknown() {
		// Arrange
		Action act = () => RecordRightsConverter.ParseOperation("share");

		// Assert
		act.Should().Throw<ArgumentException>(because: "unknown operation names must be rejected")
			.WithMessage("*share*", because: "the error names the offending value");
	}

	[Test]
	[Description("Rejects an unknown level name with a clear argument error naming the offending value.")]
	public void ParseLevel_ShouldThrow_WhenNameIsUnknown() {
		// Arrange
		Action act = () => RecordRightsConverter.ParseLevel("owner");

		// Assert
		act.Should().Throw<ArgumentException>(because: "unknown level names must be rejected")
			.WithMessage("*owner*", because: "the error names the offending value");
	}

	#endregion
}
