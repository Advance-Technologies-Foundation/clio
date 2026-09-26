using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clio.Common;

internal static partial class CreatioResponseError {

	/// <summary>
	/// Longest message, in characters, the structured patterns are run against. Every measured Creatio
	/// OData message is under 300 characters; the bound keeps a crafted multi-megabyte message from being
	/// scanned by four patterns.
	/// </summary>
	private const int MaxStructuredMessageLength = 2_048;

	/// <summary>
	/// Composes a locally authored, one-line hint from the ENUM-LIKE parts of an OData v4 error body: the
	/// <c>error.code</c> and, when a message matches one of the measured Creatio wordings, the identifiers
	/// that wording names (the unknown property, the operand types of a mistyped comparison).
	/// </summary>
	/// <param name="root">The parsed root of a body <see cref="TryClassify(JsonElement, CreatioResponseContext, out ODataErrorKind, out string)"/> recognized.</param>
	/// <returns>The hint, or <see langword="null"/> when the body carries nothing structured.</returns>
	/// <remarks>
	/// No server prose is copied. Only an <c>error.code</c> matching <see cref="ErrorCodePattern"/> and
	/// identifiers captured by patterns that accept nothing but ASCII letters, digits, underscore and dots
	/// (plus '/' in a column path) leave this method; every sentence around them is clio's own. A message
	/// that does not match a pattern contributes nothing, so a forged instruction, markup or a
	/// non-ASCII identifier yields no text at all rather than a partial one.
	/// </remarks>
	internal static string DescribeStructuredODataError(JsonElement root) {
		if (root.ValueKind != JsonValueKind.Object
			|| !root.TryGetProperty("error", out JsonElement error)
			|| error.ValueKind != JsonValueKind.Object) {
			return null;
		}
		string code = ReadErrorCode(error);
		string fact = null;
		try {
			fact = DescribeFirstStructuredFact(CollectErrorMessages(error));
		} catch (RegexMatchTimeoutException) {
			//A pattern that cannot finish in time has matched nothing; the code alone may still be reported.
		}
		if (code is null && fact is null) {
			return null;
		}
		string codePart = code is null ? string.Empty : $"code '{code}'";
		string separator = code is not null && fact is not null ? "; " : string.Empty;
		return $"From the error payload (validated identifiers only): {codePart}{separator}{fact ?? string.Empty}";
	}

	private static string ReadErrorCode(JsonElement error) {
		if (!error.TryGetProperty("code", out JsonElement codeElement)
			|| codeElement.ValueKind != JsonValueKind.String) {
			return null;
		}
		string code = codeElement.GetString();
		//Creatio sends an empty code on every OData error measured so far; an empty code carries nothing.
		return !string.IsNullOrEmpty(code) && ErrorCodePattern().IsMatch(code) ? code : null;
	}

	private static List<string> CollectErrorMessages(JsonElement error) {
		List<string> messages = [];
		if (error.TryGetProperty("message", out JsonElement headline) && headline.ValueKind == JsonValueKind.String) {
			messages.Add(headline.GetString());
		}
		AppendInnerMessages(error, messages, 0);
		return messages;
	}

	/// <summary>
	/// Matches each message against the measured wordings, most specific first, and describes the first
	/// match. At most one fact is reported: the messages of one body restate the same cause.
	/// </summary>
	private static string DescribeFirstStructuredFact(IReadOnlyCollection<string> messages) {
		foreach (string message in messages) {
			if (string.IsNullOrEmpty(message) || message.Length > MaxStructuredMessageLength) {
				continue;
			}
			Match unknownProperty = UnknownPropertyPattern().Match(message);
			if (unknownProperty.Success) {
				return $"unknown property '{unknownProperty.Groups["property"].Value}' on "
					+ $"'{ShortTypeName(unknownProperty.Groups["type"].Value)}'. Correct or remove that name in "
					+ "select, filters, expand or order-by.";
			}
			Match columnPath = ColumnPathNotFoundPattern().Match(message);
			if (columnPath.Success) {
				return $"column path '{columnPath.Groups["path"].Value}' not found in schema "
					+ $"'{columnPath.Groups["schema"].Value}'. Check that name in filters and select; a lookup is "
					+ "filtered through its navigation path (for example Account/Id), not its raw foreign-key column.";
			}
			Match typeMismatch = OperandTypeMismatchPattern().Match(message);
			if (typeMismatch.Success) {
				return $"a filter compares a '{ShortTypeName(typeMismatch.Groups["left"].Value)}' column with a "
					+ $"'{ShortTypeName(typeMismatch.Groups["right"].Value)}' value (operator "
					+ $"'{typeMismatch.Groups["operator"].Value}'). odata-read sends a JSON number or boolean as is, "
					+ "but every JSON string as a quoted string literal - except a GUID on a field whose name ends in "
					+ "'Id' after a lowercase letter or digit - so a date column, or a GUID column such as UId, cannot "
					+ "be compared with a string value here; read that filter with execute-esq instead.";
			}
			if (NullPropertyArgumentPattern().IsMatch(message)) {
				return "Creatio could not resolve a property while building the response (a null 'property' "
					+ "argument). This is how selecting a binary (Edm.Stream) column such as SysSchema.MetaData is "
					+ "answered: remove such columns from select.";
			}
		}
		return null;
	}

