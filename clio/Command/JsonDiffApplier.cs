namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

/// <summary>
/// Faithful C# clone of the Creatio client-side <c>JsonApplierService</c>
/// (<c>creatio-ui/.../services/json-applier/json-applier.service.ts</c>). It applies a Freedom UI
/// <c>viewConfigDiff</c>-style operation set (merge / set / insert / move / remove) to a source items tree,
/// reproducing the client semantics 1:1 — INCLUDING the exceptions the client throws
/// (<see cref="JsonDiffApplierException"/>: not-a-container, cyclic dependency, required-parameter-missing).
/// Unlike <see cref="PageJsonDiffApplier"/> (a tolerant, read-time merge that silently creates missing slots),
/// this clone surfaces the same errors the server raises, so it can later back a faithful diff validator.
/// Data model is Newtonsoft <see cref="JToken"/>; tokens moved into the tree are deep-cloned to avoid the
/// single-parent constraint (result-identical to the TS reference-assignment).
/// </summary>
// This type is a deliberate 1:1 port of the client TS JsonApplierService. Its method complexity, member
// visibility, string keys and control flow mirror the reference so the clone stays verifiable against it;
// refactoring them (extracting methods, encapsulating fields, LINQ-ifying loops) would diverge from the
// source of truth this validator exists to reproduce. Maintainability smells are suppressed for that reason.
[SuppressMessage("Critical Code Smell", "S3776:Cognitive Complexity of methods should not be too high", Justification = "1:1 clone of the client TS JsonApplierService — structure mirrors the reference for fidelity.")]
[SuppressMessage("Minor Code Smell", "S1104:Fields should not have public accessibility", Justification = "1:1 clone of the client TS JsonApplierService — mirrors the reference's item-info shape.")]
[SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "1:1 clone of the client TS JsonApplierService — instance methods mirror the reference and are overridden by JsonPathDiffApplier.")]
[SuppressMessage("Minor Code Smell", "S1192:String literals should not be duplicated", Justification = "1:1 clone of the client TS JsonApplierService — operation/property keys mirror the reference verbatim.")]
[SuppressMessage("Major Code Smell", "S3358:Ternary operators should not be nested", Justification = "1:1 clone of the client TS JsonApplierService — expression mirrors the reference.")]
[SuppressMessage("Minor Code Smell", "S3267:Loops should be simplified with LINQ", Justification = "1:1 clone of the client TS JsonApplierService — explicit loops mirror the reference control flow.")]
[SuppressMessage("Major Code Smell", "S1168:Empty arrays and collections should be returned instead of null", Justification = "1:1 clone of the client TS JsonApplierService — null returns mirror the reference semantics.")]
public class JsonDiffApplier {

	private readonly bool _disableApplyMoveIfIndirectParentMoved;
	private readonly Dictionary<string, ItemInfo> _memoryStore = new(StringComparer.Ordinal);
	private readonly Dictionary<string, IReadOnlyList<string>> _operationRequiredParameters = new(StringComparer.Ordinal) {
		["insert"] = [],
		["set"] = ["name", "values"],
		["merge"] = ["name", "values"],
		["move"] = ["name"],
		["remove"] = ["name"],
	};
	private const string OperationParameterName = "operation";

	/// <summary>The element identity property — <c>"name"</c> for the view-config applier, overridden to
	/// <c>"_id"</c> by <see cref="JsonPathDiffApplier"/> (mirrors <c>properties.alias.name</c>).</summary>
	protected virtual string AliasName => "name";

	/// <summary>Whether per-operation required parameters are enforced (true for the base view-config applier;
	/// the path applier overrides to false — mirrors <c>checkOperationNameRequiredParameters</c>).</summary>
	protected virtual bool CheckOperationNameRequiredParameters => true;

	protected JToken _sourceObject;
	private JObject _rootWrapper;
	private JsonApplierOperationsOptions _operationsOptions;
	private Dictionary<string, JsonApplierAliasInfo> _aliases;

	/// <param name="disableApplyMoveIfIndirectParentMoved">Mirrors the client
	/// <c>FEATURE_VALUES.DisableApplyMoveIfIndirectParentMoved</c> flag: when <c>true</c>, move ordering uses the
	/// relative path even if <see cref="JsonApplierOperationsOptions.ApplyMoveIfIndirectParentMoved"/> is set.</param>
	public JsonDiffApplier(bool disableApplyMoveIfIndirectParentMoved = false) {
		_disableApplyMoveIfIndirectParentMoved = disableApplyMoveIfIndirectParentMoved;
	}

