namespace Clio.Command;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Newtonsoft.Json.Linq;

/// <summary>
/// Faithful C# clone of the Creatio client-side <c>JsonPathApplierService</c>
/// (<c>creatio-ui/.../services/json-path-applier/json-path-applier.service.ts</c>), a subclass of
/// <see cref="JsonDiffApplier"/> used for <c>viewModelConfigDiff</c> / <c>modelConfigDiff</c>. It identifies
/// elements by <c>_id</c> (not <c>name</c>), resolves targets by <c>parentName</c> + <c>path</c> (lodash
/// <c>get</c>), does NOT enforce per-operation required parameters, deep-merges values (arrays replaced by the
/// incoming value), and tracks a parent <c>path</c> during traversal so removals can locate the grandparent.
/// </summary>
// Deliberate 1:1 port of the client TS JsonPathApplierService — method complexity and casts mirror the
// reference for fidelity; refactoring would diverge from the source of truth this validator reproduces.
[SuppressMessage("Critical Code Smell", "S3776:Cognitive Complexity of methods should not be too high", Justification = "1:1 clone of the client TS JsonPathApplierService — structure mirrors the reference for fidelity.")]
[SuppressMessage("Minor Code Smell", "S3247:Duplicate casts should not be used", Justification = "1:1 clone of the client TS JsonPathApplierService — mirrors the reference's dynamic narrowing.")]
public sealed class JsonPathDiffApplier : JsonDiffApplier {

	public JsonPathDiffApplier(bool disableApplyMoveIfIndirectParentMoved = false)
		: base(disableApplyMoveIfIndirectParentMoved) { }

	protected override string AliasName => "_id";

	protected override bool CheckOperationNameRequiredParameters => false;

	protected override ItemInfo FindItemInfoInSourceObject(string name) => FindItemInfo(name, _sourceObject);

	protected override JToken FindInsertItemParent(ItemInfo itemInfo, JObject config) =>
		itemInfo is null ? _sourceObject : itemInfo.Item;

	protected override ItemInfo FindInsertItemInfo(JObject config) {
		string parentName = config.Value<string>("parentName");
		ItemInfo itemInfo = FindItemInfoInSourceObject(parentName);
		JArray pathArr = config["path"] as JArray;
		if (itemInfo?.Item is not null && pathArr is not null) {
			itemInfo = new ItemInfo { Item = GetByPath(itemInfo.Item, PathSegments(pathArr)) };
		} else if (pathArr is not null) {
			List<object> parts = PathSegments(pathArr);
			itemInfo = new ItemInfo { Item = GetByPath(_sourceObject, parts) };
			if (itemInfo.Item is null) {
				string rootName = parts.Count > 0 ? parts[0]?.ToString() : null;
				if (parts.Count > 0) {
					parts.RemoveAt(0);
				}
				itemInfo = FindItemInfoInSourceObject(rootName);
				if (itemInfo?.Item is not null && parts.Count > 0) {
					itemInfo = new ItemInfo { Item = GetByPath(itemInfo.Item, parts) };
				}
			}
		}
		return itemInfo;
	}

	protected override ItemInfo FindMergeOrRemoveItemInfo(JObject config) {
		ItemInfo itemInfo = FindItemInfoInSourceObject(config.Value<string>("name"));
		JArray pathArr = config["path"] as JArray;
		if (itemInfo?.Item is null && pathArr is not null) {
			List<object> parts = PathSegments(pathArr);
			JToken found = GetByPath(_sourceObject, parts, parts.Count > 0 ? null : _sourceObject);
			itemInfo = new ItemInfo { Item = found };
			if (itemInfo.Item is null) {
				string parentName = parts.Count > 0 ? parts[0]?.ToString() : null;
				if (parts.Count > 0) {
					parts.RemoveAt(0);
				}
				itemInfo = FindItemInfoInSourceObject(parentName);
				if (itemInfo?.Item is not null && parts.Count > 0) {
					itemInfo = new ItemInfo { Item = GetByPath(itemInfo.Item, parts) };
				}
			}
		}
		return itemInfo;
	}

