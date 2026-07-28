using System.Windows.Input;
using ClioRing.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClioRing.ViewModels;

/// <summary>Kind of thing a ring item represents.</summary>
public enum RingItemKind {
	/// <summary>A declarative catalog action.</summary>
	Action,

	/// <summary>A discovered clio environment (selecting it runs the info verb).</summary>
	Environment
}

/// <summary>Which concentric orbit a node sits on. Categories have stable, separate orbits.</summary>
public enum RingOrbit {
	/// <summary>Inner orbit — environments.</summary>
	Inner,

	/// <summary>Outer orbit — actions.</summary>
	Outer
}

/// <summary>
/// Transient run state of a node. Accent/colour encodes this, not per-icon styling.
/// Hover and keyboard focus are handled by the view (pseudo-classes), not this enum.
/// </summary>
public enum NodeState {
	/// <summary>Resting.</summary>
	Idle,

	/// <summary>Its action is currently running.</summary>
	Running,

	/// <summary>Last run succeeded.</summary>
	Success,

	/// <summary>Last run failed.</summary>
	Failure
}

/// <summary>A single selectable node arranged on the ring.</summary>
public partial class RingItemViewModel : ViewModelBase {
	/// <summary>Creates a ring item for a catalog action (outer orbit).</summary>
	public static RingItemViewModel ForAction(RingAction action, ICommand select) => new() {
		Kind = RingItemKind.Action,
		Orbit = RingOrbit.Outer,
		NodeSize = 58,
		Label = string.IsNullOrWhiteSpace(action.ShortTitle) ? action.Title : action.ShortTitle!,
		FullLabel = action.Title,
		IconKey = string.IsNullOrWhiteSpace(action.Icon) ? "dot" : action.Icon,
		Action = action,
		SelectCommand = select,
		IsDestructive = action.Risk == Risk.Destructive
	};

	/// <summary>Creates a ring item for an environment (inner orbit).</summary>
	public static RingItemViewModel ForEnvironment(string environmentName, ICommand select) => new() {
		Kind = RingItemKind.Environment,
		Orbit = RingOrbit.Inner,
		NodeSize = 54,
		Label = environmentName,
		FullLabel = $"Environment: {environmentName}",
		IconKey = "globe",
		EnvironmentName = environmentName,
		SelectCommand = select
	};

	/// <summary>What this item represents.</summary>
	public RingItemKind Kind { get; private init; }

	/// <summary>Which concentric orbit the node sits on.</summary>
	public RingOrbit Orbit { get; private init; }

	/// <summary>Node diameter in DIPs.</summary>
	public double NodeSize { get; private init; }

	/// <summary>Concise, single-line primary label shown on the node.</summary>
	public string Label { get; private init; } = string.Empty;

	/// <summary>Full label for tooltip + accessible (AutomationProperties) name.</summary>
	public string FullLabel { get; private init; } = string.Empty;

	/// <summary>Icon family key (resolved to a stroke geometry by the view).</summary>
	public string IconKey { get; private init; } = "dot";

	/// <summary>The catalog action (when <see cref="Kind"/> is <see cref="RingItemKind.Action"/>).</summary>
	public RingAction? Action { get; private init; }

	/// <summary>The environment name (when <see cref="Kind"/> is <see cref="RingItemKind.Environment"/>).</summary>
	public string? EnvironmentName { get; private init; }

	/// <summary>Command that selects/runs this item (bound to the parent view-model's select command).</summary>
	public ICommand? SelectCommand { get; private init; }

	/// <summary>Whether selecting this node is destructive (drives the confirm dialog + styling).</summary>
	public bool IsDestructive { get; private init; }

	/// <summary>Transient visual state (drives colour/glow via the view's style classes).</summary>
	[ObservableProperty]
	private NodeState _state = NodeState.Idle;

	/// <summary>Whether this environment is the persistent selected target (restrained treatment).</summary>
	[ObservableProperty]
	private bool _selected;

	/// <summary>Canvas X of the node's top-left, computed by the radial layout.</summary>
	[ObservableProperty]
	private double _x;

	/// <summary>Canvas Y of the node's top-left, computed by the radial layout.</summary>
	[ObservableProperty]
	private double _y;

	/// <summary>Canvas X of the outward label (outer orbit only).</summary>
	[ObservableProperty]
	private double _labelX;

	/// <summary>Canvas Y of the outward label (outer orbit only).</summary>
	[ObservableProperty]
	private double _labelY;
}
