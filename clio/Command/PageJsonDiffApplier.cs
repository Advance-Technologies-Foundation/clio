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
	private const string ParentNameField = "parentName";
	private const string OperationField = "operation";
	private const string IndexField = "index";
	private const string PathSeparator = "==";
	private const string NamePropSeparator = "=";
	private readonly Dictionary<string, PageJsonDiffItemInfo> _cache = new(StringComparer.Ordinal);
	private JArray _sourceObject = new();
	private PageJsonDiffApplyOptions _currentOptions = new(false);

	public JArray ApplyDiff(
		JArray sourceObject,
		IReadOnlyList<JArray> operationSets,
		IReadOnlyList<PageJsonDiffApplyOptions> operationsOptions) {
		JArray result = sourceObject.DeepClone() as JArray ?? new JArray();
		for (int index = 0; index < operationSets.Count; index++) {
			JArray operations = operationSets[index];
			PageJsonDiffApplyOptions options = index < operationsOptions.Count
				? operationsOptions[index]
				: new PageJsonDiffApplyOptions(false);
			result = Apply(result, operations, options);
		}

		return result;
	}

	private JArray Apply(JArray sourceObject, JArray operations, PageJsonDiffApplyOptions options) {
		_sourceObject = sourceObject.DeepClone() as JArray ?? new JArray();
		_cache.Clear();
		_currentOptions = options ?? new PageJsonDiffApplyOptions(false);
		try {
			IReadOnlyList<JObject> operationItems = operations.Children<JObject>().ToList();
			ApplyOperations(operationItems);
			return _sourceObject;
		}
		finally {
			_sourceObject = new JArray();
			_cache.Clear();
			_currentOptions = new PageJsonDiffApplyOptions(false);
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
			switch (operation.Value<string>(OperationField)) {
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
			.Select(operation => operation.Value<string>(NamePropertyName))
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.ToHashSet(StringComparer.Ordinal);
		List<JObject> filteredMoves = moves
			.Where(operation => !removedNames.Contains(operation.Value<string>(NamePropertyName)))
			.ToList();
		List<JObject> removesAndMoves = [..removes, ..filteredMoves];
		List<PageJsonDiffRemovalCapture> removeCaptures = removesAndMoves
			.Select(CaptureRemovalByName)
			.Where(result => result is not null)
			.ToList();
		removeCaptures = OrderOperationsByPath(
			removeCaptures,
			capture => capture.Operation,
			isAscending: false);
		Dictionary<string, List<JObject>> movesByName = GroupOperationsByName(filteredMoves);
		List<JObject> moveInserts = [];
		foreach (PageJsonDiffRemovalCapture capture in removeCaptures) {
			if (movesByName.TryGetValue(capture.Name ?? string.Empty, out List<JObject> nameMoves)) {
				foreach (JObject moveOperation in nameMoves) {
					JObject insertOperation = ConvertMoveToInsert(moveOperation, capture.Item);
					if (insertOperation is not null) {
						moveInserts.Add(insertOperation);
					}
				}
			}
			ApplyCapturedRemoval(capture);
		}
		List<JObject> allInserts = [..inserts, ..moveInserts];
		allInserts = OrderOperationsByPath(allInserts, op => op, isAscending: true);
		List<JObject> unsuccessful = ApplyInsertGroup(allInserts);
		if (unsuccessful.Count > 0) {
			ApplyUnsuccessfulInserts(unsuccessful);
		}
	}

	private PageJsonDiffRemovalCapture CaptureRemovalByName(JObject operation) {
		string name = operation.Value<string>(NamePropertyName);
		if (string.IsNullOrWhiteSpace(name)) {
			return null;
		}
		PageJsonDiffItemInfo itemInfo = FindItemByName(name);
		if (itemInfo is null) {
			return null;
		}
		return new PageJsonDiffRemovalCapture {
			Name = name,
			Item = itemInfo.Item.DeepClone(),
			ParentToken = itemInfo.ParentToken,
			PropertyName = itemInfo.PropertyName,
			Index = itemInfo.Index,
			ParentName = itemInfo.ParentName,
			Operation = operation
		};
	}

	private void ApplyCapturedRemoval(PageJsonDiffRemovalCapture capture) {
		PageJsonDiffItemInfo current = FindItemByName(capture.Name);
		JToken parentToken = current?.ParentToken ?? capture.ParentToken;
		int index = current?.Index ?? capture.Index;
		string propertyName = current?.PropertyName ?? capture.PropertyName;
		if (parentToken is JArray array && index >= 0 && index < array.Count) {
			array.RemoveAt(index);
		}
		else if (parentToken is JObject obj && propertyName is not null) {
			obj.Remove(propertyName);
		}
		_cache.Clear();
	}

	private static Dictionary<string, List<JObject>> GroupOperationsByName(IEnumerable<JObject> operations) {
		Dictionary<string, List<JObject>> result = new(StringComparer.Ordinal);
		foreach (JObject operation in operations) {
			string name = operation.Value<string>(NamePropertyName);
			if (string.IsNullOrWhiteSpace(name)) {
				continue;
			}
			if (!result.TryGetValue(name, out List<JObject> list)) {
				list = [];
				result[name] = list;
			}
			list.Add(operation);
		}
		return result;
	}

	private static JObject ConvertMoveToInsert(JObject moveOperation, JToken capturedItem) {
		if (capturedItem is null) {
			return null;
		}
		JObject insertOperation = (JObject)moveOperation.DeepClone();
		insertOperation[OperationField] = "insert";
		JToken values = capturedItem.DeepClone();
		if (values is JObject valuesObject) {
			valuesObject.Remove(NamePropertyName);
		}
		insertOperation[ValuesPropertyName] = values;
		return insertOperation;
	}

	private List<JObject> ApplyInsertGroup(IReadOnlyList<JObject> operations) {
		List<JObject> unsuccessful = [];
		foreach (JObject operation in operations) {
			if (!TryInsert(operation)) {
				unsuccessful.Add(operation);
			}
		}
		return unsuccessful;
	}

	private void ApplyUnsuccessfulInserts(List<JObject> unsuccessful) {
		List<JObject> retry = [];
		foreach (JObject operation in unsuccessful) {
			string parentName = operation.Value<string>(ParentNameField);
			if (string.IsNullOrWhiteSpace(parentName) || FindItemByName(parentName) is not null) {
				JObject moveForm = (JObject)operation.DeepClone();
				moveForm[OperationField] = "move";
				retry.Add(moveForm);
			}
		}
		if (retry.Count == 0) {
			return;
		}
		ApplyChangePositionOperations([], unsuccessful, retry);
	}

	private bool TryInsert(JObject operation) {
		JToken parent = ResolveInsertParent(operation).Token;
		if (parent is null) {
			return false;
		}

		JToken item = operation[ValuesPropertyName]?.DeepClone() ?? new JObject();
		if (!string.IsNullOrWhiteSpace(operation.Value<string>(NamePropertyName)) && item is JObject itemObject) {
			itemObject[NamePropertyName] = operation.Value<string>(NamePropertyName);
		}

		if (parent is JArray array) {
			int requestedIndex = operation[IndexField]?.Value<int>() ?? array.Count;
			int index = Math.Clamp(requestedIndex, 0, array.Count);
			array.Insert(index, item);
			_cache.Clear();
			return true;
		}

		if (parent is JObject obj && operation.Value<string>(PropertyNameField) is string propertyName) {
			obj[propertyName] = item;
			_cache.Clear();
			return true;
		}

		return false;
	}

	private void Insert(JObject operation) => TryInsert(operation);

	private PageJsonPathResolution ResolveInsertParent(JObject operation) {
		IReadOnlyList<object> path = PageBundleMergeHelpers.GetPathSegments(operation);
		string parentName = operation.Value<string>(ParentNameField);
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

	private List<T> OrderOperationsByPath<T>(
		IReadOnlyList<T> items,
		Func<T, JObject> operationSelector,
		bool isAscending) {
		if (items.Count == 0) {
			return [];
		}
		Dictionary<string, string> hierarchy = BuildOperationHierarchy(items.Select(operationSelector));
		int sortOrder = isAscending ? 1 : -1;
		var annotated = items
			.Select(item => new {
				Item = item,
				Path = GetOperationItemPath(operationSelector(item).Value<string>(NamePropertyName), hierarchy),
				Index = operationSelector(item)[IndexField]?.Value<int>() ?? int.MaxValue
			})
			.ToList();
		var pathOrder = new List<string>();
		var groups = new Dictionary<string, List<(T Item, int Index)>>(StringComparer.Ordinal);
		foreach (var entry in annotated) {
			if (!groups.TryGetValue(entry.Path, out List<(T, int)> list)) {
				list = [];
				groups[entry.Path] = list;
				pathOrder.Add(entry.Path);
			}
			list.Add((entry.Item, entry.Index));
		}
		pathOrder.Sort((a, b) => {
			if (a.Length == b.Length) {
				return 0;
			}
			return a.Length > b.Length ? sortOrder : -sortOrder;
		});
		var result = new List<T>(items.Count);
		foreach (string path in pathOrder) {
			List<(T Item, int Index)> pathItems = groups[path];
			pathItems.Sort((a, b) => a.Index.CompareTo(b.Index));
			if (!isAscending) {
				pathItems.Reverse();
			}
			foreach ((T item, int _) in pathItems) {
				result.Add(item);
			}
		}
		return result;
	}

	private static Dictionary<string, string> BuildOperationHierarchy(IEnumerable<JObject> operations) {
		Dictionary<string, string> result = new(StringComparer.Ordinal);
		foreach (JObject operation in operations) {
			string name = operation.Value<string>(NamePropertyName);
			if (string.IsNullOrWhiteSpace(name)) {
				continue;
			}
			string parentName = operation.Value<string>(ParentNameField) ?? "_";
			string propertyName = operation.Value<string>(PropertyNameField) ?? "_";
			result[name] = parentName + NamePropSeparator + propertyName;
		}
		return result;
	}

	private string GetOperationItemPath(string name, IReadOnlyDictionary<string, string> hierarchy) {
		if (string.IsNullOrWhiteSpace(name)) {
			return string.Empty;
		}
		HashSet<string> visited = new(StringComparer.Ordinal);
		return _currentOptions.ApplyMoveIfIndirectParentMoved
			? GetOperationItemFullPath(name, hierarchy, visited, string.Empty)
			: GetOperationItemRelativePath(name, hierarchy, visited, string.Empty);
	}

	private static string GetOperationItemRelativePath(
		string name,
		IReadOnlyDictionary<string, string> hierarchy,
		HashSet<string> visited,
		string accumulator) {
		if (!hierarchy.TryGetValue(name, out string parentEntry) || !visited.Add(name)) {
			return accumulator;
		}
		string[] parts = parentEntry.Split(NamePropSeparator, 2);
		string parentName = parts[0];
		string result = parentEntry + PathSeparator + accumulator;
		if (string.IsNullOrWhiteSpace(parentName) || parentName == "_") {
			return result;
		}
		return GetOperationItemRelativePath(parentName, hierarchy, visited, result);
	}

	private string GetOperationItemFullPath(
		string name,
		IReadOnlyDictionary<string, string> hierarchy,
		HashSet<string> visited,
		string accumulator) {
		if (!visited.Add(name)) {
			return accumulator;
		}
		string parentName;
		string propertyName;
		if (hierarchy.TryGetValue(name, out string parentEntry)) {
			string[] parts = parentEntry.Split(NamePropSeparator, 2);
			parentName = parts[0];
			propertyName = parts.Length > 1 ? parts[1] : "_";
		} else {
			PageJsonDiffItemInfo sourceInfo = FindItemByName(name);
			if (sourceInfo is null) {
				return accumulator;
			}
			parentName = sourceInfo.ParentName;
			propertyName = sourceInfo.PropertyName ?? "_";
		}
		if (string.IsNullOrWhiteSpace(parentName) || parentName == "_") {
			return accumulator;
		}
		string result = parentName + NamePropSeparator + propertyName + PathSeparator + accumulator;
		return GetOperationItemFullPath(parentName, hierarchy, visited, result);
	}

	private void Set(JObject operation) {
		PageJsonDiffItemInfo itemInfo = FindItem(operation);
		if (itemInfo is null) {
			return;
		}

		JObject insertOperation = (JObject)operation.DeepClone();
		insertOperation[OperationField] = "insert";
		if (itemInfo.ParentToken is JArray) {
			insertOperation[IndexField] = itemInfo.Index;
			if (!string.IsNullOrWhiteSpace(itemInfo.ParentName)) {
				insertOperation[ParentNameField] = itemInfo.ParentName;
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
		if (current is JArray currentArray && currentArray.First is JObject currentItem && currentItem[NamePropertyName] is not null) {
			return replacement is not null;
		}

		if (current is JObject currentObject && currentObject[NamePropertyName] is not null) {
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
		string name = operation.Value<string>(NamePropertyName);
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

	private sealed class PageJsonDiffRemovalCapture {
		public string Name { get; init; }

		public JToken Item { get; init; }

		public JToken ParentToken { get; init; }

		public string PropertyName { get; init; }

		public int Index { get; init; }

		public string ParentName { get; init; }

		public JObject Operation { get; init; }
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
