using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clio.Common;

internal static partial class CreatioResponseError {

	/// <summary>
	/// Longest message, in characters, the structured patterns are run against. Every measured Creatio
	/// OData message is under 300 characters; the bound keeps a crafted multi-megabyte message from being
	/// scanned by every pattern.
	/// </summary>
	private const int MaxStructuredMessageLength = 2_048;

	private const string ConstraintGroupName = "constraint";
	private const string TableGroupName = "table";

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
		string code = null;
		string fact = null;
		try {
			code = ReadErrorCode(error);
			fact = DescribeFirstStructuredFact(CollectErrorMessages(error));
		} catch (RegexMatchTimeoutException) {
			//A pattern that cannot finish in time has matched nothing; whatever finished before it may still be reported.
		}
		if (code is null && fact is null) {
			return null;
		}
		string codePart = code is null ? string.Empty : $"code '{code}'";
		string separator = code is not null && fact is not null ? "; " : string.Empty;
		return $"{StructuredDetailPrefix}{codePart}{separator}{fact ?? string.Empty}";
	}

	/// <summary>The framing both the read and the write hint open with.</summary>
	private const string StructuredDetailPrefix = "From the error payload (validated identifiers only): ";

	/// <summary>
	/// The write-path counterpart of <see cref="DescribeStructuredODataError"/>: a locally authored, one-line
	/// hint naming the identifiers of a measured foreign-key violation (PostgreSQL 23503, SQL Server 547) in
	/// an OData v4 error body (GH-1699).
	/// </summary>
	/// <param name="root">The parsed root of a write response body.</param>
	/// <returns>The hint, or <see langword="null"/> when no message carries a measured FK wording.</returns>
	/// <remarks>
	/// The same safety rules as the read hint apply: the same message walk, the same length cap and match
	/// timeout, and only identifiers captured by bounded ASCII patterns leave this method. Only the FK facts
	/// are reported, and no <c>error.code</c>: the read facts advise on select, filters and order-by, which
	/// a write never sends, and a body without an FK wording must leave the write result exactly as it was.
	/// The hint names a cause; it says nothing about whether the row was written, because an FK violation
	/// can come from a post-insert handler writing another row.
	/// </remarks>
	internal static string DescribeStructuredODataWriteError(JsonElement root) {
		if (root.ValueKind != JsonValueKind.Object
			|| !root.TryGetProperty("error", out JsonElement error)
			|| error.ValueKind != JsonValueKind.Object) {
			return null;
		}
		string fact = null;
		try {
			fact = DescribeFirstForeignKeyFact(CollectErrorMessages(error));
		} catch (RegexMatchTimeoutException) {
			//A pattern that cannot finish in time has matched nothing.
		}
		return fact is null ? null : $"{StructuredDetailPrefix}{fact}";
	}

	/// <summary>
	/// Appends <see cref="DescribeStructuredODataWriteError"/> to a write tool's already redacted error text.
	/// </summary>
	/// <param name="redactedServerError">The redacted message the write tool reported before GH-1699.</param>
	/// <param name="root">The parsed root of the same response body.</param>
	/// <returns><paramref name="redactedServerError"/> unchanged when there is no FK hint, otherwise both.</returns>
	/// <remarks>
	/// The hint is appended AFTER redaction on purpose: it is clio's own sentence around validated
	/// identifiers, and the redactor has nothing to remove from it.
	/// </remarks>
	internal static string AppendStructuredODataWriteError(string redactedServerError, JsonElement root) {
		string hint = DescribeStructuredODataWriteError(root);
		if (hint is null) {
			return redactedServerError;
		}
		return string.IsNullOrWhiteSpace(redactedServerError) ? hint : $"{redactedServerError} {hint}";
	}

	/// <summary>
	/// Gives a diagnostic next step without assuming a mapping defect or that the rejected write was
	/// the caller's row rather than a write performed by an entity event handler.
	/// </summary>
	private const string MissingLookupAdvice =
		" Inspect lookup metadata with get-entity-schema-properties and verify the supplied IDs. "
		+ "If the relationship remains unclear, ask an administrator to resolve the named constraint. "
		+ "Do not guess replacement IDs; follow retry-guidance before resubmitting.";

	/// <summary>
	/// Matches each message against the measured FK wordings and describes the first match.
	/// </summary>
	private static string DescribeFirstForeignKeyFact(IReadOnlyCollection<string> messages) {
		foreach (string message in messages) {
			if (string.IsNullOrEmpty(message) || message.Length > MaxStructuredMessageLength) {
				continue;
			}
			string fact = DescribePostgresForeignKey(message) ?? DescribeSqlServerForeignKey(message);
			if (fact is not null) {
				return fact;
			}
		}
		return null;
	}

	private static string DescribePostgresForeignKey(string message) {
		Match insert = PostgresInsertForeignKeyPattern().Match(message);
		if (insert.Success) {
			Match detail = PostgresMissingKeyDetailPattern().Match(message);
			string cause = detail.Success
				? $"a value in column '{detail.Groups["column"].Value}' has no matching record in referenced table "
					+ $"'{detail.Groups["referenced"].Value}'."
				: "a referenced record is missing. The response does not identify the foreign-key column or referenced table.";
			return $"foreign key constraint '{insert.Groups[ConstraintGroupName].Value}' on table "
				+ $"'{insert.Groups[TableGroupName].Value}' rejected the write: {cause}{MissingLookupAdvice}";
		}
		Match delete = PostgresDeleteForeignKeyPattern().Match(message);
		if (!delete.Success) {
			return null;
		}
		string referencing = delete.Groups["referencing"].Value;
		return $"the record is still referenced: foreign key constraint '{delete.Groups[ConstraintGroupName].Value}' "
			+ $"on table '{referencing}' points at this row of table '{delete.Groups[TableGroupName].Value}'. "
			+ StillReferencedAdvice(referencing);
	}

	private static string DescribeSqlServerForeignKey(string message) {
		Match insert = SqlServerInsertForeignKeyPattern().Match(message);
		if (insert.Success) {
			return $"foreign key constraint '{insert.Groups[ConstraintGroupName].Value}' rejected the write: a referenced "
				+ $"record is missing from referenced table '{insert.Groups[TableGroupName].Value}'. "
				+ $"The response does not identify the foreign-key column.{MissingLookupAdvice}";
		}
		Match delete = SqlServerDeleteForeignKeyPattern().Match(message);
		if (!delete.Success) {
			return null;
		}
		string referencing = delete.Groups[TableGroupName].Value;
		return $"the record is still referenced: constraint '{delete.Groups[ConstraintGroupName].Value}' on table "
			+ $"'{referencing}' (column '{delete.Groups["column"].Value}') points at this row. "
			+ StillReferencedAdvice(referencing);
	}

	private static string StillReferencedAdvice(string referencingTable) =>
		$"Inspect the referencing rows in '{referencingTable}' and confirm the intended relationship. "
		+ "The rejected operation may be a key update or an entity event handler's write. "
		+ "Do not delete or re-point records without authorization; follow retry-guidance before resubmitting.";

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
				return DescribeOperandTypeMismatch(typeMismatch.Groups["left"].Value,
					typeMismatch.Groups["right"].Value, typeMismatch.Groups["operator"].Value);
			}
			if (NullPropertyArgumentPattern().IsMatch(message)) {
				return "Creatio could not resolve a property while building the response (a null 'property' "
					+ "argument). One measured cause is a binary (Edm.Stream) column in select; if select names "
					+ "one, remove it.";
			}
		}
		return null;
	}

	/// <summary>
	/// Column types odata-read cannot compare with any value it sends: a JSON string always goes out as a
	/// quoted string literal, and these types need a typed literal instead.
	/// </summary>
	private static readonly HashSet<string> TypesThatNeedATypedLiteral = new(StringComparer.Ordinal) {
		"Edm.Date", "Edm.DateTimeOffset", "Edm.Duration", "Edm.Guid", "Edm.TimeOfDay"
	};

	/// <summary>
	/// Names both operand types and the operator. Only a date, time or GUID column compared with a string
	/// is pointed at execute-esq: every other pair is fixed inside odata-read by sending the JSON value as
	/// the type of the column.
	/// </summary>
	private static string DescribeOperandTypeMismatch(string left, string right, string operatorKind) {
		string fact = $"a filter compares a '{left}' column with a '{right}' value (operator '{operatorKind}').";
		return right == "Edm.String" && TypesThatNeedATypedLiteral.Contains(left)
			? $"{fact} odata-read sends a JSON string as a quoted string literal, so this column cannot be "
				+ "compared with a string value here; read that filter with execute-esq instead."
			: $"{fact} Send the filter value as the JSON type of that column (a number, a boolean or a string).";
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
	[GeneratedRegex(@"^[A-Za-z0-9_.:-]{1,64}\z", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
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
		@"Column by path (?<path>[A-Za-z_][A-Za-z0-9_]{0,63}(?:[./][A-Za-z_][A-Za-z0-9_]{0,63}){0,3}) not found in schema (?<schema>[A-Za-z_][A-Za-z0-9_]{0,63})\.",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex ColumnPathNotFoundPattern();

	/// <summary>
	/// "A binary operator with incompatible types was detected. Found operand types 'Edm.DateTimeOffset' and
	/// 'Edm.String' for operator kind 'GreaterThan'." - measured for a date and for a UId compared with a
	/// string literal. Both operands are one-segment Edm primitive names, the only shape measured.
	/// </summary>
	[GeneratedRegex(
		@"Found operand types '(?<left>Edm\.[A-Za-z][A-Za-z0-9]{0,31})' and '(?<right>Edm\.[A-Za-z][A-Za-z0-9]{0,31})' for operator kind '(?<operator>[A-Za-z]{1,32})'",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex OperandTypeMismatchPattern();

	/// <summary>
	/// "Value cannot be null.\r\nParameter name: property" - measured as the whole innererror message (HTTP
	/// 500, headline "An error has occurred.") for a $select naming the binary column SysSchema.MetaData.
	/// Anchored at both ends so only that exact message counts.
	/// </summary>
	[GeneratedRegex(@"^Value cannot be null\.\s{1,4}Parameter name: property\s{0,4}\z",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex NullPropertyArgumentPattern();

	/// <summary>
	/// PostgreSQL 23503 for an INSERT or UPDATE: <c>insert or update on table "DocListInFinApp" violates
	/// foreign key constraint "FK6R22cV5NWM2CfAp2GAV4B2R2GfY"</c> - the wording GH-1699 captured with a raw
	/// POST. The double quotes close right after each bounded identifier, so a quoted name holding a space,
	/// markup or a non-ASCII character does not match at all.
	/// </summary>
	[GeneratedRegex(
		@"insert or update on table ""(?<table>[A-Za-z_][A-Za-z0-9_]{0,127})"" violates foreign key constraint ""(?<constraint>[A-Za-z_][A-Za-z0-9_]{0,127})""",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex PostgresInsertForeignKeyPattern();

	/// <summary>
	/// The DETAIL line PostgreSQL adds to 23503 when the driver includes error detail:
	/// <c>Key (AccountId)=(&lt;guid&gt;) is not present in table "Account".</c> The key value is matched by
	/// a bounded class and never copied.
	/// </summary>
	[GeneratedRegex(
		@"Key \((?<column>[A-Za-z_][A-Za-z0-9_]{0,127})\)=\([^()\r\n]{1,64}\) is not present in table ""(?<referenced>[A-Za-z_][A-Za-z0-9_]{0,127})""",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex PostgresMissingKeyDetailPattern();

	/// <summary>
	/// PostgreSQL 23503 for a DELETE (or a key UPDATE) of a row other rows still reference:
	/// <c>update or delete on table "Account" violates foreign key constraint "FK..." on table "Contact"</c>.
	/// </summary>
	[GeneratedRegex(
		@"update or delete on table ""(?<table>[A-Za-z_][A-Za-z0-9_]{0,127})"" violates foreign key constraint ""(?<constraint>[A-Za-z_][A-Za-z0-9_]{0,127})"" on table ""(?<referencing>[A-Za-z_][A-Za-z0-9_]{0,127})""",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex PostgresDeleteForeignKeyPattern();

	/// <summary>
	/// SQL Server 547 for an INSERT or UPDATE: <c>The INSERT statement conflicted with the FOREIGN KEY
	/// constraint "FK...". The conflict occurred in database "db", table "dbo.Account", column 'Id'.</c> The
	/// table named is the REFERENCED one. The database name is matched by a bounded class (128 characters is
	/// the SQL Server maximum) and never copied, and the schema prefix is cut.
	/// </summary>
	[GeneratedRegex(
		@"The (?:INSERT|UPDATE) statement conflicted with the FOREIGN KEY constraint ""(?<constraint>[A-Za-z_][A-Za-z0-9_]{0,127})""\. The conflict occurred in database ""[^""\r\n]{1,128}"", table ""(?:[A-Za-z_][A-Za-z0-9_]{0,127}\.)?(?<table>[A-Za-z_][A-Za-z0-9_]{0,127})"", column '[A-Za-z_][A-Za-z0-9_]{0,127}'",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex SqlServerInsertForeignKeyPattern();

	/// <summary>
	/// SQL Server 547 for a DELETE (or a key UPDATE) of a row other rows still reference: <c>The DELETE
	/// statement conflicted with the REFERENCE constraint "FK...". The conflict occurred in database "db",
	/// table "dbo.Contact", column 'AccountId'.</c> Here the table and column are the REFERENCING ones.
	/// </summary>
	[GeneratedRegex(
		@"The (?:DELETE|UPDATE) statement conflicted with the REFERENCE constraint ""(?<constraint>[A-Za-z_][A-Za-z0-9_]{0,127})""\. The conflict occurred in database ""[^""\r\n]{1,128}"", table ""(?:[A-Za-z_][A-Za-z0-9_]{0,127}\.)?(?<table>[A-Za-z_][A-Za-z0-9_]{0,127})"", column '(?<column>[A-Za-z_][A-Za-z0-9_]{0,127})'",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex SqlServerDeleteForeignKeyPattern();

}
