using System.Text.Json;
using Clio.Command.ProcessModel;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// The diagram members of the describe read-back, over the wire shape the server really sends.
/// </summary>
/// <remarks>
/// They are typed rather than left in the overflow bag because they are the vocabulary a layout question is
/// answered in, and a typed property that does not bind is worse than none: the field is present in the JSON,
/// absent from the object, and the caller concludes the package cannot report it.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "ProcessModel")]
public class DescribedDiagramGeometryTests {

	private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

	[Test]
	[Description("An element binds both its position and its size. Without the size a described position cannot "
		+ "be inverted into a diagram row - elements of different heights share a row by its centre line.")]
	public void DescribedElement_ShouldBindPositionAndSize() {
		// Arrange
		const string json = """
			{ "name": "ReadOrder", "position": "240;172", "size": "69;55" }
			""";

		// Act
		DescribedElement element = JsonSerializer.Deserialize<DescribedElement>(json, Options);

		// Assert
		element.Position.Should().Be("240;172", because: "the position is the shape's top-left corner");
		element.Size.Should().Be("69;55", because: "the size is what makes that corner invertible into a row");
	}

	[Test]
	[Description("A flow binds its whole stored chain: where it leaves, the corners between, where it arrives, "
		+ "and the edge of each shape it attaches to. Every criterion about a connector is a statement about "
		+ "these five members.")]
	public void DescribedFlow_ShouldBindTheWholeStoredChain() {
		// Arrange
		const string json = """
			{
			  "source": "HoldFindingsSharingMeeting", "target": "JoinMarketingAndFulfilment", "kind": "sequence",
			  "geometry": {
			    "start": "1029;330", "points": ["1348;330"], "end": "1348;227",
			    "exitSide": "right", "entrySide": "bottom"
			  }
			}
			""";

		// Act
		DescribedFlow flow = JsonSerializer.Deserialize<DescribedFlow>(json, Options);

		// Assert
		flow.Geometry.Should().NotBeNull(because: "the member is typed, so it has to bind rather than fall into "
			+ "the overflow bag where no caller looks for it");
		flow.Geometry.Start.Should().Be("1029;330", because: "the exit point is on the source's border");
		flow.Geometry.End.Should().Be("1348;227", because: "the entry point is on the target's border");
		flow.Geometry.Points.Should().Equal(new[] { "1348;330" },
			because: "one corner is what an L-shaped connector into a merge has");
		flow.Geometry.ExitSide.Should().Be("right", because: "it leaves along its own row");
		flow.Geometry.EntrySide.Should().Be("bottom",
			because: "and comes up into the merge's bottom vertex, the one every arrival from below shares");
	}

	[Test]
	[Description("A package that predates these members leaves them absent, and absent must read as null rather "
		+ "than as an empty value: a caller that cannot tell 'this package does not report geometry' from 'this "
		+ "connector has none' reports the wrong one as a fact.")]
	public void DescribedDiagramMembers_ShouldBeNull_WhenTheServerOmitsThem() {
		// Arrange
		const string elementJson = """{ "name": "ReadOrder", "position": "240;172" }""";
		const string flowJson = """{ "source": "A", "target": "B", "kind": "sequence" }""";

		// Act
		DescribedElement element = JsonSerializer.Deserialize<DescribedElement>(elementJson, Options);
		DescribedFlow flow = JsonSerializer.Deserialize<DescribedFlow>(flowJson, Options);

		// Assert
		element.Size.Should().BeNull(because: "an older CrtProcessBuilder does not send the size at all");
		flow.Geometry.Should().BeNull(because: "nor the geometry, and null is the honest answer for both");
	}

}