	/// <summary>
	/// Keeps an <c>Edm.*</c> primitive type whole and cuts a CLR-qualified entity type
	/// (<c>Terrasoft.Configuration.OData.SysSchema</c>) to its last segment, which is the entity name the
	/// caller used.
	/// </summary>
	private static string ShortTypeName(string qualifiedName) {
		if (qualifiedName.StartsWith("Edm.", StringComparison.Ordinal)) {
			return qualifiedName;
		}
		int lastDot = qualifiedName.LastIndexOf('.');
		return lastDot < 0 ? qualifiedName : qualifiedName[(lastDot + 1)..];
	}

	/// <summary>An <c>error.code</c> that may be echoed: short, and enum-like by its alphabet.</summary>
	[GeneratedRegex(@"^[A-Za-z0-9_.:-]{1,64}$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex ErrorCodePattern();

	/// <summary>
	/// "Could not find a property named 'Foo' on type 'Terrasoft.Configuration.OData.SysSchema'." - measured
	/// for an unknown member in $select, $filter, $orderby and $expand. The quotes close immediately after
	/// the bounded identifier, so a longer or non-ASCII name does not match at all.
	/// </summary>
	[GeneratedRegex(
		@"Could not find a property named '(?<property>[A-Za-z_][A-Za-z0-9_]{0,127})' on type '(?<type>[A-Za-z_][A-Za-z0-9_]{0,127}(?:\.[A-Za-z_][A-Za-z0-9_]{0,127}){0,7})'",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex UnknownPropertyPattern();

	/// <summary>
	/// "Column by path SysSettingsId not found in schema SysSettingsValue." - measured (GH-1407) for a
	/// filter on a raw foreign-key column, two levels down under innererror/internalexception.
	/// </summary>
	[GeneratedRegex(
		@"Column by path (?<path>[A-Za-z_][A-Za-z0-9_]{0,127}(?:[./][A-Za-z_][A-Za-z0-9_]{0,127}){0,7}) not found in schema (?<schema>[A-Za-z_][A-Za-z0-9_]{0,127})\.",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex ColumnPathNotFoundPattern();

	/// <summary>
	/// "A binary operator with incompatible types was detected. Found operand types 'Edm.DateTimeOffset' and
	/// 'Edm.String' for operator kind 'GreaterThan'." - measured for a date and for a UId compared with a
	/// string literal.
	/// </summary>
	[GeneratedRegex(
		@"Found operand types '(?<left>[A-Za-z_][A-Za-z0-9_]{0,63}(?:\.[A-Za-z_][A-Za-z0-9_]{0,63}){0,7})' and '(?<right>[A-Za-z_][A-Za-z0-9_]{0,63}(?:\.[A-Za-z_][A-Za-z0-9_]{0,63}){0,7})' for operator kind '(?<operator>[A-Za-z]{1,32})'",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex OperandTypeMismatchPattern();

	/// <summary>
	/// "Value cannot be null.\r\nParameter name: property" - measured as the whole innererror message (HTTP
	/// 500, headline "An error has occurred.") for a $select naming the binary column SysSchema.MetaData.
	/// Anchored at both ends so only that exact message counts.
	/// </summary>
	[GeneratedRegex(@"^Value cannot be null\.\s{1,4}Parameter name: property\s{0,4}$",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex NullPropertyArgumentPattern();

}
