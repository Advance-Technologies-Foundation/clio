using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Clio.Command.RuntimeVerification;

/// <summary>A record that appeared in a watched entity while a verified action ran.</summary>
/// <param name="Entity">Entity schema name.</param>
/// <param name="Id">Record id.</param>
/// <param name="Title">Value of the entity's primary display column, or <see langword="null"/> when it has none.</param>
public sealed record CreatedRecord(
	[property: JsonProperty("entity")]
	[property: System.Text.Json.Serialization.JsonPropertyName("entity")]
	string Entity,
	[property: JsonProperty("id")]
	[property: System.Text.Json.Serialization.JsonPropertyName("id")]
	string Id,
	[property: JsonProperty("title")]
	[property: System.Text.Json.Serialization.JsonPropertyName("title")]
	string Title) {

	/// <summary>The record's CreatedOn as DataService returned it (session time zone); orders the cleanup.</summary>
	[JsonIgnore]
	[System.Text.Json.Serialization.JsonIgnore]
	public string CreatedOn { get; init; }

	/// <summary>The contact in the record's CreatedBy; a cleanup deletes only the caller's own records.</summary>
	[JsonIgnore]
	[System.Text.Json.Serialization.JsonIgnore]
	public string CreatedById { get; init; }
}

/// <summary>A record a cleanup could not delete, with the reason.</summary>
public sealed record CleanupFailure(
	[property: JsonProperty("entity")]
	[property: System.Text.Json.Serialization.JsonPropertyName("entity")]
	string Entity,
	[property: JsonProperty("id")]
	[property: System.Text.Json.Serialization.JsonPropertyName("id")]
	string Id,
	[property: JsonProperty("error")]
	[property: System.Text.Json.Serialization.JsonPropertyName("error")]
	string Error);

/// <summary>What a cleanup deleted and what it could not.</summary>
public sealed class CleanupReport {

	/// <summary>Records deleted, in the order they were deleted.</summary>
	[JsonProperty("deleted")]
	[System.Text.Json.Serialization.JsonPropertyName("deleted")]
	public List<CreatedRecord> Deleted { get; set; } = [];

	/// <summary>Records still present after every pass, with the last refusal.</summary>
	[JsonProperty("failed")]
	[System.Text.Json.Serialization.JsonPropertyName("failed")]
	public List<CleanupFailure> Failed { get; set; } = [];

	/// <summary>
	/// Records left alone because nothing ties them to the action: on a shared environment another session - another
	/// user's, or the same login's - can create records in a watched entity during the same window, and a cleanup
	/// never deletes them.
	/// </summary>
	[JsonProperty("skipped")]
	[System.Text.Json.Serialization.JsonPropertyName("skipped")]
	public List<CreatedRecord> Skipped { get; set; } = [];
}

/// <summary>A process element a run is parked on.</summary>
public sealed record WaitingElement(
	[property: JsonProperty("caption")]
	[property: System.Text.Json.Serialization.JsonPropertyName("caption")]
	string Caption,
	[property: JsonProperty("type")]
	[property: System.Text.Json.Serialization.JsonPropertyName("type")]
	string Type);

/// <summary>The state of one process run as its SysProcessLog row reports it.</summary>
/// <param name="Found">Whether the run's log row exists.</param>
/// <param name="StatusValue">SysProcessStatus.Value: 0 inactive, 1 running, 2 completed, 3 error, 4 cancelled, 5 cancelling.</param>
/// <param name="ErrorSummary">First line of the run's own ErrorDescription, or of a failed sub-process's.</param>
/// <param name="WaitingOn">Elements of the run still in the Running state.</param>
public sealed record ProcessRunState(
	bool Found,
	int? StatusValue,
	string ErrorSummary,
	IReadOnlyList<WaitingElement> WaitingOn) {

	/// <summary>Whether the status is final: completed, error or cancelled.</summary>
	public bool IsTerminal => StatusValue is 2 or 3 or 4;

	/// <summary>Why part of the state is missing (for example the waiting elements could not be read), or <see langword="null"/>.</summary>
	public string Note { get; init; }
}

/// <summary>Per-entity state captured before a verified action.</summary>
public sealed class EntityWatch {

	/// <summary>Entity schema name.</summary>
	public string Entity { get; init; }

	/// <summary>Primary display column, or <see langword="null"/>.</summary>
	public string TitleColumn { get; init; }

	/// <summary>Entities this entity's lookup columns reference; orders the cleanup.</summary>
	public IReadOnlyCollection<string> References { get; init; } = [];

	/// <summary>Newest CreatedOn before the action, as DataService returned it; <see langword="null"/> for an empty table.</summary>
	public string Baseline { get; init; }

	/// <summary>Ids already at or after <see cref="Baseline"/> before the action.</summary>
	public IReadOnlySet<string> KnownIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>Why the entity could not be watched; <see langword="null"/> when it is watched.</summary>
	public string Error { get; init; }
}

/// <summary>Everything captured before a verified action.</summary>
public sealed class WatchBaseline {

	/// <summary>One entry per watched entity, in the caller's order.</summary>
	public IReadOnlyList<EntityWatch> Entities { get; init; } = [];

	/// <summary>The calling user's contact id, or <see langword="null"/> when it could not be read.</summary>
	public string CurrentUserContactId { get; init; }
}

/// <summary>The records a watch found, and why some entities could not be read.</summary>
public sealed class WatchOutcome {

	/// <summary>Records created since the baseline, oldest first per entity.</summary>
	public List<CreatedRecord> Created { get; } = [];

	/// <summary>One line per entity that could not be watched or read in full.</summary>
	public List<string> Warnings { get; } = [];
}

/// <summary>The runtime-verification services run-process uses, grouped so its constructor stays small.</summary>
public interface IRunVerificationServices {

	/// <summary>Reads and cancels a process run.</summary>
	IProcessRunMonitor Monitor { get; }

	/// <summary>Diffs watched entities before and after a run.</summary>
	ICreatedRecordWatcher Watcher { get; }

	/// <summary>Deletes what a run created.</summary>
	ICreatedRecordCleaner Cleaner { get; }

	/// <summary>Ties the records that appeared in the window to the run, so a cleanup deletes only those.</summary>
	IRunRecordProvenance Provenance { get; }
}

internal sealed record RunVerificationServices(
	IProcessRunMonitor Monitor,
	ICreatedRecordWatcher Watcher,
	ICreatedRecordCleaner Cleaner,
	IRunRecordProvenance Provenance) : IRunVerificationServices;
