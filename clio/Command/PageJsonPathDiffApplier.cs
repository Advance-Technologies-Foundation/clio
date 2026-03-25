namespace Clio.Command;

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

internal interface IPageJsonPathDiffApplier {
	JObject Apply(JObject sourceObject, JArray operations);
}

internal sealed class PageJsonPathDiffApplier : IPageJsonPathDiffApplier {
	private const string ValuesPropertyName = "values";

	public JObject Apply(JObject sourceObject, JArray operations) {
		JObject result = sourceObject.DeepClone() as JObject ?? new JObject();
		foreach (JObject operation in operations.Children<JObject>()) {
			switch (operation.Value<string>("operation")) {
				case "merge":
					Merge(result, operation);
					break;
				case "set":
					Set(result, operation);
					break;
				case "insert":
					Insert(result, operation);
					break;
				case "remove":
					Remove(result, operation);
					break;
				case "move":
					Move(result, operation);
					break;
			}
		}

		return result;
	}

	private static void Merge(JObject root, JObject operation) {
		PageJsonPathTarget target = ResolveTarget(root, operation);
		if (target.Token is JObject targetObject && operation[ValuesPropertyName] is JObject values) {
			JObject merged = PageBundleMergeHelpers.DeepMerge(targetObject, values);
			ReplaceToken(target, merged);
		}
	}

	private static void Set(JObject root, JObject operation) {
		PageJsonPathTarget target = ResolveTarget(root, operation);
		if (target.Token is null || operation[ValuesPropertyName] is null) {
			return;
		}

		ReplaceToken(target, operation[ValuesPropertyName].DeepClone());
	}

	private static void Insert(JObject root, JObject operation) {
		PageJsonPathTarget target = ResolveInsertParent(root, operation);
		JToken values = operation[ValuesPropertyName]?.DeepClone();
		if (values is null) {
			return;
		}

		if (target.Token is JArray array) {
			int requestedIndex = operation["index"]?.Value<int>() ?? array.Count;
			int index = Math.Clamp(requestedIndex, 0, array.Count);
			array.Insert(index, values);
			return;
		}

		if (target.Token is JObject obj && operation.Value<string>("propertyName") is string propertyName) {
			obj[propertyName] = values;
		}
	}

	private static void Remove(JObject root, JObject operation) {
		PageJsonPathTarget target = ResolveTarget(root, operation);
		if (target.Token is null) {
			return;
		}

		if (operation["properties"] is JArray properties && target.Token is JObject targetObject) {
			foreach (string property in properties.Values<string>()) {
				targetObject.Remove(property);
			}

			return;
		}

		if (target.Parent is JArray array) {
			array.RemoveAt(target.Index);
			return;
		}

		if (target.Parent is JObject obj && target.PropertyName is not null) {
			obj.Remove(target.PropertyName);
		}
	}

	private static void Move(JObject root, JObject operation) {
		PageJsonPathTarget target = ResolveTarget(root, operation);
		if (target.Token is null) {
			return;
		}

		JToken movedValue = target.Token.DeepClone();
		Remove(root, operation);
		JObject insertOperation = (JObject)operation.DeepClone();
		insertOperation["operation"] = "insert";
		insertOperation[ValuesPropertyName] = movedValue;
		Insert(root, insertOperation);
	}

	private static void ReplaceToken(PageJsonPathTarget target, JToken replacement) {
		if (target.Parent is JArray array) {
			array[target.Index] = replacement;
			return;
		}

		if (target.Parent is JObject obj && target.PropertyName is not null) {
			obj[target.PropertyName] = replacement;
		}
	}

	private static PageJsonPathTarget ResolveInsertParent(JObject root, JObject operation) {
		string parentName = operation.Value<string>("parentName");
		IReadOnlyList<object> path = PageBundleMergeHelpers.GetPathSegments(operation);
		if (!string.IsNullOrWhiteSpace(parentName)) {
			PageJsonPathTarget namedTarget = FindByAlias(root, parentName, "_id");
			if (namedTarget.Token is null) {
				return PageJsonPathTarget.Empty;
			}

			return path.Count > 0 ? ResolvePath(namedTarget.Token, path) : namedTarget;
		}

		return path.Count > 0 ? ResolvePath(root, path) : new PageJsonPathTarget(root, null, null, 0);
	}

	private static PageJsonPathTarget ResolveTarget(JObject root, JObject operation) {
		string name = operation.Value<string>("name");
		IReadOnlyList<object> path = PageBundleMergeHelpers.GetPathSegments(operation);
		if (!string.IsNullOrWhiteSpace(name)) {
			PageJsonPathTarget namedTarget = FindByAlias(root, name, "_id");
			if (namedTarget.Token is null) {
				return PageJsonPathTarget.Empty;
			}

			return path.Count > 0 ? ResolvePath(namedTarget.Token, path) : namedTarget;
		}

		return path.Count > 0 ? ResolvePath(root, path) : new PageJsonPathTarget(root, null, null, 0);
	}

	private static PageJsonPathTarget ResolvePath(JToken source, IReadOnlyList<object> path) {
		JToken current = source;
		JToken parent = null;
		string propertyName = null;
		int index = 0;
		foreach (object segment in path) {
			parent = current;
			switch (segment) {
				case int arrayIndex when current is JArray array:
					if (arrayIndex < 0 || arrayIndex >= array.Count) {
						return PageJsonPathTarget.Empty;
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
					return PageJsonPathTarget.Empty;
			}

			if (current is null) {
				return PageJsonPathTarget.Empty;
			}
		}

		return new PageJsonPathTarget(current, parent, propertyName, index);
	}

	private static PageJsonPathTarget FindByAlias(JToken token, string name, string aliasProperty) {
		return token switch {
			JObject obj => FindByAliasInObject(obj, name, aliasProperty),
			JArray array => FindByAliasInArray(array, name, aliasProperty),
			_ => PageJsonPathTarget.Empty
		};
	}

	private static PageJsonPathTarget FindByAliasInObject(JObject obj, string name, string aliasProperty) {
		if (string.Equals(obj.Value<string>(aliasProperty), name, StringComparison.Ordinal)) {
			return new PageJsonPathTarget(obj, null, null, 0);
		}
		foreach (JProperty property in obj.Properties()) {
			PageJsonPathTarget found = FindByAlias(property.Value, name, aliasProperty);
			if (found.Token is not null) {
				return AttachObjectParent(found, obj, property.Name);
			}
		}
		return PageJsonPathTarget.Empty;
	}

	private static PageJsonPathTarget FindByAliasInArray(JArray array, string name, string aliasProperty) {
		for (int index = 0; index < array.Count; index++) {
			PageJsonPathTarget found = FindByAlias(array[index], name, aliasProperty);
			if (found.Token is not null) {
				return AttachArrayParent(found, array, index);
			}
		}
		return PageJsonPathTarget.Empty;
	}

	private static PageJsonPathTarget AttachObjectParent(PageJsonPathTarget found, JObject parent, string propertyName) {
		return found.Parent is null
			? found with { Parent = parent, PropertyName = propertyName }
			: found;
	}

	private static PageJsonPathTarget AttachArrayParent(PageJsonPathTarget found, JArray parent, int index) {
		return found.Parent is null
			? found with { Parent = parent, Index = index }
			: found;
	}

	private sealed record PageJsonPathTarget(JToken Token, JToken Parent, string PropertyName, int Index) {
		public static PageJsonPathTarget Empty { get; } = new(null, null, null, 0);
	}
}