	protected override ItemInfo FindRemoveParentItemInfo(string parentItemName, ItemInfo itemInfo) {
		ItemInfo parentItemInfo = FindItemInfoInSourceObject(parentItemName);
		if (parentItemInfo is null && itemInfo?.ParentPath is not null) {
			var parentPath = new List<object>(itemInfo.ParentPath);
			if (parentPath.Count > 0) {
				parentPath.RemoveAt(parentPath.Count - 1); // removed index
			}
			if (parentPath.Count > 0) {
				parentPath.RemoveAt(parentPath.Count - 1); // removed propertyName
			}
			if (parentPath.Count == 0) {
				return null;
			}
			parentItemInfo = new ItemInfo { Item = GetByPath(_sourceObject, parentPath) };
		}
		return parentItemInfo;
	}

	protected override bool Merge(JObject config) {
		ItemInfo itemInfo = FindMergeOrRemoveItemInfo(config);
		bool parentExists = !IsEmpty(itemInfo);
		if (parentExists) {
			var configValues = (JObject)config["values"];
			var values = new JObject();
			foreach (string propertyName in ExcludeAliasProperties(config.Value<string>("name"), configValues)) {
				values[propertyName] = configValues[propertyName]?.DeepClone();
			}
			var target = (JObject)itemInfo.Item;
			foreach (JProperty property in DeepMergeReplaceArrays(target, values).Properties()) {
				target[property.Name] = property.Value;
			}
		}
		return parentExists;
	}

	protected override bool IterateChildItems(JToken config, System.Func<IterationConfig, bool> iterator) =>
		WalkWithPath(config, iterator, new List<object>());

	private bool WalkWithPath(JToken config, System.Func<IterationConfig, bool> iterator, List<object> parentPath) {
		bool result = true;
		if (config is not JObject obj) {
			return result;
		}
		foreach (JProperty propertyEntry in obj.Properties().ToList()) {
			JToken property = propertyEntry.Value;
			if (property is JArray or JObject) {
				bool isParentArray = property is JArray;
				IList<JToken> items = isParentArray ? (JArray)property : new List<JToken> { property };
				parentPath.Add(propertyEntry.Name);
				for (int index = 0; index < items.Count; index++) {
					JToken childItem = items[index];
					if (isParentArray) {
						parentPath.Add(index);
					}
					bool childIterationResult = WalkWithPath(childItem, iterator, new List<object>(parentPath));
					var iterationConfig = new IterationConfig {
						Item = childItem as JObject,
						PropertyName = propertyEntry.Name,
						ChildIterationResult = childIterationResult,
						ParentPath = parentPath.ToArray(),
						Parent = config,
					};
					result = iterator(iterationConfig) && childIterationResult;
					if (isParentArray && result) {
						parentPath.RemoveAt(parentPath.Count - 1);
					}
					if (!result) {
						break; // inner-each return false
					}
				}
				if (result) {
					parentPath.RemoveAt(parentPath.Count - 1);
				}
			}
			if (!result) {
				break; // outer-each return false
			}
		}
		return result;
	}

	private static List<object> PathSegments(JArray path) =>
		path.Select(token => (object)token.Value<string>()).ToList();

	/// <summary>
	/// Deep-merges <paramref name="source"/> into a clone of <paramref name="target"/>, mirroring
	/// <c>deepmerge.all([target, source], { arrayMerge: (_, src) =&gt; src })</c>: nested objects merge,
	/// arrays and scalars are replaced by the incoming value, target-only keys are preserved.
	/// </summary>
	private static JObject DeepMergeReplaceArrays(JObject target, JObject source) {
		var result = (JObject)target.DeepClone();
		foreach (JProperty property in source.Properties()) {
			JToken incoming = property.Value;
			if (result[property.Name] is JObject existing && incoming is JObject incomingObject) {
				result[property.Name] = DeepMergeReplaceArrays(existing, incomingObject);
			} else {
				result[property.Name] = incoming.DeepClone();
			}
		}
		return result;
	}
}
