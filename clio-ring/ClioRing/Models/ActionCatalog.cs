using System.Collections.Generic;

namespace ClioRing.Models;

/// <summary>Discriminates which typed action block is active. Closed set — no type-name discriminators.</summary>
public enum ActionKind {
	/// <summary>Runs a clio CLI verb as a child process. See <see cref="RingAction.ClioCommand"/>.</summary>
	ClioCommand,

	/// <summary>Opens a URL in the default browser. See <see cref="RingAction.OpenUrl"/>.</summary>
	OpenUrl,

	/// <summary>Opens a file-system path with the OS shell. See <see cref="RingAction.OpenPath"/>.</summary>
	OpenPath,

	/// <summary>
	/// Opens the guided Creatio Install form (a primary main-ring action). Carries no typed block —
	/// the ring host opens the form window rather than running a child process. See story 8.
	/// </summary>
	GuidedInstall,

	/// <summary>
	/// Opens the guided Creatio Uninstall flow (a primary main-ring action). Carries no typed block —
	/// the ring host opens the flow window (local-env picker → simple Yes/No confirm → shared pipeline)
	/// rather than running a child process directly. See story 9.
	/// </summary>
	GuidedUninstall
}

/// <summary>Runtime type of a parameter the user supplies before an action runs.</summary>
public enum ParameterType {
	/// <summary>Free-text value.</summary>
	String,

	/// <summary>Integer value.</summary>
	Int,

	/// <summary>Boolean flag.</summary>
	Bool,

	/// <summary>A registered clio environment name.</summary>
	Env
}

/// <summary>Confirmation gate an action requires before it executes.</summary>
public enum Risk {
	/// <summary>Runs immediately.</summary>
	None,

	/// <summary>Requires a confirmation prompt.</summary>
	Confirm,

	/// <summary>Destructive: requires explicit confirmation and is visually flagged.</summary>
	Destructive
}

/// <summary>Typed payload for <see cref="ActionKind.ClioCommand"/>.</summary>
public sealed record ClioCommandSpec {
	/// <summary>The clio verb, e.g. <c>get-info</c>.</summary>
	public required string Verb { get; init; }

	/// <summary>Additional positional/option arguments passed verbatim to clio.</summary>
	public IReadOnlyList<string> Args { get; init; } = new List<string>();

	/// <summary>Optional environment name; when set clio is invoked with <c>-e &lt;EnvName&gt;</c>.</summary>
	public string? EnvName { get; init; }
}

/// <summary>Typed payload for <see cref="ActionKind.OpenUrl"/>.</summary>
public sealed record OpenUrlSpec {
	/// <summary>The absolute URL to open.</summary>
	public required string Url { get; init; }
}

/// <summary>Typed payload for <see cref="ActionKind.OpenPath"/>.</summary>
public sealed record OpenPathSpec {
	/// <summary>The file-system path to open.</summary>
	public required string Path { get; init; }
}

/// <summary>Describes a single parameter the user is prompted for before an action runs.</summary>
public sealed record ParameterDescriptor {
	/// <summary>Parameter name.</summary>
	public required string Name { get; init; }

	/// <summary>Runtime type used to render/validate input.</summary>
	public required ParameterType ParameterType { get; init; }

	/// <summary>Whether a value must be supplied.</summary>
	public bool Required { get; init; }

	/// <summary>Optional default value (as a string; coerced by <see cref="ParameterType"/>).</summary>
	public string? Default { get; init; }
}

/// <summary>
/// A single declarative action. Exactly one typed block (<see cref="ClioCommand"/>,
/// <see cref="OpenUrl"/>, <see cref="OpenPath"/>) is populated, selected by <see cref="Kind"/>.
/// </summary>
public sealed record RingAction {
	/// <summary>Stable unique id.</summary>
	public required string Id { get; init; }

	/// <summary>Display title shown in the ring.</summary>
	public required string Title { get; init; }

	/// <summary>Optional short label for the ring node (falls back to <see cref="Title"/>).</summary>
	public string? ShortTitle { get; init; }

	/// <summary>
	/// Optional confirmation copy naming the concrete consequence + target. Supports the
	/// <c>{env}</c> placeholder (replaced with the selected environment). When absent a generic
	/// message is used.
	/// </summary>
	public string? ConfirmText { get; init; }

	/// <summary>Icon glyph or short label shown in the ring.</summary>
	public string Icon { get; init; } = string.Empty;

	/// <summary>Selects which typed block below is active.</summary>
	public required ActionKind Kind { get; init; }

	/// <summary>Populated iff <see cref="Kind"/> == <see cref="ActionKind.ClioCommand"/>.</summary>
	public ClioCommandSpec? ClioCommand { get; init; }

	/// <summary>Populated iff <see cref="Kind"/> == <see cref="ActionKind.OpenUrl"/>.</summary>
	public OpenUrlSpec? OpenUrl { get; init; }

	/// <summary>Populated iff <see cref="Kind"/> == <see cref="ActionKind.OpenPath"/>.</summary>
	public OpenPathSpec? OpenPath { get; init; }

	/// <summary>Parameters prompted for before execution.</summary>
	public IReadOnlyList<ParameterDescriptor> Parameters { get; init; } = new List<ParameterDescriptor>();

	/// <summary>Confirmation gate.</summary>
	public Risk Risk { get; init; } = Risk.None;

	/// <summary>
	/// When true, the confirmation dialog additionally requires the user to type the exact
	/// selected environment name before the action can run — a stronger gate than the default
	/// arm-delay for irreversible operations (e.g. uninstall). Defaults to false.
	/// </summary>
	public bool RequireTypedConfirm { get; init; }

	/// <summary>
	/// Optional explicit consequence line shown prominently in the confirmation dialog (distinct from
	/// <see cref="ConfirmText"/>), for irreversible actions that must spell out the concrete effect —
	/// e.g. "the database will be dropped and application files removed, with no undo". Null = none.
	/// </summary>
	public string? ConsequenceText { get; init; }
}

/// <summary>Root document of <c>actions.json</c>: the full declarative action graph.</summary>
public sealed record ActionCatalog {
	/// <summary>All configured actions.</summary>
	public IReadOnlyList<RingAction> Actions { get; init; } = new List<RingAction>();
}
