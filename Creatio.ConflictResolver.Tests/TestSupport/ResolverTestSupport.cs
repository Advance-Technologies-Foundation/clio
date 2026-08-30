using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Creatio.ConflictResolver.Tests.TestSupport;

internal static class ResolverTestSupport
{
    public const string SchemaUid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    public const string UidA = "11111111-1111-1111-1111-111111111111";
    public const string UidB = "22222222-2222-2222-2222-222222222222";
    public const string UidC = "33333333-3333-3333-3333-333333333333";

    public static readonly global::Creatio.ConflictResolver.IConflictResolver Resolver = new global::Creatio.ConflictResolver.ConflictResolver();

    public static string BuildMetadata(params (string Uid, string Name)[] columns)
    {
        var payload = new
        {
            MetaData = new
            {
                Schema = new
                {
                    UId = SchemaUid,
                    D2 = columns.Select(static x => new Dictionary<string, string>
                    {
                        ["UId"] = x.Uid,
                        ["A2"] = x.Name
                    }).ToArray()
                }
            }
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    public static IReadOnlyList<string> GetMetadataD2Uids(string mergedContent)
    {
        var root = JsonNode.Parse(mergedContent)!;
        var d2 = root["MetaData"]?["Schema"]?["D2"]?.AsArray() ?? throw new InvalidOperationException("D2 not found");
        return d2
            .Select(static x => x?["UId"]?.GetValue<string>())
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x!)
            .ToArray();
    }

    public static string GetMetadataValue(string mergedContent, string uid)
    {
        var root = JsonNode.Parse(mergedContent)!;
        var d2 = root["MetaData"]?["Schema"]?["D2"]?.AsArray() ?? throw new InvalidOperationException("D2 not found");
        var item = d2.FirstOrDefault(x => string.Equals(x?["UId"]?.GetValue<string>(), uid, StringComparison.Ordinal));
        return item?["A2"]?.GetValue<string>() ?? string.Empty;
    }

    public static string BuildDescriptor(long timestamp)
    {
        return BuildDescriptor($"/Date({timestamp})/");
    }

    public static string BuildDescriptor(string modifiedOnUtc)
    {
        var payload = new
        {
            Descriptor = new
            {
                UId = SchemaUid,
                Name = "TestSchema",
                ModifiedOnUtc = modifiedOnUtc,
                ManagerName = "EntitySchemaManager",
                Caption = "Test"
            }
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    public static string BuildResource(params (string Name, string Value)[] items)
    {
        var xml = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                "Resources",
                new XAttribute("Culture", "en-US"),
                new XElement(
                    "Group",
                    new XAttribute("Type", "String"),
                    new XElement(
                        "Items",
                        items.Select(static x => new XElement(
                            "Item",
                            new XAttribute("Name", x.Name),
                            new XAttribute("Value", x.Value)))))));

        return xml.ToString(SaveOptions.None);
    }

    public static IReadOnlyDictionary<string, string> GetResourceMap(string content)
    {
        var doc = XDocument.Parse(content);
        return doc
            .Descendants()
            .Where(static x => x.Name.LocalName == "Item")
            .Select(static x => new
            {
                Name = (string?)x.Attribute("Name"),
                Value = (string?)x.Attribute("Value")
            })
            .Where(static x => !string.IsNullOrWhiteSpace(x.Name))
            .ToDictionary(static x => x.Name!, static x => x.Value ?? string.Empty);
    }

    public static string GetFixturePath(string fixtureCase, string fileName)
    {
        var normalizedFixtureCase = fixtureCase
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        var sourceProjectPath = TryGetTestProjectRootDirectory();
        if (!string.IsNullOrWhiteSpace(sourceProjectPath))
        {
            var sourceFixturePath = Path.Combine(
                sourceProjectPath,
                "Fixtures",
                normalizedFixtureCase,
                fileName);

            if (File.Exists(sourceFixturePath))
            {
                return sourceFixturePath;
            }
        }

        var path = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "Fixtures",
            normalizedFixtureCase,
            fileName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Fixture file '{fileName}' not found in case '{fixtureCase}'.",
                path);
        }

