using System.Text.Json.Nodes;

namespace Creatio.ConflictResolver;

internal static class JsonSemanticKeyValidator
{
	public static bool TryFindDuplicate(JsonNode? node, string keyPropertyName, out string duplicatePath)
	{
		return TryFindDuplicate(node, keyPropertyName, "$", out duplicatePath);
	}

	private static bool TryFindDuplicate(
		JsonNode? node,
		string keyPropertyName,
		string path,
		out string duplicatePath)
	{
		if (node is JsonArray array)
		{
			var keyedItems = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
			for (var index = 0; index < array.Count; index++)
			{
				if (array[index] is JsonObject item &&
					item[keyPropertyName] is JsonValue keyValue &&
					keyValue.TryGetValue<string>(out var key) &&
					!string.IsNullOrWhiteSpace(key))
				{
					if (keyedItems.TryGetValue(key, out JsonObject? previous) && !JsonNode.DeepEquals(previous, item))
					{
						duplicatePath = $"{path}[{index}].{keyPropertyName}:{key}";
						return true;
					}
					keyedItems[key] = item;
				}

				if (TryFindDuplicate(array[index], keyPropertyName, $"{path}[{index}]", out duplicatePath))
				{
					return true;
				}
			}
		}
		else if (node is JsonObject obj)
		{
			foreach (var property in obj)
			{
				if (TryFindDuplicate(property.Value, keyPropertyName, $"{path}.{property.Key}", out duplicatePath))
				{
					return true;
				}
			}
		}

		duplicatePath = string.Empty;
		return false;
	}
}