	// ----- public API (mirrors TS apply / applyDiff) -----

	public JToken Apply(JToken sourceObject, JArray operations, JsonApplierOperationsOptions operationsOptions = null) {
		JToken result;
		_sourceObject = sourceObject?.DeepClone();
		_operationsOptions = operationsOptions;
		_rootWrapper = new JObject();
		if (_sourceObject is not null) {
			_rootWrapper["items"] = _sourceObject;
		}
		if (IsEmpty(sourceObject)) {
			ResetAliases();
		}
		try {
			var innerOperations = (JArray)(operations?.DeepClone() ?? new JArray());
			ApplyOperations(innerOperations);
			result = _sourceObject;
		} finally {
			_sourceObject = null;
			_operationsOptions = null;
			_memoryStore.Clear();
			_rootWrapper = null;
		}
		return result;
	}

	public JToken ApplyDiff(
		JArray sourceObject,
		IReadOnlyList<JArray> operations,
		IReadOnlyList<JsonApplierOperationsOptions> operationsOptions = null) {
		JToken result = sourceObject?.DeepClone() ?? new JArray();
		for (int i = 0; i < operations.Count; i++) {
			JsonApplierOperationsOptions options = operationsOptions is not null && i < operationsOptions.Count
				? operationsOptions[i]
				: null;
			result = Apply(result, operations[i], options);
		}
		return result;
	}

	// ----- static predicates (mirror TS) -----

	public static bool IsItemConfig(JToken config) =>
		config is JObject obj && !IsEmpty(obj["name"]);

	protected static bool IsEmpty(object value) {
		switch (value) {
			case null:
				return true;
			case string s:
				return s.Length == 0;
			case JToken token:
				if (token.Type is JTokenType.Null or JTokenType.Undefined) {
					return true;
				}
				if (token.Type == JTokenType.String) {
					return token.Value<string>()!.Length == 0;
				}
				return token is JArray array && array.Count == 0;
			case System.Collections.ICollection collection:
				return collection.Count == 0;
			default:
				return false;
		}
	}

	private static bool IsEmptyObject(JObject value) => value is null || value.Count == 0;

	// Mirrors JS truthiness for operation-method results (bool / JObject / null).
	private static bool IsTruthy(object value) => value switch {
		null => false,
		bool b => b,
		JToken t => t.Type is not (JTokenType.Null or JTokenType.Undefined),
		_ => true,
	};

	// Mirrors JS `!value` falsiness for required-parameter presence checks.
	private static bool IsFalsy(JToken token) {
		if (token is null || token.Type is JTokenType.Null or JTokenType.Undefined) {
			return true;
		}
		return token.Type switch {
			JTokenType.String => token.Value<string>()!.Length == 0,
			JTokenType.Boolean => !token.Value<bool>(),
			JTokenType.Integer => token.Value<long>() == 0,
			JTokenType.Float => token.Value<double>() == 0d,
			_ => false,
		};
	}

	private static string Format(string template, params object[] args) =>
		string.Format(CultureInfo.InvariantCulture, template, args);

	// ----- tree traversal (mirror iterateChildItems) -----

	protected sealed class IterationConfig {
		public JObject Item;
		public string PropertyName;
		public JToken Parent;
		public bool ChildIterationResult;
		public object[] ParentPath;
	}

	protected virtual bool IterateChildItems(JToken config, Func<IterationConfig, bool> iterator) {
		bool result = true;
		if (config is not JObject obj) {
			return result;
		}
		foreach (JProperty propertyEntry in obj.Properties().ToList()) {
			JToken property = propertyEntry.Value;
			if (property is JArray or JObject) {
				IList<JToken> items = property is JArray arr ? arr : new List<JToken> { property };
				foreach (JToken childItem in items) {
					if (!IsItemConfig(items[0])) {
						break; // inner-each return false: stop this property, result unchanged
					}
					bool childIterationResult = IterateChildItems(childItem, iterator);
					var iterationConfig = new IterationConfig {
						Item = childItem as JObject,
						PropertyName = propertyEntry.Name,
						Parent = config,
						ChildIterationResult = childIterationResult,
					};
					result = iterator(iterationConfig) && childIterationResult;
					if (!result) {
						break; // inner-each return false
					}
				}
			}
			if (!result) {
				break; // outer-each return false
			}
		}
		return result;
	}

