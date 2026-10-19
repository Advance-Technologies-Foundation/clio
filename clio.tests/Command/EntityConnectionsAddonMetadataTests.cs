namespace Clio.Tests.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command.EntityConnections;
using FluentAssertions;
using NUnit.Framework;

/// <summary>
/// The port of creatio-ui <c>entity-connections-addon.ts</c>. Each Build case is also checked against an independent
/// model of the server merge (add-on items by position, then the table rows in order, minus removals), so a case can
/// only pass when the written content really produces the requested list.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class EntityConnectionsAddonMetadataTests {
	private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000000");
	private static readonly Guid B = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000000");
	private static readonly Guid C = Guid.Parse("cccccccc-0000-0000-0000-000000000000");
	private static readonly Guid D = Guid.Parse("dddddddd-0000-0000-0000-000000000000");

	private IEntityConnectionsAddonMetadata _metadata = null!;

	[SetUp]
	public void SetUp() {
		_metadata = new EntityConnectionsAddonMetadata();
	}

	// Independent oracle of the CrtUIv2 provider merge.
	private static List<Guid> Merge(EntityConnectionsAddonContent content, IReadOnlyList<Guid> table) {
		HashSet<Guid> excluded = [.. content.Removed];
		List<Guid> items = content.Items.Where(columnUId => !excluded.Contains(columnUId)).ToList();
		return [.. items, .. table.Where(columnUId => !excluded.Contains(columnUId) && !items.Contains(columnUId))];
	}

	private static EntityConnectionsAddonContent Content(Guid[] items, Guid[] removed) =>
		new(items, removed, new JsonObject());

	[Test]
	[Description("Empty metadata is an add-on without a layer: no items, no removals.")]
	public void Parse_ShouldReturnEmptyContent_WhenMetadataIsEmpty() {
		// Act
		EntityConnectionsAddonContent content = _metadata.Parse("");

		// Assert
		content.Items.Should().BeEmpty(because: "an object without an add-on layer has no items");
		content.Removed.Should().BeEmpty(because: "an object without an add-on layer has no removals");
	}

	[Test]
	[Description("Parses the server format, orders items by Position, and matches property names case-insensitively.")]
	public void Parse_ShouldOrderItemsByPosition_AndReadAnyCasing() {
		// Arrange
		string metaData = $$"""
			{"Items":[{"UId":"{{B}}","columnUId":"{{B}}","position":1},{"UId":"{{A}}","ColumnUId":"{{A}}","Position":0},{"Position":2}],
			 "removed":[{"UId":"{{C}}","ColumnUId":"{{C}}"}]}
			""";

		// Act
		EntityConnectionsAddonContent content = _metadata.Parse(metaData);

		// Assert
		content.Items.Should().Equal([A, B],
			because: "items are sorted by Position, read under either casing, and an entry without a column is skipped");
		content.Removed.Should().Equal([C], because: "the removals are read as stored");
	}

	[Test]
	[Description("Metadata that is not JSON fails instead of being rewritten from an empty layer.")]
	public void Parse_ShouldThrow_WhenMetadataIsNotJson() {
		// Act
		Action act = () => _metadata.Parse("not json");

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "writing over an unreadable layer would silently drop whatever it held");
	}

	[Test]
	[Description("Serialization writes the server shape with UId equal to ColumnUId and positions by index, and keeps unknown top-level properties.")]
	public void Serialize_ShouldWriteServerShape_AndKeepOtherProperties() {
		// Arrange
		EntityConnectionsAddonContent content = _metadata.Parse(
			$$"""{"futureField":{"enabled":true},"Items":[{"ColumnUId":"{{A}}","Position":5}]}""");
		content = content with { Items = [B, A], Removed = [C] };

		// Act
		JsonObject written = JsonNode.Parse(_metadata.Serialize(content))!.AsObject();

		// Assert
		written["futureField"]!["enabled"]!.GetValue<bool>().Should().BeTrue(
			because: "a top-level property this tool does not model must survive the write");
		written.ContainsKey("Items").Should().BeFalse(
			because: "the old casing of the items key is replaced, not duplicated next to the new one");
		written["items"]!.AsArray().Select(item => (
				item!["UId"]!.GetValue<string>(), item["ColumnUId"]!.GetValue<string>(), item["Position"]!.GetValue<int>()))
			.Should().Equal([(B.ToString("D"), B.ToString("D"), 0), (A.ToString("D"), A.ToString("D"), 1)],
				because: "UId always equals ColumnUId and positions follow the list order");
		written["removed"]!.AsArray().Select(item => item!["ColumnUId"]!.GetValue<string>())
			.Should().Equal([C.ToString("D")], because: "removals are written with their column");
	}

	[Test]
	[Description("An unchanged list writes nothing.")]
	public void Build_ShouldReturnNull_WhenListIsUnchanged() {
		// Act
		EntityConnectionsAddonContent result = _metadata.Build(EntityConnectionsAddonContent.Empty, [A, B, C], [A, B, C]);

		// Assert
		result.Should().BeNull(because: "a desired list equal to the effective one needs no add-on write");
	}

	[Test]
	[Description("Removing a table connection writes only a removal.")]
	public void Build_ShouldWriteOnlyRemoval_WhenTableConnectionIsRemoved() {
		// Act
		EntityConnectionsAddonContent result = _metadata.Build(EntityConnectionsAddonContent.Empty, [A, B, C], [A, C]);

		// Assert
		result.Items.Should().BeEmpty(because: "the remaining columns keep their table order, so none becomes an item");
		result.Removed.Should().Equal([B], because: "the column left out is removed");
		Merge(result, [A, B, C]).Should().Equal([A, C], because: "the server merge must give the requested list");
	}

	[Test]
	[Description("Moving a column up writes an item only for the moved leading part, not for the columns after it.")]
	public void Build_ShouldWriteItemsOnlyForMovedLeadingPart_WhenColumnIsMovedUp() {
		// Act
		EntityConnectionsAddonContent result = _metadata.Build(EntityConnectionsAddonContent.Empty, [A, B, C, D], [C, A, B, D]);

		// Assert
		result.Items.Should().Equal([C], because: "only the moved column has to be an item; the rest keep table order");
		result.Removed.Should().BeEmpty(because: "nothing was removed");
		Merge(result, [A, B, C, D]).Should().Equal([C, A, B, D], because: "the server merge must give the requested order");
	}

	[Test]
	[Description("Reordering writes items up to the last column that is out of table order.")]
	public void Build_ShouldWriteItemsUpToLastOutOfOrderColumn_WhenColumnsAreSwapped() {
		// Act
		EntityConnectionsAddonContent result = _metadata.Build(EntityConnectionsAddonContent.Empty, [A, B, C, D], [A, C, B, D]);

		// Assert
		result.Items.Should().Equal([A, C], because: "the prefix up to the swapped column must be pinned as items");
		Merge(result, [A, B, C, D]).Should().Equal([A, C, B, D], because: "the server merge must give the requested order");
	}

	[Test]
	[Description("A newly added lookup column with no table row becomes an item at its position.")]
	public void Build_ShouldWriteAddedColumnAsItem_WhenColumnHasNoTableRow() {
		// Act
		EntityConnectionsAddonContent result = _metadata.Build(EntityConnectionsAddonContent.Empty, [A, B], [A, B, C]);

		// Assert
		result.Items.Should().Equal([A, B, C], because: "a column with no table row exists only as an add-on item");
		result.Removed.Should().BeEmpty(because: "nothing was removed");
		Merge(result, [A, B]).Should().Equal([A, B, C], because: "the server merge must give the requested list");
	}

	[Test]
	[Description("Re-adding a removed column drops its removal and keeps the loaded items and the other removals.")]
	public void Build_ShouldDropRemoval_WhenRemovedColumnIsAddedBack() {
		// Arrange
		EntityConnectionsAddonContent baseContent = Content([D], [B, C]);

		// Act
		EntityConnectionsAddonContent result = _metadata.Build(baseContent, [D, A], [D, A, B]);

		// Assert
		result.Items.Should().Equal([D, A, B], because: "the re-added column has to be placed after the loaded items");
		result.Removed.Should().Equal([C], because: "the re-added column leaves the removals, the other one stays");
		Merge(result, [A, B, C]).Should().Equal([D, A, B], because: "the server merge must give the requested list");
	}

	[Test]
	[Description("Removing a column that was a loaded add-on item moves it into the removals.")]
	public void Build_ShouldMoveRemovedItemIntoRemovals_WhenLoadedItemIsRemoved() {
		// Arrange
		EntityConnectionsAddonContent baseContent = Content([D], []);

		// Act
		EntityConnectionsAddonContent result = _metadata.Build(baseContent, [D, A, B], [A, B]);

		// Assert
		result.Items.Should().BeEmpty(because: "the remaining columns are in table order");
		result.Removed.Should().Equal([D], because: "a dropped item must be removed explicitly — a parent layer may hold it");
		Merge(result, [A, B]).Should().Equal([A, B], because: "the server merge must give the requested list");
	}

	[Test]
	[Description("Removing every connection removes each effective column.")]
	public void Build_ShouldRemoveEveryColumn_WhenDesiredListIsEmpty() {
		// Act
		EntityConnectionsAddonContent result = _metadata.Build(EntityConnectionsAddonContent.Empty, [A, B], []);

		// Assert
		result.Items.Should().BeEmpty(because: "an empty list has no items");
		result.Removed.Should().Equal([A, B], because: "every effective column has to be removed");
		Merge(result, [A, B]).Should().BeEmpty(because: "the server merge must give an empty list");
	}

	[Test]
	[Description("Build keeps the top-level properties of the base content.")]
	public void Build_ShouldKeepOtherProperties_WhenListChanges() {
		// Arrange
		EntityConnectionsAddonContent baseContent = _metadata.Parse("""{"futureField":1}""");

		// Act
		EntityConnectionsAddonContent result = _metadata.Build(baseContent, [A, B], [B, A]);

		// Assert
		result.OtherProperties["futureField"]!.GetValue<int>().Should().Be(1,
			because: "the write starts from the read content and changes only items and removals");
	}

	[Test]
	[Description("WithoutColumns drops the items and removals of the given columns; the same instance is returned when none is referenced.")]
	public void WithoutColumns_ShouldDropGivenColumns_AndReturnSameInstanceWhenNoneReferenced() {
		// Arrange
		EntityConnectionsAddonContent content = Content([A, B, C], [D]);

		// Act
		EntityConnectionsAddonContent filtered = _metadata.WithoutColumns(content, new HashSet<Guid> { B, D });
		EntityConnectionsAddonContent untouched = _metadata.WithoutColumns(content, new HashSet<Guid> { Guid.NewGuid() });

		// Assert
		filtered.Items.Should().Equal([A, C], because: "the item of a dropped column leaves the content");
		filtered.Removed.Should().BeEmpty(because: "the removal of a dropped column leaves the content");
		untouched.Should().BeSameAs(content, because: "the caller detects a needed clean-up write by reference");
	}
}
