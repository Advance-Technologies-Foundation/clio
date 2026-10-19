using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Clio.Command.EntityConnections;

/// <summary>
/// Content of one <c>EntityConnections</c> add-on layer as the schema designer returns it: the connection
/// columns the layer places first (<see cref="Items"/>, ordered by their <c>Position</c>), the columns it removes
/// (<see cref="Removed"/>), and every other top-level metadata property, kept untouched for the write.
/// </summary>
/// <param name="Items">Column UIds the layer places at the top of the list, in position order.</param>
/// <param name="Removed">Column UIds the layer removes from the list.</param>
/// <param name="OtherProperties">Top-level metadata properties other than <c>items</c> and <c>removed</c>.</param>
internal sealed record EntityConnectionsAddonContent(
	IReadOnlyList<Guid> Items,
	IReadOnlyList<Guid> Removed,
	JsonObject OtherProperties) {

	/// <summary>An add-on without a layer: no items, no removals, no other properties.</summary>
	internal static EntityConnectionsAddonContent Empty => new([], [], new JsonObject());
}

/// <summary>
/// Reads, writes and diffs the metadata of the <c>EntityConnections</c> add-on (core
/// <c>Terrasoft.Core.Addons.EntityConnections</c>), the store behind the Object designer "Connections" node.
/// A port of creatio-ui <c>entity-connections-addon.ts</c>, so clio writes exactly the content the designer would.
/// </summary>
internal interface IEntityConnectionsAddonMetadata {

	/// <summary>
	/// Parses add-on metadata received from the server. Property names are matched case-insensitively
	/// (<c>items</c>/<c>Items</c>, <c>ColumnUId</c>/<c>columnUId</c>); items are sorted by <c>Position</c>, entries
	/// without a column UId are skipped, and an empty body is an empty layer.
	/// </summary>
	/// <param name="metaData">The <c>metaData</c> string of the add-on schema.</param>
	/// <returns>The parsed content.</returns>
	/// <exception cref="InvalidOperationException">The metadata is not a JSON object.</exception>
	EntityConnectionsAddonContent Parse(string metaData);

	/// <summary>
	/// Serializes the content in the server shape <c>{"items":[{"UId","ColumnUId","Position"}],"removed":[{"UId","ColumnUId"}]}</c>
	/// (<c>UId</c> always equals <c>ColumnUId</c>), keeping every other top-level property.
	/// </summary>
	/// <param name="content">The content to write.</param>
	/// <returns>The metadata string to save.</returns>
	string Serialize(EntityConnectionsAddonContent content);

	/// <summary>
	/// Returns <paramref name="content"/> without the items and removals of <paramref name="columnUIds"/>; the same
	/// instance when it references none of them.
	/// </summary>
	/// <param name="content">The add-on content.</param>
	/// <param name="columnUIds">Columns to drop, for example columns the object no longer has.</param>
	/// <returns>The filtered content.</returns>
	EntityConnectionsAddonContent WithoutColumns(EntityConnectionsAddonContent content, IReadOnlySet<Guid> columnUIds);

	/// <summary>
	/// Builds the content of the current package's layer that turns <paramref name="effective"/> into
	/// <paramref name="desired"/>: columns of <paramref name="effective"/> missing from <paramref name="desired"/> are
	/// added to the removals, columns of <paramref name="desired"/> leave the removals, and the items are the shortest
	/// leading part of <paramref name="desired"/> such that the rest equals the remaining columns in their current
	/// order, so columns that were not moved do not become items.
	/// </summary>
	/// <param name="baseContent">The add-on content read for the current package (parent layers folded with it).</param>
	/// <param name="effective">The connection columns as the server merges them, in server order.</param>
	/// <param name="desired">The requested connection columns, in the requested order.</param>
	/// <returns><c>null</c> when <paramref name="desired"/> equals <paramref name="effective"/>: nothing is written then.</returns>
	EntityConnectionsAddonContent Build(
		EntityConnectionsAddonContent baseContent,
		IReadOnlyList<Guid> effective,
		IReadOnlyList<Guid> desired);
}

/// <inheritdoc />
internal sealed class EntityConnectionsAddonMetadata : IEntityConnectionsAddonMetadata {

	private const string ItemsKey = "items";
	private const string RemovedKey = "removed";
	private const string UIdKey = "UId";
	private const string ColumnUIdKey = "ColumnUId";
	private const string PositionKey = "Position";

	/// <inheritdoc />
	public EntityConnectionsAddonContent Parse(string metaData) {
		if (string.IsNullOrWhiteSpace(metaData)) {
			return EntityConnectionsAddonContent.Empty;
		}
		JsonObject raw;
		try {
			raw = JsonNode.Parse(metaData) as JsonObject;
		} catch (JsonException exception) {
			throw new InvalidOperationException(
				"The EntityConnections add-on metadata is not valid JSON: " + exception.Message, exception);
		}
		if (raw is null) {
			throw new InvalidOperationException("The EntityConnections add-on metadata is not a JSON object.");
		}

		List<Guid> items = ReadArray(raw, ItemsKey)
			.Select(entry => (columnUId: ReadColumnUId(entry), position: ReadPosition(entry)))
			.Where(entry => entry.columnUId.HasValue)
			// OrderBy is stable, so entries with the same position keep their stored order (as Array.sort does).
			.OrderBy(entry => entry.position)
			.Select(entry => entry.columnUId.Value)
			.ToList();
		List<Guid> removed = ReadArray(raw, RemovedKey)
			.Select(ReadColumnUId)
			.Where(columnUId => columnUId.HasValue)
			.Select(columnUId => columnUId.Value)
			.ToList();

		JsonObject other = new();
		foreach (KeyValuePair<string, JsonNode> property in raw) {
			if (IsKey(property.Key, ItemsKey) || IsKey(property.Key, RemovedKey)) {
				continue;
			}
			other[property.Key] = property.Value?.DeepClone();
		}
		return new EntityConnectionsAddonContent(items, removed, other);
	}

