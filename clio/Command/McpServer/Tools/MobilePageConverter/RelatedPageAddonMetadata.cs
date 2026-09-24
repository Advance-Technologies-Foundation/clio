using System;
using System.Text.Json.Nodes;

namespace Clio.Command.McpServer.Tools.MobilePageConverter;

/// <summary>
/// Pure parser for a <c>RelatedPage</c>/<c>MobileRelatedPage</c> add-on's <c>metaData</c> body — one read of the
/// <c>Pages</c> array, shared by <see cref="MobileActionTargetProbe.ClassifyRelatedPageMetadata"/> (which only
/// needs to know whether an untyped default exists) and <see cref="MobileActionTargetProbe.ExtractDefaultPageSchemaUId"/>
/// (which needs that default's <c>PageSchemaUId</c>). Before this class the two methods parsed the same JSON in
/// two separate loops that could silently drift on what counts as "the" default entry.
/// </summary>
internal static class RelatedPageAddonMetadata {

	/// <summary>
	/// The three outcomes a metaData body settles into. <see cref="Unparseable"/> covers every shape that
	/// says nothing about the object (blank body, malformed JSON, a non-object root) — the same shape
	/// <c>AddonSchemaDto.MetaData</c> defaults to when the server never answered. <see cref="NoDefault"/> is a
	/// real, well-formed answer that simply names no untyped default (including a body with no <c>Pages</c>
	/// array at all). <see cref="DefaultFound"/> is the only outcome that carries a
	/// <see cref="Result.PageSchemaUId"/>.
	/// </summary>
	internal enum Status { Unparseable, NoDefault, DefaultFound }

	/// <param name="Status">Which of the three outcomes this body settled into.</param>
	/// <param name="PageSchemaUId">
	/// The untyped default entry's raw <c>PageSchemaUId</c> value when <paramref name="Status"/> is
	/// <see cref="Status.DefaultFound"/>; <see langword="null"/> otherwise. Not itself validated as a
	/// <see cref="Guid"/> — callers that need one parse it themselves.
	/// </param>
	internal readonly record struct Result(Status Status, string PageSchemaUId);

	/// <summary>
	/// Parses <paramref name="metaData"/> once and reports which <see cref="Status"/> it settled into. The
	/// mobile UI offers no record-type and no audience choice, so an object has at most one mobile page: the
	/// "default" this looks for is the entry with <c>IsDefault</c> true AND a blank <c>TypeColumnValue</c> — a
	/// typed or audience-scoped entry can still be WRITTEN into this add-on (the same spec builder serves the
	/// web <c>RelatedPage</c> add-on, where those fields are real) but is not what the mobile UI would produce,
	/// so it is ignored here. Never throws.
	/// </summary>
	internal static Result TryFindUntypedDefault(string metaData) {
		if (string.IsNullOrWhiteSpace(metaData)) {
			return new Result(Status.Unparseable, null);
		}
		try {
			if (JsonNode.Parse(metaData) is not JsonObject obj) {
				return new Result(Status.Unparseable, null);
			}
			if (obj["Pages"] is not JsonArray pages) {
				return new Result(Status.NoDefault, null);
			}
			foreach (JsonNode page in pages) {
				if (page is JsonObject entry && Bool(entry, "IsDefault")
					&& string.IsNullOrWhiteSpace(Str(entry, "TypeColumnValue"))) {
					return new Result(Status.DefaultFound, Str(entry, "PageSchemaUId"));
				}
			}
			return new Result(Status.NoDefault, null);
		} catch (Exception) {
			return new Result(Status.Unparseable, null);
		}
	}

	/// <summary>
	/// Reads a scalar property as text. A non-string scalar falls back to its literal form, matching
	/// <c>RelatedPageAddonService</c>'s reader: this probe classifies the SAME add-on metadata, and a numeric
	/// <c>TypeColumnValue</c> read as null there would make a TYPED entry look like the untyped default.
	/// </summary>
	internal static string Str(JsonObject obj, string property) {
		if (obj is null || string.IsNullOrEmpty(property)
			|| !obj.TryGetPropertyValue(property, out JsonNode node) || node is not JsonValue value) {
			return null;
		}
		return value.TryGetValue(out string text) ? text : value.ToJsonString().Trim('"');
	}

	/// <summary>
	/// Reads a boolean property, tolerating a bool stored as the STRING <c>"true"</c>. Deliberately as lenient
	/// as <c>RelatedPageAddonService</c>'s reader: the two surfaces classify the same persisted add-on, and a
	/// stricter read here would report "no default mobile page" for a record the other surface reports as the
	/// default — a disagreement that lands on the false-alarm side.
	/// </summary>
	private static bool Bool(JsonObject obj, string property) {
		if (obj is null || !obj.TryGetPropertyValue(property, out JsonNode node) || node is not JsonValue value) {
			return false;
		}
		if (value.TryGetValue(out bool flag)) {
			return flag;
		}
		return value.TryGetValue(out string text) && bool.TryParse(text, out bool parsed) && parsed;
	}
}
