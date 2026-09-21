using System.Collections.ObjectModel;
namespace Clio10.Contracts;

/// <summary>Portable argument shapes independent of a serialization library.</summary>
public enum ArgumentKind {
    /// <summary>A text value.</summary>
    String,
    /// <summary>A boolean value.</summary>
    Boolean,
    /// <summary>A whole number.</summary>
    Integer,
    /// <summary>A numeric value.</summary>
    Number,
    /// <summary>A nested name/value object.</summary>
    Object,
    /// <summary>An ordered collection.</summary>
    Array
}

/// <summary>Discoverable input schema owned by an operation.</summary>
public sealed record ArgumentDescriptor(string Name, ArgumentKind Kind, bool Required = false, string? Description = null,
    IReadOnlyList<ArgumentDescriptor>? Fields = null);

/// <summary>Copies portable values so a caller cannot mutate a running workflow's input.</summary>
public static class ArgumentValues {
    /// <summary>Creates an immutable snapshot of scalars, nested dictionaries and arrays.</summary>
    public static IReadOnlyDictionary<string, object?> Snapshot(IReadOnlyDictionary<string, object?>? values) =>
        CopyObject(values ?? new Dictionary<string, object?>(), 0);
    private static IReadOnlyDictionary<string, object?> CopyObject(IReadOnlyDictionary<string, object?> values, int depth) =>
        new ReadOnlyDictionary<string, object?>(values.ToDictionary(x => x.Key, x => Copy(x.Value, depth + 1), StringComparer.Ordinal));
    private static object? Copy(object? value, int depth) {
        if (depth > 32) throw new ArgumentException("Argument nesting exceeds 32 levels.");
        return value switch {
            double number when !double.IsFinite(number) => throw new ArgumentException("Numbers must be finite."),
            null or string or bool or int or long or double or decimal => value,
            IReadOnlyDictionary<string, object?> dictionary => CopyObject(dictionary, depth),
            System.Collections.IEnumerable list => Array.AsReadOnly(list.Cast<object?>().Select(x => Copy(x, depth + 1)).ToArray()),
            _ => throw new ArgumentException("Arguments must use portable scalar, object or array values.")
        };
    }
    /// <summary>Checks names and top-level types; workflows validate domain-specific values.</summary>
    public static bool IsValid(IReadOnlyDictionary<string, object?> values, IReadOnlyList<ArgumentDescriptor>? descriptors) {
        var schema = descriptors ?? [];
        if (values.Keys.Any(key => !schema.Any(x => x.Name == key))) return false;
        return schema.All(field => !values.TryGetValue(field.Name, out var value) ? !field.Required : field.Kind switch {
            ArgumentKind.String => value is string,
            ArgumentKind.Boolean => value is bool,
            ArgumentKind.Integer => value is int or long,
            ArgumentKind.Number => value is int or long or double or decimal,
            ArgumentKind.Object => value is IReadOnlyDictionary<string, object?> nested && (field.Fields is null || IsValid(nested, field.Fields)),
            ArgumentKind.Array => value is IReadOnlyList<object?>,
            _ => false
        });
    }
}
