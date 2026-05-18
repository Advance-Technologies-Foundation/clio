using System;
namespace Clio.Command;

/// <summary>
/// Identifies the kind of Freedom UI page schema.
/// Backing values match the <c>ClientUnitSchemaType</c> enum stored in schema MetaData.
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
	/// Converts a Creatio <c>ClientUnitSchemaType</c> integer value to <see cref="PageSchemaType"/>.
	/// </summary>
	public static PageSchemaType FromNumericValue(int? id) => id switch {
		9 => PageSchemaType.Web,
		10 => PageSchemaType.Mobile,
		_ => PageSchemaType.Unknown
	};

	/// <summary>
	/// Infers <see cref="PageSchemaType"/> from the raw schema body content.
	/// Bodies starting with <c>{</c> (after trimming whitespace) are mobile JSON;
	/// anything else (typically <c>define(…)</c>) is a web AMD module.
	/// </summary>
	public static PageSchemaType FromBody(string body) {
		if (string.IsNullOrWhiteSpace(body)) {
			return PageSchemaType.Unknown;
		}
		return body.AsSpan().TrimStart().StartsWith("{") ? PageSchemaType.Mobile : PageSchemaType.Web;
	}
}