	// ----- item lookup + memory store (mirror _findItemInfo) -----

	protected ItemInfo FindItemInfo(string name, JToken parent) {
		if (IsEmpty(name)) {
			return null;
		}
		if (_memoryStore.TryGetValue(name, out ItemInfo cachedItem)) {
			return cachedItem;
		}
		JsonApplierAliasInfo alias = _aliases is not null && _aliases.TryGetValue(name, out JsonApplierAliasInfo a) ? a : null;
		string aliasName = alias?.Name;
		ItemInfo result = null;
		IterateChildItems(parent, iterationConfig => {
			JObject item = iterationConfig.Item;
			string itemName = item?[AliasName]?.Value<string>();
			bool isAliasFound = !string.IsNullOrEmpty(aliasName) && itemName == aliasName;
			bool isItemFound = itemName == name || isAliasFound;
			if (!iterationConfig.ChildIterationResult || isItemFound) {
				string parentName = (iterationConfig.Parent as JObject)?[AliasName]?.Value<string>();
				var currentItemInfo = new ItemInfo {
					Item = item,
					ParentName = parentName,
					ParentPath = iterationConfig.ParentPath,
					PropertyName = iterationConfig.PropertyName,
				};
				if (itemName is not null) {
					_memoryStore[itemName] = currentItemInfo;
				}
				if (isItemFound) {
					result = currentItemInfo;
				}
			}
			return !isItemFound;
		});
		return result;
	}

	protected virtual ItemInfo FindItemInfoInSourceObject(string name) => FindItemInfo(name, _rootWrapper);

	private void RemoveFromCache(JToken item) {
		IterateChildItems(item, iterationConfig => {
			string childName = iterationConfig.Item?[AliasName]?.Value<string>();
			if (childName is not null) {
				_memoryStore.Remove(childName);
			}
			return true;
		});
		string itemName = item[AliasName]?.Value<string>();
		if (itemName is not null) {
			_memoryStore.Remove(itemName);
		}
	}

	// ----- operation dispatch (mirror _applyOperations) -----

	private void ApplyOperations(JArray operations) {
		SplittedOperations splitted = GetSplittedOperations(operations);
		ApplyOperationGroup(op => Merge(op), splitted.Merge);
		ApplyChangePositionOperationGroup(splitted.Remove, splitted.Insert, splitted.Move);
		ApplyOperationGroup(op => Remove(op), splitted.RemoveProperties);
		ApplyOperationGroup(op => Set(op), splitted.Set);
	}

	private sealed class SplittedOperations {
		public readonly List<JObject> Merge = [];
		public readonly List<JObject> Set = [];
		public readonly List<JObject> RemoveProperties = [];
		public readonly List<JObject> Remove = [];
		public readonly List<JObject> Move = [];
		public readonly List<JObject> Insert = [];
	}

	private SplittedOperations GetSplittedOperations(JArray operations) {
		var result = new SplittedOperations();
		foreach (JToken operationToken in operations) {
			if (operationToken is not JObject operationItem) {
				continue;
			}
			CheckOperation(operationItem);
			string operation = operationItem.Value<string>(OperationParameterName);
			if (IsExcludeAliasOperation(operationItem.Value<string>("name"), operation, operationItem["properties"])) {
				continue;
			}
			switch (operation) {
				case "merge":
					result.Merge.Add(operationItem);
					break;
				case "set":
					result.Set.Add(operationItem);
					break;
				case "insert":
					result.Insert.Add(operationItem);
					break;
				case "move":
					result.Move.Add(operationItem);
					break;
				case "remove":
					if (operationItem["properties"] is JArray) {
						result.RemoveProperties.Add(operationItem);
					} else {
						result.Remove.Add(operationItem);
					}
					break;
			}
		}
		return result;
	}

	private List<JObject> ApplyOperationGroup(Func<JObject, object> operationMethod, List<JObject> operations) {
		var unsuccessful = new List<JObject>();
		foreach (JObject operationItem in operations) {
			if (!IsTruthy(operationMethod(operationItem))) {
				unsuccessful.Add(operationItem);
			}
		}
		return unsuccessful;
	}

	// ----- change-position (remove / insert / move) pipeline -----

