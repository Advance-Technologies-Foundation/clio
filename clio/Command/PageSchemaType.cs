namespace Clio.Command;

/// <summary>
/// Identifies the kind of Freedom UI page schema.
/// Backing values match the <c>SysSchema.SchemaType</c> column in Creatio.
/// </summary>
public enum PageSchemaType {

	/// <summary>Unknown or unsupported schema type.</summary>
	Unknown = -1,

	/// <summary>Freedom UI web page (AMD module, schemaType = 9).</summary>
	Web = 9,

	/// <summary>Freedom UI mobile page (plain JSON, schemaType = 10).</summary>
	Mobile = 10
}

/// <summary>
/// Helpers for converting between <see cref="PageSchemaType"/> and its int / string representations.
/// </summary>
public static class PageSchemaTypeExtensions {

	/// <summary>
	/// Returns the human-readable label used in external-facing JSON responses
	/// (<c>"web"</c>, <c>"mobile"</c>, <c>"unknown"</c>).
	/// </summary>
	public static string ToLabel(this PageSchemaType type) => type switch {
		PageSchemaType.Web => "web",
		PageSchemaType.Mobile => "mobile",
		_ => "unknown"
	};

	/// <summary>
	/// Converts a Creatio <c>SysSchema.SchemaType</c> integer value to <see cref="PageSchemaType"/>.
	/// </summary>
	public static PageSchemaType FromNumericValue(int? id) => id switch {
		9 => PageSchemaType.Web,
		10 => PageSchemaType.Mobile,
		_ => PageSchemaType.Unknown
	};
}