	/// <inheritdoc />
	public string Serialize(EntityConnectionsAddonContent content) {
		ArgumentNullException.ThrowIfNull(content);
		JsonObject result = (JsonObject)(content.OtherProperties?.DeepClone() ?? new JsonObject());
		JsonArray items = [];
		for (int position = 0; position < content.Items.Count; position++) {
			string columnUId = content.Items[position].ToString("D");
			items.Add(new JsonObject {
				[UIdKey] = columnUId,
				[ColumnUIdKey] = columnUId,
				[PositionKey] = position
			});
		}
		JsonArray removed = [];
		foreach (Guid columnUId in content.Removed) {
			string value = columnUId.ToString("D");
			removed.Add(new JsonObject { [UIdKey] = value, [ColumnUIdKey] = value });
		}
		result[ItemsKey] = items;
		result[RemovedKey] = removed;
		return result.ToJsonString();
	}

	/// <inheritdoc />
	public EntityConnectionsAddonContent WithoutColumns(
		EntityConnectionsAddonContent content, IReadOnlySet<Guid> columnUIds) {
		ArgumentNullException.ThrowIfNull(content);
		if (columnUIds is null || columnUIds.Count == 0) {
			return content;
		}
		List<Guid> items = content.Items.Where(columnUId => !columnUIds.Contains(columnUId)).ToList();
		List<Guid> removed = content.Removed.Where(columnUId => !columnUIds.Contains(columnUId)).ToList();
		if (items.Count == content.Items.Count && removed.Count == content.Removed.Count) {
			return content;
		}
		// Items are positioned by their index on serialization, so dropping one renumbers the rest.
		return content with { Items = items, Removed = removed };
	}

	/// <inheritdoc />
	public EntityConnectionsAddonContent Build(
		EntityConnectionsAddonContent baseContent,
		IReadOnlyList<Guid> effective,
		IReadOnlyList<Guid> desired) {
		ArgumentNullException.ThrowIfNull(baseContent);
		ArgumentNullException.ThrowIfNull(effective);
		ArgumentNullException.ThrowIfNull(desired);
		if (effective.SequenceEqual(desired)) {
			return null;
		}
		HashSet<Guid> desiredSet = [.. desired];
		List<Guid> removed = baseContent.Removed
			.Concat(effective.Where(columnUId => !desiredSet.Contains(columnUId)))
			.Where(columnUId => !desiredSet.Contains(columnUId))
			.Distinct()
			.ToList();
		HashSet<Guid> itemSet = [.. baseContent.Items];
		List<Guid> restOrder = effective.Where(columnUId => !itemSet.Contains(columnUId)).ToList();
		int itemCount = GetItemCount(desired, restOrder, itemSet);
		return baseContent with { Items = desired.Take(itemCount).ToList(), Removed = removed };
	}

	/// <summary>
	/// Returns how many leading columns of <paramref name="desired"/> must be add-on items so that the server merge
	/// (add-on items by position, then the other columns in their current order) gives <paramref name="desired"/>.
	/// </summary>
	private static int GetItemCount(IReadOnlyList<Guid> desired, IReadOnlyList<Guid> restOrder, IReadOnlySet<Guid> itemSet) {
		HashSet<Guid> restSet = [.. restOrder];
		int lowerBound = 0;
		for (int index = 0; index < desired.Count; index++) {
			if (itemSet.Contains(desired[index]) || !restSet.Contains(desired[index])) {
				lowerBound = index + 1;
			}
		}
		for (int count = lowerBound; count < desired.Count; count++) {
			List<Guid> tail = desired.Skip(count).ToList();
			HashSet<Guid> tailSet = [.. tail];
			if (tail.SequenceEqual(restOrder.Where(tailSet.Contains))) {
				return count;
			}
		}
		return desired.Count;
	}

	private static IEnumerable<JsonObject> ReadArray(JsonObject raw, string key) {
		JsonNode node = FindProperty(raw, key);
		return node is JsonArray array ? array.OfType<JsonObject>() : [];
	}

	private static Guid? ReadColumnUId(JsonObject entry) {
		JsonNode node = FindProperty(entry, ColumnUIdKey);
		if (node is JsonValue value && value.TryGetValue(out string text) && Guid.TryParse(text, out Guid columnUId)
			&& columnUId != Guid.Empty) {
			return columnUId;
		}
		return null;
	}

	private static double ReadPosition(JsonObject entry) {
		JsonNode node = FindProperty(entry, PositionKey);
		if (node is not JsonValue value) {
			return 0;
		}
		if (value.TryGetValue(out double number)) {
			return number;
		}
		return value.TryGetValue(out string text)
			&& double.TryParse(text, System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out double parsed)
				? parsed
				: 0;
	}

	// Exact spelling first, then any casing: the server serializes `items`/`ColumnUId`, older layers may differ.
	private static JsonNode FindProperty(JsonObject parent, string key) {
		if (parent.TryGetPropertyValue(key, out JsonNode exact)) {
			return exact;
		}
		return parent.FirstOrDefault(property => IsKey(property.Key, key)).Value;
	}

	private static bool IsKey(string candidate, string key) =>
		string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase);
}