        return path;
    }

    private static string? TryGetTestProjectRootDirectory()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            var projectFilePath = Path.Combine(directory.FullName, "Creatio.ConflictResolver.Tests.csproj");
            if (File.Exists(projectFilePath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    public static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    public static IReadOnlyList<string> GetClientUnitCanonicalArraySectionEntries(
        string content,
        string marker,
        string? keyProperty = null)
    {
        var jsonSection = ExtractClientUnitSchemaSectionJson(content, marker);
        var root = JsonNode.Parse(jsonSection);
        if (root is not JsonArray array)
        {
            throw new InvalidOperationException($"Section '{marker}' is not an array.");
        }

        var entries = new List<(string Key, string Canonical)>();
        foreach (var item in array)
        {
            var canonical = CanonicalizeJson(item);
            var key = keyProperty is not null && item is JsonObject obj
                ? TryGetObjectString(obj, keyProperty) ?? canonical
                : canonical;
            entries.Add((key, canonical));
        }

        return entries
            .OrderBy(static x => x.Key, StringComparer.Ordinal)
            .ThenBy(static x => x.Canonical, StringComparer.Ordinal)
            .Select(static x => x.Canonical)
            .ToArray();
    }

    public static string GetClientUnitCanonicalObjectSection(string content, string marker)
    {
        var jsonSection = ExtractClientUnitSchemaSectionJson(content, marker);
        var root = JsonNode.Parse(jsonSection);
        if (root is not JsonObject obj)
        {
            throw new InvalidOperationException($"Section '{marker}' is not an object.");
        }

        return CanonicalizeJson(obj);
    }

    public static IReadOnlyDictionary<string, string> GetClientUnitViewModelAttributePaths(string content)
    {
        var jsonSection = ExtractClientUnitSchemaSectionJson(content, "SCHEMA_VIEW_MODEL_CONFIG_DIFF");
        var root = JsonNode.Parse(jsonSection);
        if (root is not JsonArray array)
        {
            throw new InvalidOperationException("Section 'SCHEMA_VIEW_MODEL_CONFIG_DIFF' is not an array.");
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in array.OfType<JsonObject>())
        {
            if (!IsAttributesMergeEntry(entry))
            {
                continue;
            }

            if (entry["values"] is not JsonObject values)
            {
                continue;
            }

            foreach (var attr in values)
            {
                if (attr.Value is not JsonObject attrConfig)
                {
                    continue;
                }

                var path = TryExtractModelConfigPath(attrConfig);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    map[attr.Key] = path!;
                }
            }
        }

        return map
            .OrderBy(static x => x.Key, StringComparer.Ordinal)
            .ToDictionary(static x => x.Key, static x => x.Value, StringComparer.Ordinal);
    }

    private static string ExtractClientUnitSchemaSectionJson(string content, string marker)
    {
        var token = $"/**{marker}*/";
        var firstMarker = content.IndexOf(token, StringComparison.Ordinal);
        if (firstMarker < 0)
        {
            throw new InvalidOperationException($"Marker '{marker}' not found.");
        }

        var current = firstMarker + token.Length;
        while (current < content.Length && char.IsWhiteSpace(content[current]))
        {
            current++;
        }

        if (current >= content.Length)
        {
            throw new InvalidOperationException($"Marker '{marker}' does not contain section content.");
        }

        var openChar = content[current];
        var closeChar = openChar switch
        {
            '[' => ']',
            '{' => '}',
            _ => throw new InvalidOperationException($"Marker '{marker}' is not followed by array/object.")
        };

        var closeIndex = FindMatchingBracket(content, current, openChar, closeChar);
        if (closeIndex < 0)
        {
            throw new InvalidOperationException($"Cannot find matching bracket for marker '{marker}'.");
        }

        return content.Substring(current, closeIndex - current + 1);
    }

    private static int FindMatchingBracket(string source, int startIndex, char openChar, char closeChar)
    {
        var depth = 0;
        var inString = false;
        var stringDelimiter = '\0';
        var escaped = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var i = startIndex; i < source.Length; i++)
        {
            var ch = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (inLineComment)
            {
                if (ch == '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                if (ch == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == stringDelimiter)
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '/' && next == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (ch == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            if (ch is '\'' or '"' or '`')
            {
                inString = true;
                stringDelimiter = ch;
                continue;
            }

            if (ch == openChar)
            {
                depth++;
                continue;
            }

            if (ch == closeChar)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static string CanonicalizeJson(JsonNode? node)
    {
        var normalized = NormalizeNode(node);
        return normalized?.ToJsonString() ?? "null";
    }

    private static JsonNode? NormalizeNode(JsonNode? node)
    {
        return node switch
        {
            null => null,
            JsonObject obj => NormalizeObject(obj),
            JsonArray arr => NormalizeArray(arr),
            _ => node.DeepClone()
        };
    }

    private static JsonObject NormalizeObject(JsonObject obj)
    {
        var normalized = new JsonObject();
        foreach (var pair in obj.OrderBy(static x => x.Key, StringComparer.Ordinal))
        {
            normalized[pair.Key] = NormalizeNode(pair.Value);
        }

        return normalized;
    }

    private static JsonArray NormalizeArray(JsonArray array)
    {
        var normalized = new JsonArray();
        foreach (var item in array)
        {
            normalized.Add(NormalizeNode(item));
        }

        return normalized;
    }

    private static string? TryGetObjectString(JsonObject obj, string propertyName)
    {
        if (!obj.TryGetPropertyValue(propertyName, out var node) || node is not JsonValue jsonValue)
        {
            return null;
        }

        return jsonValue.TryGetValue<string>(out var value) ? value : null;
    }

    private static bool IsAttributesMergeEntry(JsonObject entry)
    {
        var operation = TryGetObjectString(entry, "operation");
        if (!string.Equals(operation, "merge", StringComparison.Ordinal))
        {
            return false;
        }

        if (entry["path"] is not JsonArray pathArray || pathArray.Count != 1)
        {
            return false;
        }

        var pathSegment = pathArray[0] as JsonValue;
        return pathSegment is not null &&
               pathSegment.TryGetValue<string>(out var segment) &&
               string.Equals(segment, "attributes", StringComparison.Ordinal);
    }

    private static string? TryExtractModelConfigPath(JsonObject attrConfig)
    {
        if (attrConfig["modelConfig"] is not JsonObject modelConfig)
        {
            return null;
        }

        return TryGetObjectString(modelConfig, "path");
    }
}
