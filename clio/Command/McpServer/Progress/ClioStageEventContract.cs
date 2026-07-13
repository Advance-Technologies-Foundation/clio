using System.Text.Json;

namespace Clio.Command.McpServer.Progress;

/// <summary>
/// Shared constants for the <see cref="ClioStageEvent"/> contract: the schema version, the
/// canonical <see cref="System.Text.Json"/> options, and the stable wire vocabularies.
/// </summary>
/// <remarks>
/// The Ring mirrors this contract (ADR D2). <see cref="SchemaVersion"/> is the cross-repo
/// compatibility gate; the byte shape is anchored by a committed JSON fixture. The vocabularies
/// are exposed as string constants (not enums) so both repos and the on-disk receipt reference
/// identical wire tokens without an ordinal coupling.
/// </remarks>
public static class ClioStageEventContract {

	/// <summary>Current contract version. Bumped only on a breaking field change.</summary>
	public const int SchemaVersion = 1;

	/// <summary>
	/// Canonical serializer options for the contract: compact (no indentation), so each event
	/// serializes to a single line suitable for the <c>_meta</c> envelope and the NDJSON receipt.
	/// Per-member <c>[JsonPropertyName]</c> and <c>[JsonIgnore(WhenWritingNull)]</c> attributes
	/// carry the wire names and null-omission, so no global policy is configured here.
	/// Unknown members are tolerated on read (System.Text.Json skips them by default).
	/// </summary>
	public static JsonSerializerOptions SerializerOptions { get; } = new() {
		WriteIndented = false
	};

	/// <summary>Allowed <see cref="ClioStageEvent.EventType"/> values.</summary>
	public static class EventTypes {

		/// <summary>The up-front manifest of every stage that will run.</summary>
		public const string Manifest = "manifest";

		/// <summary>A single stage transition.</summary>
		public const string Stage = "stage";

		/// <summary>The terminal outcome of the run.</summary>
		public const string RunCompleted = "run-completed";
	}

	/// <summary>Allowed <see cref="ClioStageEvent.Operation"/> values.</summary>
	public static class Operations {

		/// <summary>A Creatio deploy operation.</summary>
		public const string Deploy = "deploy";

		/// <summary>A Creatio uninstall operation.</summary>
		public const string Uninstall = "uninstall";
	}

	/// <summary>Allowed <see cref="ClioStageDetail.Status"/> values.</summary>
	public static class StageStatuses {

		/// <summary>The stage is currently running.</summary>
		public const string Running = "running";

		/// <summary>The stage completed successfully.</summary>
		public const string Done = "done";

		/// <summary>The stage failed.</summary>
		public const string Failed = "failed";

		/// <summary>The stage was skipped (see <see cref="ClioStageDetail.SkipReason"/>).</summary>
		public const string Skipped = "skipped";
	}

	/// <summary>Allowed <see cref="ClioRunCompleted.Outcome"/> values.</summary>
	public static class RunOutcomes {

		/// <summary>The run completed successfully.</summary>
		public const string Success = "success";

		/// <summary>The run failed.</summary>
		public const string Failure = "failure";
	}

	/// <summary>Allowed <see cref="ClioStageDetail.SkipReason"/> values.</summary>
	public static class SkipReasons {

		/// <summary>The stage is inert for the resolved inputs (e.g. non-network source for <c>stage-build</c>).</summary>
		public const string NotApplicable = "not-applicable";

		/// <summary>The stage was skipped because an earlier stage failed (failure cascade).</summary>
		public const string AfterFailure = "after-failure";

		/// <summary>The stage is not supported (e.g. app-pool profile deletion).</summary>
		public const string NotSupported = "not-supported";
	}
}