	private void ApplyChangePositionOperationGroup(List<JObject> removes, List<JObject> inserts, List<JObject> moves, int previousUnsuccessfulCount = int.MaxValue) {
		moves = FilterMoveOperation(removes, moves);
		var removesAndMoves = new List<JObject>(removes);
		removesAndMoves.AddRange(moves);
		var removeOperations = new List<JObject>();
		foreach (JObject operationItem in removesAndMoves) {
			JObject removeOperation = ConvertMoveOperationToRemove(operationItem);
			if (!IsEmptyObject(removeOperation)) {
				removeOperations.Add(removeOperation);
			}
		}
		removeOperations = GetOperationsSequenceByPath(removeOperations, isAsc: false);
		var insertOperations = new List<JObject>();
		Dictionary<string, List<JObject>> objectNameOperationsGroup = GetObjectNameOperationsGroup(moves);
		foreach (JObject operationItem in removeOperations) {
			string opName = operationItem.Value<string>("name") ?? string.Empty;
			if (objectNameOperationsGroup.TryGetValue(opName, out List<JObject> objectNameOperations)) {
				foreach (JObject moveOperation in objectNameOperations) {
					JObject insertOperation = ConvertMoveOperationToInsert(moveOperation);
					if (!IsEmptyObject(insertOperation)) {
						insertOperations.Add(insertOperation);
					}
				}
			}
			Remove(operationItem);
		}
		var allInserts = new List<JObject>(inserts);
		allInserts.AddRange(insertOperations);
		allInserts = GetOperationsSequenceByPath(allInserts, isAsc: true);
		List<JObject> unsuccessful = ApplyOperationGroup(op => Insert(op), allInserts);
		if (unsuccessful.Count > 0) {
			ApplyUnsuccessfulInsertOperationGroup(unsuccessful, previousUnsuccessfulCount);
		}
	}

	private void ApplyUnsuccessfulInsertOperationGroup(List<JObject> unsuccessful, int previousUnsuccessfulCount = int.MaxValue) {
		// Mirror the TS forEach+splice (mutating during iteration): setting operation='move' on each, dropping
		// entries whose parent is still missing. The index does not rewind after a removal — matching forEach.
		for (int index = 0; index < unsuccessful.Count; index++) {
			unsuccessful[index]["operation"] = "move";
			if (FindItemInfoInSourceObject(unsuccessful[index].Value<string>("parentName")) is null) {
				unsuccessful.RemoveAt(index);
			}
		}
		if (unsuccessful.Count > 0) {
			// No-progress guard: if this retry set is not strictly smaller than the previous pass, the remaining
			// operations can never place — e.g. a move of an element into its own descendant, whose parent is
			// always found (nothing is dropped) yet the insert always fails. Stop instead of recursing forever
			// (which would StackOverflow and crash the MCP server process). Valid diffs always shrink each pass.
			if (unsuccessful.Count >= previousUnsuccessfulCount) {
				return;
			}
			ApplyChangePositionOperationGroup([], [], unsuccessful, unsuccessful.Count);
		}
	}

	private static Dictionary<string, List<JObject>> GetObjectNameOperationsGroup(List<JObject> operations) {
		var result = new Dictionary<string, List<JObject>>(StringComparer.Ordinal);
		foreach (JObject operationItem in operations) {
			string name = operationItem.Value<string>("name") ?? string.Empty;
			if (!result.TryGetValue(name, out List<JObject> list)) {
				list = [];
				result[name] = list;
			}
			list.Add(operationItem);
		}
		return result;
	}

	private List<JObject> FilterMoveOperation(List<JObject> removes, List<JObject> moves) {
		List<JObject> filtered = moves;
		foreach (JObject removesItem in removes) {
			string removeName = removesItem.Value<string>("name");
			filtered = filtered.Where(movesItem => removeName != movesItem.Value<string>("name")).ToList();
		}
		return filtered;
	}

	private JObject ConvertMoveOperationToRemove(JObject operationItem) {
		var result = new JObject();
		string operationItemName = operationItem.Value<string>("name");
		ItemInfo item = FindItemInfoInSourceObject(operationItemName);
		if (item is not null) {
			result = new JObject {
				["parentName"] = item.ParentName,
				["propertyName"] = item.PropertyName,
				["index"] = item.PropertyName, // verbatim quirk from the TS source (index set to propertyName)
				["operation"] = "remove",
				["name"] = operationItemName,
			};
		}
		return result;
	}

