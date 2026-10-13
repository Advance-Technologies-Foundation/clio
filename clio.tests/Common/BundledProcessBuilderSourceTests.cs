using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// The region arithmetic of <see cref="BundledProcessBuilderSource"/>, over synthetic source, so a drift test
/// that fails on the real archive is known to be about the archive and not about this reader.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class BundledProcessBuilderSourceTests {

	[Test]
	[Description("A contract class that nests a region of its own still yields the members after that nested region: the class region ends at the #endregion that balances it, not the first one.")]
	public void DataMemberNames_ShouldReadPastANestedRegion_WhenTheClassNestsOne() {
		// Arrange
		string source = string.Join("\n",
			"#region Class: ProbeDescriptor",
			"[DataMember(Name = \"first\")]",
			"#region Properties",
			"[DataMember(Name = \"nested\")]",
			"#endregion",
			"[DataMember(Name = \"last\")]",
			"#endregion",
			"#region Class: LaterDescriptor",
			"[DataMember(Name = \"later\")]",
			"#endregion",
			"");

		// Act
		IReadOnlyList<string> members = BundledProcessBuilderSource.DataMemberNames(source, "ProbeDescriptor");

		// Assert
		members.Should().Equal(["first", "nested", "last"],
			because: "a window cut at the nested #endregion would drop 'last' and a drift test would blame the shape, "
				+ "while a window run past the class's own #endregion would take 'later' from the next type");
	}

	[Test]
	[Description("A contract class whose region never balances answers no members rather than widening to the rest of the source, nested region or not.")]
	public void DataMemberNames_ShouldReturnNothing_WhenTheClassRegionNeverBalances() {
		// Arrange
		string source = string.Join("\n",
			"#region Class: ProbeDescriptor",
			"[DataMember(Name = \"first\")]",
			"#region Properties",
			"#endregion",
			"[DataMember(Name = \"fromAnotherType\")]",
			"");

		// Act
		IReadOnlyList<string> members = BundledProcessBuilderSource.DataMemberNames(source, "ProbeDescriptor");

		// Assert
		members.Should().BeEmpty(
			because: "taking the rest of the source as the window would let a member of any later type satisfy a probe");
	}
}
