namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

internal sealed record PageJsonDiffApplyOptions(bool ApplyMoveIfIndirectParentMoved);

internal interface IPageJsonDiffApplier {
	JArray ApplyDiff(JArray sourceObject, IReadOnlyList<JArray> operationSets, IReadOnlyList<PageJsonDiffApplyOptions> operationsOptions);
}

internal sealed class PageJsonDiffApplier : IPageJsonDiffApplier {
	private const string NamePropertyName = "name";
	private const string PropertyNameField = "propertyName";
	private const string ValuesPropertyName = "values";
	private readonly Dictionary<string, PageJsonDiffItemInfo> _cache = new(StringComparer.Ordinal);
	private JArray _sourceObject = new();

	public JArray ApplyDiff(
		JArray sourceObject,
		IReadOnlyList<JArray> operationSets,
		IReadOnlyList<PageJsonDiffApplyOptions> operationsOptions) {
		JArray result = sourceObject.DeepClone() as JArray ?? new JArray();
		for (int index = 0; index < operationSets.Count; index++) {
			JArray operations = operationSets[index];
			result = Apply(result, operations);
		}

		return result;
	}

	private JArray Apply(JArray sourceObject, JArray operations) {
		_sourceObject = sourceObject.DeepClone() as JArray ?? new JArray();
		_cache.Clear();
		try {
			IReadOnlyList<JObject> operationItems = operations.Children<JObject>().ToList();
			ApplyOperations(operationItems);
			return _sourceObject;
		}
		finally {
			_sourceObject = new JArray();
			_cache.Clear();
		}
	}

	private void ApplyOperations(IReadOnlyList<JObject> operations) {
		List<JObject> merge = [];
		List<JObject> set = [];
		List<JObject> removeProperties = [];
		List<JObject> remove = [];
		List<JObject> move = [];
		List<JObject> insert = [];

		foreach (JObject operation in operations) {
			switch (operation.Value<string>("operation")) {
				case "merge":
					merge.Add(operation);
					break;
				case "set":
					set.Add(operation);
					break;
				case "insert":
					insert.Add(operation);
					break;
				case "move":
					move.Add(operation);
					break;
				case "remove":
					if (operation["properties"] is JArray properties && properties.Count > 0) {
						removeProperties.Add(operation);
					}
					else {
						remove.Add(operation);
					}

					break;
			}
		}

		foreach (JObject operation in merge) {
			Merge(operation);
		}

		ApplyChangePositionOperations(remove, insert, move);

		foreach (JObject operation in removeProperties) {
			Remove(operation);
		}

		foreach (JObject operation in set) {
			Set(operation);
		}
	}

	private void ApplyChangePositionOperations(
		IReadOnlyList<JObject> removes,
		IReadOnlyList<JObject> inserts,
		IReadOnlyList<JObject> moves) {
		HashSet<string> removedNames = removes
			.Select(operation => operation.Value<string>("name"))
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.ToHashSet(StringComparer.Ordinal);
		List<JObject> moveOperations = moves
			.Where(operation => !removedNames.Contains(operation.Value<string>("name")))
			.ToList();
		List<PageJsonDiffRemovalResult> moveRemovalResults = moveOperations
			.Select(CaptureRemoval)
			.Where(result => result is not null)
			.ToList();
		List<PageJsonDiffRemovalResult> removeResults = removes
			.Select(CaptureRemoval)
			.Where(result => result is not null)
			.Concat(moveRemovalResults)
			.ToList();
		foreach (PageJsonDiffRemovalResult removal in removeResults
			         .OrderByDescending(result => result.Depth)
			         .ThenByDescending(result => result.Index)) {
			RemoveByResult(removal);
		}

		List<PageJsonDiffInsertOperation> insertOperations = inserts
			.Select(operation => PageJsonDiffInsertOperation.Create(operation))
			.Concat(moveRemovalResults.Select(PageJsonDiffInsertOperation.CreateFromMove))
			.ToList();
		foreach (PageJsonDiffInsertOperation insertOperation in insertOperations
			         .OrderBy(operation => operation.Depth)
			         .ThenBy(operation => operation.Index)) {
			Insert(insertOperation.Operation);
		}
	}