	private JObject ConvertMoveOperationToInsert(JObject operationItem) {
		var result = new JObject();
		string operationItemName = operationItem.Value<string>("name");
		ItemInfo item = FindItemInfoInSourceObject(operationItemName);
		if (item is not null) {
			var insertOperationItem = new JObject {
				["values"] = item.Item.DeepClone(),
				["operation"] = "",
			};
			((JObject)insertOperationItem["values"]).Remove(AliasName);
			foreach (JProperty property in operationItem.Properties()) {
				insertOperationItem[property.Name] = property.Value.DeepClone();
			}
			insertOperationItem["operation"] = "insert";
			result = insertOperationItem;
		}
		return result;
	}

	// ----- path ordering (mirror _getOperationsSequenceByPath) -----

	private Dictionary<string, (string ParentName, string PropertyName)> GetOperationHierarchy(List<JObject> operations) {
		var result = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
		foreach (JObject operationItem in operations) {
			string name = operationItem.Value<string>("name");
			if (name is null) {
				continue;
			}
			result[name] = (
				string.IsNullOrEmpty(operationItem.Value<string>("parentName")) ? "_" : operationItem.Value<string>("parentName"),
				string.IsNullOrEmpty(operationItem.Value<string>("propertyName")) ? "_" : operationItem.Value<string>("propertyName"));
		}
		return result;
	}

	private string GetOperationItemRelativePath(string name, Dictionary<string, (string ParentName, string PropertyName)> hierarchy, string resultOut, HashSet<string> visited) {
		string result = resultOut;
		if (name is not null && hierarchy.TryGetValue(name, out (string ParentName, string PropertyName) entry)) {
			result = entry.ParentName + "=" + entry.PropertyName + "==" + result;
			result = GetOperationItemPath(entry.ParentName, hierarchy, result, visited);
		}
		return result;
	}

	private string GetOperationItemFullPath(string name, Dictionary<string, (string ParentName, string PropertyName)> hierarchy, string resultOut, HashSet<string> visited) {
		string result = resultOut;
		string parentName;
		string propertyName;
		if (name is not null && hierarchy.TryGetValue(name, out (string ParentName, string PropertyName) entry)) {
			parentName = entry.ParentName;
			propertyName = entry.PropertyName;
		} else {
			ItemInfo info = FindItemInfoInSourceObject(name);
			if (info is null) {
				return result;
			}
			parentName = info.ParentName;
			propertyName = info.PropertyName;
		}
		if (!string.IsNullOrEmpty(parentName)) {
			result = parentName + "=" + propertyName + "==" + result;
			result = GetOperationItemPath(parentName, hierarchy, result, visited);
		}
		return result;
	}

	private string GetOperationItemPath(string name, Dictionary<string, (string ParentName, string PropertyName)> hierarchy, string resultOut = "", HashSet<string> visited = null) {
		// A cyclic parentName chain (e.g. A parented to B and B parented to A) would recurse forever and
		// throw StackOverflowException — uncatchable in .NET, killing the whole MCP server process. Track the
		// names visited on the current chain; revisiting one is a cycle, surfaced as the existing catchable
		// LoopDependency error (a deliberate divergence from the TS clone, which also never terminates here).
		visited ??= new HashSet<string>(StringComparer.Ordinal);
		if (name is not null && !visited.Add(name)) {
			throw new JsonDiffApplierException(Format(JsonDiffApplierResources.LoopDependency, name));
		}
		if (!_disableApplyMoveIfIndirectParentMoved && (_operationsOptions?.ApplyMoveIfIndirectParentMoved ?? false)) {
			return GetOperationItemFullPath(name, hierarchy, resultOut, visited);
		}
		return GetOperationItemRelativePath(name, hierarchy, resultOut, visited);
	}

