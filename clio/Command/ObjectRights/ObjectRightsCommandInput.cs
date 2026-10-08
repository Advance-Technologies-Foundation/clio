using System;
using System.Collections.Generic;
using Clio.Common;
using Clio.Common.ObjectRights;

namespace Clio.Command.ObjectRights;

/// <summary>
/// What a name the caller passed resolved to. <see cref="ByCode"/> is the read of the object with that code — not found
/// when no object has it, <see langword="null"/> when the name is not a schema identifier; <see cref="TitleMatches"/> are
/// the objects whose title the name is, looked up only when no object has the code.
/// </summary>
/// <param name="Code">The name as a schema identifier, or <see langword="null"/> when it is not one.</param>
/// <param name="ByCode">The read of the object with that code.</param>
/// <param name="TitleMatches">The objects whose title the name is; empty when an object has the code.</param>
internal sealed record ObjectNameResolution(string Code, ObjectRightsInfo ByCode,
	IReadOnlyList<ObjectTitleMatch> TitleMatches);

/// <summary>
/// The inputs <c>get-object-rights</c> and <c>set-object-rights</c> read the same way, so their validation and their
/// messages cannot drift apart.
/// </summary>
internal static class ObjectRightsCommandInput {

	/// <summary>
	/// Reads <c>--entity-schema-name</c>: required and trimmed. Whether it is a code or a title is decided by
	/// <see cref="TryResolveObjectName"/>, which reads only a schema identifier as a code
	/// (<see cref="ObjectRightsSupport.TryNormalizeSchemaName"/>).
	/// </summary>
	/// <param name="raw">The name as the caller passed it.</param>
	/// <param name="logger">Where the refusal is written.</param>
	/// <param name="named">The trimmed name, when one was given.</param>
	/// <returns><see langword="true"/> when a name was given.</returns>
	internal static bool TryReadObjectName(string raw, ILogger logger, out string named) {
		named = raw?.Trim();
		if (string.IsNullOrEmpty(named)) {
			logger.WriteError("Error: --entity-schema-name is required.");
			return false;
		}
		return true;
	}

	/// <summary>
	/// Resolves what the caller named. The code always wins: a schema identifier is read as a code first, and an object
	/// with that code — read, or taken to exist because its read failed — is never resolved by title. Otherwise the name
	/// is looked up as a title; text that is not a schema identifier is only ever a title. What to do with the title
	/// matches is each command's own policy.
	/// </summary>
	/// <param name="named">The trimmed name the caller passed.</param>
	/// <param name="readByCode">Reads the object with a code, the way the command reads it.</param>
	/// <param name="reader">The object-rights reader, for the title lookup.</param>
	/// <param name="requestOptions">Timeout, retry and deadline settings.</param>
	/// <param name="logger">Where an error is written.</param>
	/// <param name="resolution">What the name resolved to.</param>
	/// <returns><see langword="false"/> when the title lookup failed, or when the name is neither a schema identifier
	/// nor any object's title; the error is written.</returns>
	internal static bool TryResolveObjectName(string named, Func<string, ObjectRightsInfo> readByCode,
		IObjectRightsReader reader, CreatioRequestOptions requestOptions, ILogger logger,
		out ObjectNameResolution resolution) {
		resolution = null;
		string code = ObjectRightsSupport.TryNormalizeSchemaName(named, out string normalized) ? normalized : null;
		ObjectRightsInfo byCode = code is null ? null : readByCode(code);
		if (byCode is { Found: true }) {
			resolution = new ObjectNameResolution(code, byCode, Array.Empty<ObjectTitleMatch>());
			return true;
		}
		IReadOnlyList<ObjectTitleMatch> matches;
		// A lookup the service fails is reported as a failure, never as "no object has this title" — and when the name is
		// a code no object has, the failure says that too, so a typo is not taken for a passing fault.
		try {
			matches = reader.FindObjectsByTitle(named, requestOptions) ?? Array.Empty<ObjectTitleMatch>();
		}
		catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex)) {
			string lookup = $"the lookup of the objects titled '{ObjectRightsSupport.Display(named)}' failed: "
				+ ObjectRightsSupport.DisplayFailure(ex);
			logger.WriteError(code is null
				? $"Error: {lookup}"
				: $"Error: no object has the code '{code}', and {lookup}");
			return false;
		}
		if (code is null && matches.Count == 0) {
			logger.WriteError($"Error: --entity-schema-name '{ObjectRightsSupport.Display(named)}' is not an object code "
				+ "(letters, digits and '_' only), and no object has it as its title.");
			return false;
		}
		resolution = new ObjectNameResolution(code, byCode, matches);
		return true;
	}

	/// <summary>
	/// States that <paramref name="named"/> is a title, naming the object(s) it belongs to by their code:
	/// <c>'Creatio functionality' is not an object code: it is the title of Feature</c>.
	/// </summary>
	/// <param name="named">The name the caller passed.</param>
	/// <param name="matches">The objects whose title it is; at least one.</param>
	/// <returns>The display-safe clause.</returns>
	internal static string NotACode(string named, IReadOnlyList<ObjectTitleMatch> matches) =>
		$"'{ObjectRightsSupport.Display(named)}' is not an object code: it is the title of "
		+ (matches.Count == 1
			? matches[0].Name
			: $"{matches.Count} objects: {ObjectRightsSupport.FormatTitleMatches(matches)}");

	/// <summary>
	/// The request options built from a remote command's timeout and retry arguments and, for a caller bounded by a
	/// deadline (MCP), the time the whole call may take, which starts now.
	/// </summary>
	/// <param name="options">The command's options.</param>
	/// <param name="callBudget">The time every request of the call may take together; <see langword="null"/>: no limit
	/// beyond each request's timeout.</param>
	/// <returns>The request options for the service calls.</returns>
	internal static CreatioRequestOptions RequestOptions(RemoteCommandOptions options, TimeSpan? callBudget = null) => new() {
		TimeOut = options.TimeOut, MaxAttempts = options.MaxAttempts, RetryDelay = options.RetryDelay,
		Deadline = callBudget is { } budget ? new RequestDeadline(budget) : null
	};
}
