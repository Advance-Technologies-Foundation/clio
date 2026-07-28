namespace Clio.Common.Responses
{
	using System.Runtime.Serialization;

	#region Class: CommandEnvelope

	/// <summary>
	/// Unified, machine-readable output envelope emitted by clio commands in <c>--json</c> mode
	/// (the BL-1 Agent-DX contract). Exactly one such object is written to stdout for a command
	/// invocation; diagnostic logs and warnings go to stderr, so the stdout stream always parses as a
	/// single JSON document.
	/// </summary>
	/// <typeparam name="TData">Type of the command-specific success payload carried in <see cref="Data"/>.</typeparam>
	/// <remarks>
	/// Field order is stable (<c>schemaVersion, ok, command, data, error</c>). On success <see cref="Ok"/> is
	/// <c>true</c>, <see cref="Data"/> carries the payload and <see cref="Error"/> is <c>null</c>; on failure
	/// <see cref="Ok"/> is <c>false</c>, <see cref="Data"/> is <c>null</c> and <see cref="Error"/> describes the
	/// failure with a stable machine-readable code. The contract is versioned by <see cref="SchemaVersion"/>
	/// (additive changes bump the minor version) so consumers can branch on the shape safely.
	/// </remarks>
	[DataContract]
	public class CommandEnvelope<TData>
	{

		#region Properties: Public

		/// <summary>Semantic version of the envelope contract. Additive changes bump the minor version.</summary>
		[DataMember(Name = "schemaVersion", Order = 0)]
		public string SchemaVersion { get; set; } = CommandEnvelope.CurrentSchemaVersion;

		/// <summary><c>true</c> when the command succeeded; <c>false</c> otherwise.</summary>
		[DataMember(Name = "ok", Order = 1)]
		public bool Ok { get; set; }

		/// <summary>Canonical kebab-case name of the command that produced this envelope (e.g. <c>list-packages</c>).</summary>
		[DataMember(Name = "command", Order = 2)]
		public string Command { get; set; }

		/// <summary>Command-specific success payload; <c>null</c> when <see cref="Ok"/> is <c>false</c>.</summary>
		[DataMember(Name = "data", Order = 3)]
		public TData Data { get; set; }

		/// <summary>Failure description; <c>null</c> when <see cref="Ok"/> is <c>true</c>.</summary>
		[DataMember(Name = "error", Order = 4)]
		public CommandError Error { get; set; }

		#endregion

	}

	#endregion

	#region Class: CommandEnvelope

	/// <summary>Shared constants for the unified command output envelope.</summary>
	public static class CommandEnvelope
	{

		/// <summary>Current envelope schema version emitted by clio.</summary>
		public const string CurrentSchemaVersion = "1.0";

	}

	#endregion

	#region Class: CommandError

	/// <summary>
	/// Stable, machine-readable failure description carried by <see cref="CommandEnvelope{TData}.Error"/>.
	/// Intentionally omits the stack trace — the code lets automation branch on the failure class, and the
	/// message is a user-friendly sentence, so raw traces never leak into machine output.
	/// </summary>
	[DataContract]
	public class CommandError
	{

		#region Constructors: Public

		/// <summary>Initializes an empty <see cref="CommandError"/> (for deserialization).</summary>
		public CommandError() { }

		/// <summary>Initializes a <see cref="CommandError"/> with a stable code and a human-readable message.</summary>
		/// <param name="code">Stable, machine-readable error code (see <see cref="Clio.Common.CommandErrorCodes"/>).</param>
		/// <param name="message">User-friendly, actionable error message.</param>
		public CommandError(string code, string message) {
			Code = code;
			Message = message;
		}

		#endregion

		#region Properties: Public

		/// <summary>Stable, machine-readable error code (see <see cref="Clio.Common.CommandErrorCodes"/>).</summary>
		[DataMember(Name = "code", Order = 0)]
		public string Code { get; set; }

		/// <summary>User-friendly, actionable error message (never a raw stack trace).</summary>
		[DataMember(Name = "message", Order = 1)]
		public string Message { get; set; }

		#endregion

	}

	#endregion

}