	private List<JObject> GetOperationsSequenceByPath(List<JObject> operations, bool isAsc) {
		Dictionary<string, (string, string)> hierarchy = GetOperationHierarchy(operations);
		var withPath = operations
			.Select(op => (Op: op, Path: GetOperationItemPath(op.Value<string>("name"), hierarchy)))
			.ToList();
		var groups = new Dictionary<string, List<JObject>>(StringComparer.Ordinal);
		var order = new List<string>();
		foreach ((JObject op, string path) in withPath) {
			if (!groups.TryGetValue(path, out List<JObject> group)) {
				group = [];
				groups[path] = group;
				order.Add(path);
			}
			group.Add(op);
		}
		// Stable ordering by path length (ascending when isAsc, otherwise descending) — OrderBy is stable.
		List<string> orderedPaths = order.OrderBy(path => isAsc ? path.Length : -path.Length).ToList();
		var result = new List<JObject>();
		foreach (string path in orderedPaths) {
			List<JObject> pathOperationCollection = groups[path]
				.OrderBy(op => op["index"], IndexComparer.Instance) // stable sortBy('index') ascending
				.ToList();
			if (!isAsc) {
				pathOperationCollection.Reverse();
			}
			result.AddRange(pathOperationCollection);
		}
		return result;
	}

	private sealed class IndexComparer : IComparer<JToken> {
		public static readonly IndexComparer Instance = new();

		public int Compare(JToken x, JToken y) {
			bool ex = IsEmpty(x);
			bool ey = IsEmpty(y);
			if (ex && ey) {
				return 0;
			}
			if (ex) {
				return 1; // undefined/empty sorts last (lodash sortBy)
			}
			if (ey) {
				return -1;
			}
			bool xNum = x!.Type is JTokenType.Integer or JTokenType.Float;
			bool yNum = y!.Type is JTokenType.Integer or JTokenType.Float;
			if (xNum && yNum) {
				return x.Value<double>().CompareTo(y.Value<double>());
			}
			return string.CompareOrdinal(x.ToString(), y.ToString());
		}
	}

	// ----- aliases (mirror _saveAlias / _excludeAliasProperties / _isExcludeAliasOperation) -----

	private void SaveAlias(JObject config) {
		if (IsEmpty(config["alias"])) {
			return;
		}
		var cloneConfig = (JObject)config.DeepClone();
		var aliasToken = (JObject)cloneConfig["alias"];
		_aliases ??= new Dictionary<string, JsonApplierAliasInfo>(StringComparer.Ordinal);
		string aliasName = aliasToken.Value<string>("name");
		var info = new JsonApplierAliasInfo {
			Name = cloneConfig.Value<string>("name"),
			ExcludeProperties = (aliasToken["excludeProperties"] as JArray)?.Select(t => t.Value<string>()).ToList(),
			ExcludeOperations = (aliasToken["excludeOperations"] as JArray)?.Select(t => t.Value<string>()).ToList(),
		};
		_aliases[aliasName] = info;
	}

	private void ResetAliases() => _aliases = null;

	protected IReadOnlyList<string> ExcludeAliasProperties(string name, JObject configValues) {
		List<string> properties = configValues.Properties().Select(p => p.Name).ToList();
		JsonApplierAliasInfo alias = _aliases is not null && name is not null && _aliases.TryGetValue(name, out JsonApplierAliasInfo a) ? a : null;
		IReadOnlyList<string> excludeProperties = alias?.ExcludeProperties;
		if (excludeProperties is not null) {
			properties = properties.Where(p => !excludeProperties.Contains(p)).ToList();
		}
		return properties;
	}

	private bool IsExcludeAliasOperation(string name, string operationName, JToken properties) {
		if (properties is JArray && operationName == "remove") {
			return false;
		}
		JsonApplierAliasInfo alias = _aliases is not null && name is not null && _aliases.TryGetValue(name, out JsonApplierAliasInfo a) ? a : null;
		IReadOnlyList<string> excludeOperations = alias?.ExcludeOperations;
		return excludeOperations is not null && excludeOperations.Contains(operationName);
	}

	// ----- insert / set / merge / remove (mirror _insert / _set / _merge / _remove) -----

	protected virtual ItemInfo FindInsertItemInfo(JObject config) => FindItemInfoInSourceObject(config.Value<string>("parentName"));

	protected virtual JToken FindInsertItemParent(ItemInfo itemInfo, JObject config) =>
		itemInfo is null ? _sourceObject : itemInfo.Item[config.Value<string>("propertyName")];