	private PageJsonDiffRemovalResult CaptureRemoval(JObject operation) {
		PageJsonDiffItemInfo itemInfo = FindItem(operation);
		if (itemInfo is null) {
			return null;
		}

		return new PageJsonDiffRemovalResult {
			Name = operation.Value<string>("name"),
			Item = itemInfo.Item.DeepClone(),
			ParentToken = itemInfo.ParentToken,
			PropertyName = itemInfo.PropertyName,
			Index = itemInfo.Index,
			Depth = itemInfo.Depth,
			Operation = operation
		};
	}

	private void RemoveByResult(PageJsonDiffRemovalResult removal) {
		PageJsonDiffItemInfo current = !string.IsNullOrWhiteSpace(removal.Name)
			? FindItemByName(removal.Name)
			: null;
		JToken parentToken = current?.ParentToken ?? removal.ParentToken;
		int index = current?.Index ?? removal.Index;
		if (parentToken is JArray array && index >= 0 && index < array.Count) {
			array.RemoveAt(index);
		}
		else if (parentToken is JObject obj && (current?.PropertyName ?? removal.PropertyName) is not null) {
			obj.Remove(current?.PropertyName ?? removal.PropertyName);
		}

		_cache.Clear();
	}

	private void Insert(JObject operation) {
		JToken parent = ResolveInsertParent(operation).Token;
		if (parent is null) {
			return;
		}

		JToken item = operation[ValuesPropertyName]?.DeepClone() ?? new JObject();
		if (!string.IsNullOrWhiteSpace(operation.Value<string>(NamePropertyName)) && item is JObject itemObject) {
			itemObject[NamePropertyName] = operation.Value<string>(NamePropertyName);
		}

		if (parent is JArray array) {
			int requestedIndex = operation["index"]?.Value<int>() ?? array.Count;
			int index = Math.Clamp(requestedIndex, 0, array.Count);
			array.Insert(index, item);
			_cache.Clear();
			return;
		}

		if (parent is JObject obj && operation.Value<string>(PropertyNameField) is string propertyName) {
			obj[propertyName] = item;
			_cache.Clear();
		}
	}

	private PageJsonPathResolution ResolveInsertParent(JObject operation) {
		IReadOnlyList<object> path = PageBundleMergeHelpers.GetPathSegments(operation);
		string parentName = operation.Value<string>("parentName");
		if (!string.IsNullOrWhiteSpace(parentName)) {
			PageJsonDiffItemInfo itemInfo = FindItemByName(parentName);
			if (itemInfo is null) {
				return PageJsonPathResolution.Empty;
			}

			if (path.Count > 0) {
				return ResolvePath(itemInfo.Item, path);
			}

			if (operation.Value<string>(PropertyNameField) is string propertyName && itemInfo.Item[propertyName] is JToken directChild) {
				return new PageJsonPathResolution(directChild, itemInfo.Item, propertyName, 0, itemInfo.Depth + 1);
			}

			return new PageJsonPathResolution(itemInfo.Item, itemInfo.ParentToken, itemInfo.PropertyName, itemInfo.Index, itemInfo.Depth);
		}

		return path.Count > 0
			? ResolvePath(_sourceObject, path)
			: new PageJsonPathResolution(_sourceObject, null, null, 0, 0);
	}

	private void Set(JObject operation) {
		PageJsonDiffItemInfo itemInfo = FindItem(operation);
		if (itemInfo is null) {
			return;
		}

		JObject insertOperation = (JObject)operation.DeepClone();
		insertOperation["operation"] = "insert";
		if (itemInfo.ParentToken is JArray) {
			insertOperation["index"] = itemInfo.Index;
			if (!string.IsNullOrWhiteSpace(itemInfo.ParentName)) {
				insertOperation["parentName"] = itemInfo.ParentName;
			}
			if (!string.IsNullOrWhiteSpace(itemInfo.PropertyName)) {
				insertOperation[PropertyNameField] = itemInfo.PropertyName;
			}
		}
		else if (itemInfo.ParentToken is JObject && !string.IsNullOrWhiteSpace(itemInfo.PropertyName)) {
			insertOperation[PropertyNameField] = itemInfo.PropertyName;
		}

		Remove(operation);
		Insert(insertOperation);
	}

