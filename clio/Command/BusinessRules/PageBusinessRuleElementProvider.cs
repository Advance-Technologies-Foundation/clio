using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Clio.Command.BusinessRules;

internal interface IPageBusinessRuleElementProvider
{
    IReadOnlySet<string> GetElementNames(PageBundleInfo bundle);
}

internal sealed class PageBusinessRuleElementProvider : IPageBusinessRuleElementProvider
{
    public IReadOnlySet<string> GetElementNames(PageBundleInfo bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        HashSet<string> result = new(StringComparer.Ordinal);
        CollectElementNames(bundle.ViewConfig, result);
        return result;
    }

    private static void CollectElementNames(JsonNode? node, ISet<string> sink)
    {
        Stack<JsonNode?> pending = new();
        pending.Push(node);
        while (pending.Count > 0)
        {
            JsonNode? current = pending.Pop();
            switch (current)
            {
                case JsonArray array:
                    foreach (JsonNode? item in array)
                    {
                        if (item is JsonArray || item is JsonObject)
                        {
                            pending.Push(item);
                        }
                    }

                    break;
                case JsonObject obj:
                    // In Freedom UI viewConfig, "name" is reserved for element declarations.
                    // Traversing every branch keeps discovery aligned with valid nested element placements.
                    string name = obj["name"]?.GetValue<string>() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        sink.Add(name);
                    }

                    foreach (KeyValuePair<string, JsonNode?> property in obj)
                    {
                        if (property.Value is JsonArray || property.Value is JsonObject)
                        {
                            pending.Push(property.Value);
                        }
                    }

                    break;
            }
        }
    }
}