	private bool Insert(JObject config) {
		string parentName = config.Value<string>("parentName");
		string name = config.Value<string>("name");
		if (!string.IsNullOrEmpty(config.Value<string>("nameTo"))) {
			parentName = config.Value<string>("nameTo");
		}
		ItemInfo itemInfo = FindInsertItemInfo(config);
		bool parentExists = !(itemInfo is null && !string.IsNullOrEmpty(config.Value<string>("parentName")));
		JToken item = config["values"] is { } values && values.Type != JTokenType.Null ? values.DeepClone() : new JObject();
		SaveAlias(config);
		JToken parent = FindInsertItemParent(itemInfo, config);
		if (!IsEmpty(name) && item is JObject itemObject) {
			itemObject[AliasName] = name;
		}
		if (parent is JArray parentArray) {
			int length = parentArray.Count;
			int itemIndex = IsEmpty(config["index"]) ? length : config["index"]!.Value<int>();
			itemIndex = NormalizeSpliceStart(itemIndex, length);
			parentArray.Insert(itemIndex, item);
		} else if (parent is JObject && itemInfo is not null) {
			// parent is a JObject only when it came from itemInfo.Item[propertyName] (a null itemInfo yields the
			// root _sourceObject, a JArray), so itemInfo is never null here — the guard makes that explicit.
			itemInfo.Item[config.Value<string>("propertyName")] = item;
		} else {
			throw new JsonDiffApplierException(Format(JsonDiffApplierResources.NotContainerItemInsertException, parentName));
		}
		return parentExists;
	}

	private static int NormalizeSpliceStart(int index, int length) {
		if (index < 0) {
			return Math.Max(length + index, 0);
		}
		return Math.Min(index, length);
	}

	private bool Set(JObject config) {
		string parentName = config.Value<string>("parentName");
		if (!string.IsNullOrEmpty(config.Value<string>("nameTo"))) {
			parentName = config.Value<string>("nameTo");
		}
		JObject itemInfo = Remove(config);
		bool parentExists = !IsEmpty(itemInfo);
		if (parentExists) {
			config["index"] = itemInfo["index"]?.DeepClone();
			config["nameTo"] = parentName;
			config["parentName"] = parentName;
			config["propertyName"] = itemInfo["propertyName"]?.DeepClone();
		}
		Insert(config);
		return parentExists;
	}

	protected virtual ItemInfo FindMergeOrRemoveItemInfo(JObject config) => FindItemInfoInSourceObject(config.Value<string>("name"));

	protected virtual bool Merge(JObject config) {
		ItemInfo itemInfo = FindMergeOrRemoveItemInfo(config);
		bool parentExists = !IsEmpty(itemInfo);
		if (parentExists) {
			var values = config["values"] as JObject;
			if (values is null) {
				return parentExists; // a merge with no (or non-object) values is a no-op
			}
			foreach (JProperty property in ((JObject)itemInfo.Item).Properties().ToList()) {
				JToken firstChild = property.Value is JArray arr ? (arr.Count > 0 ? arr[0] : null) : property.Value;
				if (IsItemConfig(firstChild) && values[property.Name] is not null) {
					// ItemWithItemsPropertyMergeException (warn-and-drop, no throw)
					values.Remove(property.Name);
				}
			}
			foreach (string propertyName in ExcludeAliasProperties(config.Value<string>("name"), values)) {
				((JObject)itemInfo.Item)[propertyName] = values[propertyName]?.DeepClone();
			}
		}
		return parentExists;
	}

	protected virtual ItemInfo FindRemoveParentItemInfo(string parentItemName, ItemInfo itemInfo) => FindItemInfoInSourceObject(parentItemName);

	private JObject Remove(JObject config) {
		JObject result = new JObject();
		ItemInfo itemInfo = FindMergeOrRemoveItemInfo(config);
		if (IsEmpty(itemInfo)) {
			return null;
		}
		if (config["properties"] is JArray removeProperties) {
			var itemProperties = (JObject)itemInfo.Item;
			foreach (JToken removeProperty in removeProperties) {
				itemProperties.Remove(removeProperty.Value<string>());
			}
		} else {
			JToken removedItem = itemInfo.Item;
			string parentItemName = itemInfo.ParentName;
			ItemInfo parentItemInfo = FindRemoveParentItemInfo(parentItemName, itemInfo);
			JToken items = parentItemInfo is not null ? parentItemInfo.Item[itemInfo.PropertyName] : _sourceObject;
			int itemIndex = 0;
			if (items is JArray itemsArray) {
				itemIndex = IndexOfByReference(itemsArray, removedItem);
				if (itemIndex >= 0) {
					itemsArray.RemoveAt(itemIndex);
				}
			} else {
				((JObject)parentItemInfo!.Item).Remove(itemInfo.PropertyName);
			}
			RemoveFromCache(removedItem);
			result = new JObject {
				["index"] = itemIndex,
				["item"] = removedItem,
				["nameTo"] = parentItemName,
				["parentName"] = parentItemName,
				["propertyName"] = itemInfo.PropertyName,
			};
		}
		return result;
	}