	private void Merge(JObject operation) {
		PageJsonDiffItemInfo itemInfo = FindItem(operation);
		if (itemInfo?.Item is not JObject target || operation[ValuesPropertyName] is not JObject values) {
			return;
		}

		foreach (JProperty property in values.Properties()) {
			if (ShouldSkipMerge(target[property.Name], property.Value)) {
				continue;
			}

			target[property.Name] = property.Value.DeepClone();
		}
	}

	private static bool ShouldSkipMerge(JToken current, JToken replacement) {
		if (current is JArray currentArray && currentArray.First is JObject currentItem && currentItem["name"] is not null) {
			return replacement is not null;
		}

		if (current is JObject currentObject && currentObject["name"] is not null) {
			return replacement is not null;
		}

		return false;
	}

	private void Remove(JObject operation) {
		PageJsonDiffItemInfo itemInfo = FindItem(operation);
		if (itemInfo is null) {
			return;
		}

		if (operation["properties"] is JArray properties && itemInfo.Item is JObject targetObject) {
			foreach (string property in properties.Values<string>()) {
				targetObject.Remove(property);
			}

			return;
		}

		if (itemInfo.ParentToken is JArray array) {
			array.RemoveAt(itemInfo.Index);
		}
		else if (itemInfo.ParentToken is JObject parentObject && itemInfo.PropertyName is not null) {
			parentObject.Remove(itemInfo.PropertyName);
		}
		else {
			return;
		}

		_cache.Clear();
	}

	private PageJsonDiffItemInfo FindItem(JObject operation) {
		string name = operation.Value<string>("name");
		IReadOnlyList<object> path = PageBundleMergeHelpers.GetPathSegments(operation);
		if (!string.IsNullOrWhiteSpace(name)) {
			PageJsonDiffItemInfo namedItem = FindItemByName(name);
			if (namedItem is null) {
				return null;
			}

			if (path.Count == 0) {
				return namedItem;
			}

			PageJsonPathResolution resolution = ResolvePath(namedItem.Item, path);
			return resolution.Token is null
				? null
				: new PageJsonDiffItemInfo(
					resolution.Token,
					resolution.Parent,
					resolution.PropertyName,
					resolution.Index,
					resolution.Depth,
					namedItem.ParentName);
		}

		if (path.Count == 0) {
			return null;
		}

		PageJsonPathResolution rootResolution = ResolvePath(_sourceObject, path);
		return rootResolution.Token is null
			? null
			: new PageJsonDiffItemInfo(
				rootResolution.Token,
				rootResolution.Parent,
				rootResolution.PropertyName,
				rootResolution.Index,
				rootResolution.Depth,
				null);
	}

	private PageJsonDiffItemInfo FindItemByName(string name) {
		if (_cache.TryGetValue(name, out PageJsonDiffItemInfo cachedItem)) {
			return cachedItem;
		}

		PageJsonDiffItemInfo result = null;
		TraverseItems(_sourceObject, null, null, 0, null, info => {
			string itemName = info.Item.Value<string>(NamePropertyName);
			if (string.Equals(itemName, name, StringComparison.Ordinal)) {
				_cache[name] = info;
				result = info;
				return false;
			}

			if (!string.IsNullOrWhiteSpace(itemName)) {
				_cache[itemName] = info;
			}
			return true;
		});
		return result;
	}

