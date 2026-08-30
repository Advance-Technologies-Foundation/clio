using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Creatio.ConflictResolver.Strategies;

internal static class JsonMetadataReorderer
{
	private const string ItemsPropertyName = "Items";
	private const string UidPropertyName = "UId";
	private static readonly JsonSerializerOptions PrettyJsonOptions = new()
	{
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	public static bool TryReorder(
		string baseContent,
		string localContent,
		string remoteContent,
		string mergedContent,
		out string orderedMergedContent)
	{
		orderedMergedContent = mergedContent;
		if (!TryParseItems(baseContent, out _, out var baseItems))
		{
			return false;
		}

		if (!TryParseItems(localContent, out _, out var localItems))
		{
			return false;
		}

		if (!TryParseItems(remoteContent, out _, out var remoteItems))
		{
			return false;
		}

		if (!TryParseItems(mergedContent, out var mergedRoot, out var mergedItems))
		{
			return false;
		}

		var orderedReferenceItems = BuildOrderedReferenceItems(baseItems, localItems, remoteItems);
		var uidIndexMap = BuildUidIndexMap(orderedReferenceItems);
		var orderedItems = mergedItems
			.Select((item, position) => new IndexedMergedItem(
				uidIndexMap.TryGetValue(item.UId, out var index) ? index : int.MaxValue,
				position,
				item))
			.OrderBy(static x => x.Index)
			.ThenBy(static x => x.Position)
			.Select(static x => x.Item.Node.DeepClone())
			.ToArray();

		var itemsArray = new JsonArray();
		foreach (var orderedItem in orderedItems)
		{
			itemsArray.Add(orderedItem);
		}

		mergedRoot![ItemsPropertyName] = itemsArray;
		orderedMergedContent = BoundedJsonSerializer.Serialize(mergedRoot, PrettyJsonOptions);
		return true;
	}

	private static IReadOnlyDictionary<string, int> BuildUidIndexMap(IReadOnlyList<DiffMetadataItem> orderedItems) {
		var indexByUid = new Dictionary<string, int>(StringComparer.Ordinal);
		var currentIndex = 0;
		foreach (var item in orderedItems) {
			indexByUid[item.UId] = currentIndex;
			currentIndex++;
		}
		return indexByUid;
	}

	private static IReadOnlyDictionary<string, int> BuildUidIndexMap(IReadOnlyList<ConcurrentDiffMetadataItem> items) {
		var indexByUid = new Dictionary<string, int>(StringComparer.Ordinal);
		var currentIndex = 0;
		foreach (var item in items) {
			if (indexByUid.ContainsKey(item.UId)) {
				continue;
			}
			indexByUid[item.UId] = currentIndex;
			currentIndex++;
		}
		return indexByUid;
	}

	private static IReadOnlyList<DiffMetadataItem> BuildOrderedReferenceItems(
		IReadOnlyList<DiffMetadataItem> baseItems,
		IReadOnlyList<DiffMetadataItem> localItems,
		IReadOnlyList<DiffMetadataItem> remoteItems)
	{
		var baseLocalPair = new DiffMetadataItemPairCollection(localItems, baseItems, 1);
		var baseRemotePair = new DiffMetadataItemPairCollection(remoteItems, baseItems, 2);
		var localRemotePair = new DiffMetadataItemPairCollection(localItems, remoteItems, 3);
		var pairCollections = new[] {
			localRemotePair,
			baseLocalPair,
			baseRemotePair
		};

		var concurrentItems = new List<ConcurrentDiffMetadataItem>();
		foreach (var pairCollection in pairCollections) {
			var secondCollectionIndexByUid = BuildIndexMap(pairCollection.SecondCollection);
			for (var firstIndex = 0; firstIndex < pairCollection.FirstCollection.Count; firstIndex++) {
				var uid = pairCollection.FirstCollection[firstIndex].UId;
				if (!secondCollectionIndexByUid.TryGetValue(uid, out var secondIndex)) {
					continue;
				}
				concurrentItems.Add(new ConcurrentDiffMetadataItem(
					uid,
					pairCollection,
					firstIndex,
					secondIndex));
			}
		}

		var orderedConcurrentItems = concurrentItems
			.OrderBy(static x => x.MaxCollectionIndex)
			.ThenBy(static x => x.Priority)
			.ToArray();

		var orderedConcurrentItemIndexByUid = BuildUidIndexMap(orderedConcurrentItems);
		foreach (var concurrentItem in orderedConcurrentItems) {
			concurrentItem.Index = orderedConcurrentItemIndexByUid[concurrentItem.UId];
		}

		orderedConcurrentItems = concurrentItems
			.OrderBy(static x => x.Index)
			.ThenBy(static x => x.Priority)
			.ToArray();

		var orderedItems = new List<DiffMetadataItem>();
		foreach (var concurrentItem in orderedConcurrentItems) {
			AppendRange(
				concurrentItem.PairCollections.FirstCollection,
				concurrentItem.PairCollections.FirstCurrentIndex,
				concurrentItem.FirstCollectionIndex,
				orderedItems);
			concurrentItem.PairCollections.FirstCurrentIndex = concurrentItem.FirstCollectionIndex + 1;

			AppendRange(
				concurrentItem.PairCollections.SecondCollection,
				concurrentItem.PairCollections.SecondCurrentIndex,
				concurrentItem.SecondCollectionIndex,
				orderedItems);
			concurrentItem.PairCollections.SecondCurrentIndex = concurrentItem.SecondCollectionIndex + 1;
		}

		return orderedItems;
	}

	private static IReadOnlyDictionary<string, int> BuildIndexMap(IReadOnlyList<DiffMetadataItem> items) {
		var indexByUid = new Dictionary<string, int>(StringComparer.Ordinal);
		for (var index = 0; index < items.Count; index++) {
			if (!indexByUid.ContainsKey(items[index].UId)) {
				indexByUid[items[index].UId] = index;
			}
		}

		return indexByUid;
	}

	private static void AppendRange(
		IReadOnlyList<DiffMetadataItem> items,
		int startIndex,
		int endIndex,
		ICollection<DiffMetadataItem> target) {
		if (startIndex > endIndex) {
			return;
		}
		for (var index = startIndex; index <= endIndex; index++) {
			if (index >= 0 && index < items.Count) {
				target.Add(items[index]);
			}
		}
	}

	private static bool TryParseItems(
		string content,
		out JsonObject? root,
		out IReadOnlyList<DiffMetadataItem> items)
	{
		root = null;
		items = [];
		try
		{
			root = JsonNode.Parse(content) as JsonObject;
			if (root is null || root[ItemsPropertyName] is not JsonArray itemsArray)
			{
				return false;
			}

			var parsedItems = new List<DiffMetadataItem>(itemsArray.Count);
			foreach (var itemNode in itemsArray)
			{
				if (itemNode is not JsonObject itemObject ||
					!itemObject.TryGetPropertyValue(UidPropertyName, out var uidNode) ||
					uidNode is not JsonValue uidValue ||
					!uidValue.TryGetValue<string>(out var uid) ||
					string.IsNullOrWhiteSpace(uid))
				{
					return false;
				}

				parsedItems.Add(new DiffMetadataItem(uid, itemObject));
			}

			items = parsedItems;
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private sealed class DiffMetadataItemPairCollection
	{
		public DiffMetadataItemPairCollection(
			IReadOnlyList<DiffMetadataItem> firstCollection,
			IReadOnlyList<DiffMetadataItem> secondCollection,
			int priority)
		{
			FirstCollection = firstCollection;
			SecondCollection = secondCollection;
			Priority = priority;
		}

		public IReadOnlyList<DiffMetadataItem> FirstCollection { get; }
		public IReadOnlyList<DiffMetadataItem> SecondCollection { get; }
		public int FirstCurrentIndex { get; set; }
		public int SecondCurrentIndex { get; set; }
		public int Priority { get; }
	}

	private sealed class ConcurrentDiffMetadataItem
	{
		public ConcurrentDiffMetadataItem(
			string uid,
			DiffMetadataItemPairCollection pairCollections,
			int firstCollectionIndex,
			int secondCollectionIndex)
		{
			UId = uid;
			PairCollections = pairCollections;
			FirstCollectionIndex = firstCollectionIndex;
			SecondCollectionIndex = secondCollectionIndex;
			MaxCollectionIndex = Math.Max(firstCollectionIndex, secondCollectionIndex);
			Priority = pairCollections.Priority;
		}

		public string UId { get; }
		public DiffMetadataItemPairCollection PairCollections { get; }
		public int FirstCollectionIndex { get; }
		public int SecondCollectionIndex { get; }
		public int MaxCollectionIndex { get; }
		public int Priority { get; }
		public int Index { get; set; }
	}

	private sealed record DiffMetadataItem(string UId, JsonObject Node);

	private sealed record IndexedMergedItem(int Index, int Position, DiffMetadataItem Item);
}