	private static int IndexOfByReference(JArray array, JToken item) {
		for (int i = 0; i < array.Count; i++) {
			if (ReferenceEquals(array[i], item)) {
				return i;
			}
		}
		return -1;
	}

	// ----- validation (mirror _checkOperation / _checkRequiredParameters) -----

	private void CheckOperation(JObject operation) {
		string name = operation.Value<string>("name");
		string parentName = operation.Value<string>("parentName");
		if (!IsEmpty(name) && parentName == name) {
			throw new JsonDiffApplierException(Format(JsonDiffApplierResources.LoopDependency, name));
		}
		CheckRequiredParameters(operation, [OperationParameterName]);
		if (CheckOperationNameRequiredParameters) {
			string operationName = operation.Value<string>(OperationParameterName);
			if (operationName is not null && _operationRequiredParameters.TryGetValue(operationName, out IReadOnlyList<string> required)) {
				CheckRequiredParameters(operation, required);
			}
		}
	}

	private static void CheckRequiredParameters(JObject operationConfig, IReadOnlyList<string> requiredParameters) {
		if (IsEmpty(requiredParameters)) {
			return;
		}
		foreach (string parameterName in requiredParameters) {
			if (IsFalsy(operationConfig[parameterName])) {
				throw new JsonDiffApplierException(Format(JsonDiffApplierResources.RequiredParameterNotFound, parameterName));
			}
		}
	}

	protected sealed class ItemInfo {
		public JToken Item;
		public string ParentName;
		public string PropertyName;
		public object[] ParentPath;
	}

	/// <summary>
	/// Resolves a lodash-<c>get</c>-style path (string property names / int array indices) against a token,
	/// returning <paramref name="defaultValue"/> when the path is empty or any segment is missing.
	/// </summary>
	protected static JToken GetByPath(JToken root, IReadOnlyList<object> path, JToken defaultValue = null) {
		if (path is null || path.Count == 0) {
			return defaultValue;
		}
		JToken current = root;
		foreach (object key in path) {
			switch (current) {
				case JObject obj when obj[key.ToString()!] is { } child:
					current = child;
					break;
				case JArray arr when TryIndex(key, arr.Count, out int index):
					current = arr[index];
					break;
				default:
					return defaultValue;
			}
		}
		return current;
	}

	private static bool TryIndex(object key, int count, out int index) {
		index = key switch {
			int i => i,
			_ => int.TryParse(key.ToString(), out int parsed) ? parsed : -1,
		};
		return index >= 0 && index < count;
	}

	private sealed class JsonApplierAliasInfo {
		public string Name;
		public IReadOnlyList<string> ExcludeProperties;
		public IReadOnlyList<string> ExcludeOperations;
	}
}

/// <summary>Options for <see cref="JsonDiffApplier"/> (mirrors the client <c>JsonApplierOperationsOptions</c>).</summary>
public sealed class JsonApplierOperationsOptions {
	public bool ApplyMoveIfIndirectParentMoved { get; init; }
}

/// <summary>Error thrown by <see cref="JsonDiffApplier"/>, mirroring the client <c>new Error(...)</c> throws.</summary>
public sealed class JsonDiffApplierException : Exception {
	public JsonDiffApplierException(string message) : base(message) { }
}

/// <summary>Exact clone of the client <c>resources.ts</c> message templates used by <see cref="JsonDiffApplier"/>.</summary>
public static class JsonDiffApplierResources {
	public const string LoopDependency = "Cyclic dependency exists for object \"{0}\". The parentName parameter cannot be equal to name";
	public const string RequiredParameterNotFound = "Required parameter \"{0}\" not found in object";
	public const string ParameterNameObsolete = "Instead of the \"{0}\" parameter, use \"{1}";
	public const string PropertyInParameterObsolete = "The \"{0}\" parameter should no longer contain \"{1}\"";
	public const string NotContainerItemInsertException = "Item \"{0}\" is not a container for other items";
	public const string ItemWithItemsPropertyMergeException = "Item \"{0}\" should not contain parameter \"{1}\"";
	public const string ItemNameAlreadyExists = "Item with value \"{0}\" of the \"name\" parameter already exists";
}