	private void TraverseItems(
		JToken token,
		JToken parentToken,
		string propertyName,
		int depth,
		string parentName,
		Func<PageJsonDiffItemInfo, bool> visitor) {
		if (token is not JContainer) {
			return;
		}

		if (token is JArray array) {
			for (int index = 0; index < array.Count; index++) {
				if (array[index] is not JObject child || child[NamePropertyName] is null) {
					continue;
				}

				var info = new PageJsonDiffItemInfo(child, array, propertyName, index, depth + 1, parentName);
				if (!visitor(info)) {
					return;
				}

				TraverseChildProperties(child, depth + 1, child.Value<string>(NamePropertyName), visitor);
			}

			return;
		}

		if (token is JObject obj && obj[NamePropertyName] is not null) {
			var info = new PageJsonDiffItemInfo(obj, parentToken, propertyName, 0, depth, parentName);
			if (!visitor(info)) {
				return;
			}

			TraverseChildProperties(obj, depth, obj.Value<string>(NamePropertyName), visitor);
		}
	}

	private void TraverseChildProperties(
		JObject obj,
		int depth,
		string parentName,
		Func<PageJsonDiffItemInfo, bool> visitor) {
		foreach (JProperty property in obj.Properties()) {
			if (property.Value is JArray childArray && childArray.First is JObject childObject && childObject[NamePropertyName] is not null) {
				TraverseItems(childArray, obj, property.Name, depth, parentName, visitor);
				continue;
			}

			if (property.Value is JObject childItem && childItem[NamePropertyName] is not null) {
				TraverseItems(childItem, obj, property.Name, depth + 1, parentName, visitor);
			}
		}
	}

	private static PageJsonPathResolution ResolvePath(JToken source, IReadOnlyList<object> path) {
		JToken current = source;
		JToken parent = null;
		string propertyName = null;
		int index = 0;
		int depth = 0;
		foreach (object segment in path) {
			parent = current;
			depth++;
			switch (segment) {
				case int arrayIndex when current is JArray array:
					if (arrayIndex < 0 || arrayIndex >= array.Count) {
						return PageJsonPathResolution.Empty;
					}

					index = arrayIndex;
					propertyName = null;
					current = array[arrayIndex];
					break;
				case string key when current is JObject obj:
					propertyName = key;
					current = obj[key];
					break;
				default:
					return PageJsonPathResolution.Empty;
			}

			if (current is null) {
				return PageJsonPathResolution.Empty;
			}
		}

		return new PageJsonPathResolution(current, parent, propertyName, index, depth);
	}

	private sealed record PageJsonDiffItemInfo(
		JToken Item,
		JToken ParentToken,
		string PropertyName,
		int Index,
		int Depth,
		string ParentName);

	private sealed class PageJsonDiffRemovalResult {
		public string Name { get; init; }

		public JToken Item { get; init; }

		public JToken ParentToken { get; init; }

		public string PropertyName { get; init; }

		public int Index { get; init; }

		public int Depth { get; init; }

		public JObject Operation { get; init; }
	}

	private sealed class PageJsonDiffInsertOperation {
		public JObject Operation { get; init; }

		public int Depth { get; init; }

		public int Index { get; init; }

		public static PageJsonDiffInsertOperation Create(JObject operation) {
			IReadOnlyList<object> path = PageBundleMergeHelpers.GetPathSegments(operation);
			return new PageJsonDiffInsertOperation {
				Operation = (JObject)operation.DeepClone(),
				Depth = path.Count + (!string.IsNullOrWhiteSpace(operation.Value<string>("parentName")) ? 1 : 0),
				Index = operation["index"]?.Value<int>() ?? int.MaxValue
			};
		}

		public static PageJsonDiffInsertOperation CreateFromMove(PageJsonDiffRemovalResult removal) {
			JObject operation = (JObject)removal.Operation.DeepClone();
			operation["operation"] = "insert";
			operation["values"] = removal.Item.DeepClone();
			if (operation["values"] is JObject item) {
				item.Remove("name");
			}

			return Create(operation);
		}
	}

	private sealed record PageJsonPathResolution(
		JToken Token,
		JToken Parent,
		string PropertyName,
		int Index,
		int Depth) {
		public static PageJsonPathResolution Empty { get; } = new(null, null, null, 0, 0);
	}
}
