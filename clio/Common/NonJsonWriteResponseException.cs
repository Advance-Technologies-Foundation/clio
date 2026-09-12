using System;

namespace Clio.Common;

/// <summary>
/// Why a write-endpoint body could not be read as the DataService response the operation expected.
/// </summary>
public enum NonJsonWriteResponseKind {

	/// <summary>
	/// The body is not JSON at all - an HTML login or gateway page, an empty body, a JSON document
	/// carrying no response object, or text no parser accepts.
	/// </summary>
	NotJson,

	/// <summary>
	/// The body IS valid JSON, but it does not match the contract this operation expects - a field of the
	/// wrong type, or a document that is not the expected object at all. The request reached something
	/// that answers JSON; it is the shape that is wrong, not the media.
	/// </summary>
	UnexpectedShape
}

/// <summary>
/// A Creatio sys-settings WRITE endpoint answered with a body that is not the DataService response the
/// operation expected, and the body did not PROVE a rejected session, so
/// <c>ISysSettingsManager.ThrowIfSessionRejected</c> let it through.
/// </summary>
/// <remarks>
/// The write path holds the raw body, which is strictly more evidence than
/// <see cref="ClassifyingDataProvider"/> can have on the read path, where ATF's provider keeps only the
/// exception text. Before this type the extra evidence was thrown away: the body reached
/// <c>JsonSerializer.Deserialize</c>, and the bare <see cref="System.Text.Json.JsonException"/> it raised
/// carried nothing but a byte offset - so the write path's diagnosis was strictly WORSE than the read
/// path's <c>NonJsonPage</c> verdict, which at least names both causes and keeps the excerpt.
/// <para>
/// RELATIONSHIP TO <c>Clio.Package.NonJsonServiceResponseException</c>: same idea, different policy, and
/// deliberately not merged. That type composes a message that embeds the endpoint URL and a bounded
/// preview of the body, and has nowhere to put a <see cref="ServerDetail"/>; this one keeps ALL
/// server-authored text out of the message (issue #1333) and carries the neutralized excerpt on the
/// debug-only channel. The names are kept distinct - "write response" versus "service response" - so a
/// reader cannot mistake one policy for the other.
/// </para>
/// <para>
/// Derives from <see cref="InvalidOperationException"/> - the same lineage as
/// <see cref="DataProviderFailureException"/> - rather than from
/// <see cref="System.Text.Json.JsonException"/>: a parser exception is a statement about a parser, and
/// this one is a statement about the environment. <c>SysSettingsCommand.CategorizeFailure</c> has a
/// dedicated arm ABOVE its <see cref="InvalidOperationException"/> arm, so this keeps the <c>Network</c>
/// envelope the <see cref="System.Text.Json.JsonException"/> arm produces rather than degrading to
/// <c>Unknown</c>.
/// </para>
/// <para>
/// Implements <see cref="IAuthoritativeErrorMessage"/> so
/// <see cref="SurfacedExceptionMessage.Resolve"/> stops here instead of walking to the inner parser
/// fault. Without the marker the MCP envelope surfaced the parser's own text through <c>clio-run</c> -
/// and <c>System.Text.Json</c> quotes the offending path and value in it
/// (<c>Path: $.saveResult.&lt;server-controlled key&gt;</c>), so server-chosen bytes reached an agent's
/// context unfenced and uncapped, which is exactly what this type exists to prevent.
/// </para>
/// <para>
/// No <see cref="IConsoleRenderedFailure"/> rendering exists: <see cref="Exception.Message"/> is a fixed
/// local sentence, so there is no agent fence for a terminal to drop.
/// </para>
/// </remarks>
public sealed class NonJsonWriteResponseException : InvalidOperationException, IServerDetailCarrier,
		IAuthoritativeErrorMessage {

	/// <summary>Creates the failure.</summary>
	/// <param name="message">The fixed local diagnostic to surface.</param>
	/// <param name="kind">Whether the body was not JSON at all, or JSON of the wrong shape.</param>
	/// <param name="serverDetail">The neutralized, capped body excerpt, for debug verbosity only.</param>
	/// <param name="innerException">The parser failure, or <see langword="null"/> for an empty body.</param>
	public NonJsonWriteResponseException(string message, NonJsonWriteResponseKind kind,
		string serverDetail = null, Exception innerException = null)
		: base(message, innerException) {
		Kind = kind;
		ServerDetail = serverDetail;
	}

	/// <summary>Why the body could not be read as the expected response.</summary>
	public NonJsonWriteResponseKind Kind { get; }

	/// <inheritdoc/>
	public string ServerDetail { get; }
}
