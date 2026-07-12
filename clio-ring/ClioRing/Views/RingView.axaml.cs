using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClioRing.ViewModels;

namespace ClioRing.Views;

/// <summary>
/// The radial ring view. Scalar/drawer state uses compiled XAML bindings; the two orbits of nodes
/// (inner = environments, outer = actions) are built in code-behind with a single stroke-icon
/// family. The ring geometry is fixed across all states — only node styling changes.
/// </summary>
public partial class RingView : UserControl {
	/// <summary>Disables the staggered entrance animation (headless screenshots + reduced motion).</summary>
	public static bool EnableEntranceAnimation { get; set; } = true;

	/// <summary>Reduced-motion fallback: no entrance stagger, no running pulse.</summary>
	public static bool ReducedMotion { get; set; }

	private readonly List<(RingItemViewModel Item, Border Node)> _nodes = new();
	private readonly List<(RingItemViewModel Item, PropertyChangedEventHandler Handler)> _subscriptions = new();
	private RingViewModel? _boundViewModel;
	private ScrollViewer? _outputScroller;
	private TextBox? _envSearchBox;
	private ListBox? _envList;
	private Button? _hubButton;
	private bool _autoScroll = true;

	/// <summary>Creates the view.</summary>
	public RingView() {
		InitializeComponent();
		DataContextChanged += OnDataContextChanged;
		Loaded += OnLoaded;
	}

	private void InitializeComponent() {
		Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
	}

	private void OnLoaded(object? sender, EventArgs e) {
		_outputScroller = this.FindControl<ScrollViewer>("OutputScroller");
		if (_outputScroller is not null) {
			_outputScroller.ScrollChanged += OnOutputScrollChanged;
		}

		_envSearchBox = this.FindControl<TextBox>("EnvSearchBox");
		if (_envSearchBox is not null) {
			_envSearchBox.KeyDown += OnSearchKeyDown;
		}

		_envList = this.FindControl<ListBox>("EnvList");
		if (_envList is not null) {
			_envList.AddHandler(TappedEvent, OnEnvListTapped, RoutingStrategies.Bubble);
		}

		_hubButton = this.FindControl<Button>("HubButton");

		if (this.FindControl<Button>("BuildBadgeButton") is { } badge) {
			badge.Click += OnBuildBadgeClick;
		}
	}

	private async void OnBuildBadgeClick(object? sender, RoutedEventArgs e) {
		try {
			if (_boundViewModel is not null && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) {
				await clipboard.SetTextAsync(_boundViewModel.BuildIdentity);
			}
		}
		catch (Exception) {
			// Clipboard access is best-effort.
		}
	}

	private void OnSearchKeyDown(object? sender, KeyEventArgs e) {
		if (_boundViewModel is null) {
			return;
		}

		// Ctrl+P pins/unpins the highlighted row WITHOUT changing the active selection.
		if (e.Key == Key.P && e.KeyModifiers.HasFlag(KeyModifiers.Control)) {
			_boundViewModel.TogglePinHighlighted();
			e.Handled = true;
			return;
		}

		switch (e.Key) {
			case Key.Down:
				_boundViewModel.MoveHighlight(1);
				e.Handled = true;
				break;
			case Key.Up:
				_boundViewModel.MoveHighlight(-1);
				e.Handled = true;
				break;
			case Key.Enter:
				_boundViewModel.ConfirmHighlightedCommand.Execute(null);
				e.Handled = true;
				break;
			case Key.Escape:
				// Esc clears the query first; a second Esc (empty query) closes the palette.
				if (_boundViewModel.HasQuery) {
					_boundViewModel.ClearQueryCommand.Execute(null);
				}
				else {
					_boundViewModel.ClosePickerCommand.Execute(null);
				}

				e.Handled = true;
				break;
		}
	}

	private void OnEnvListTapped(object? sender, TappedEventArgs e) {
		if (_boundViewModel is null) {
			return;
		}

		// A tap on the pin star toggles the pin only — do not also select+close.
		if (e.Source is Visual v && v.FindAncestorOfType<Button>() is { } button && button.Classes.Contains("pin")) {
			return;
		}

		_boundViewModel.ConfirmHighlightedCommand.Execute(null);
	}

	private void OnDataContextChanged(object? sender, EventArgs e) {
		if (_boundViewModel is not null) {
			_boundViewModel.LayoutChanged -= RebuildRing;
			_boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
		}

		_boundViewModel = DataContext as RingViewModel;
		if (_boundViewModel is not null) {
			_boundViewModel.LayoutChanged += RebuildRing;
			_boundViewModel.PropertyChanged += OnViewModelPropertyChanged;
			RebuildRing();
		}
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) {
		switch (e.PropertyName) {
			case nameof(RingViewModel.OutputText):
				AutoScrollOutput();
				break;
			case nameof(RingViewModel.Outcome):
				FocusCompletionCloseIfDone();
				break;
			case nameof(RingViewModel.HasPendingConfirm):
				FocusConfirmCancelIfOpen();
				break;
			case nameof(RingViewModel.IsPickerOpen):
				OnPickerOpenChanged();
				break;
			case nameof(RingViewModel.HighlightedIndex):
				ScrollHighlightIntoView();
				break;
		}
	}

	private void OnPickerOpenChanged() {
		if (_boundViewModel is null) {
			return;
		}

		if (_boundViewModel.IsPickerOpen) {
			// Autofocus the search box so typing filters immediately.
			Dispatcher.UIThread.Post(
				() => {
					_envSearchBox?.Focus();
					_envSearchBox?.SelectAll();
				},
				DispatcherPriority.Background);
		}
		else {
			// Restore focus predictably to the hub after selecting/closing.
			Dispatcher.UIThread.Post(() => _hubButton?.Focus(), DispatcherPriority.Background);
		}
	}

	private void ScrollHighlightIntoView() {
		if (_boundViewModel is null || _envList is null) {
			return;
		}

		int index = _boundViewModel.HighlightedIndex;
		if (index >= 0 && index < _boundViewModel.FilteredEnvironments.Count) {
			Dispatcher.UIThread.Post(
				() => _envList.ScrollIntoView(index),
				DispatcherPriority.Background);
		}
	}

	private void RebuildRing() {
		if (_boundViewModel is null || this.FindControl<Canvas>("RingCanvas") is not { } canvas) {
			return;
		}

		DetachSubscriptions();
		canvas.Children.Clear();
		_nodes.Clear();

		int index = 0;
		foreach (RingItemViewModel item in _boundViewModel.Items) {
			Border node = BuildNode(item);
			Canvas.SetLeft(node, item.X);
			Canvas.SetTop(node, item.Y);
			canvas.Children.Add(node);
			_nodes.Add((item, node));

			// Outer (action) nodes get an outward, single-line short label.
			if (item.Orbit == RingOrbit.Outer) {
				var label = new TextBlock { Text = item.Label, Width = 96 };
				label.Classes.Add("nodelabel");
				Canvas.SetLeft(label, item.LabelX);
				Canvas.SetTop(label, item.LabelY);
				canvas.Children.Add(label);
			}

			SubscribeState(item, node);
			ApplyStateClasses(node, item);

			if (EnableEntranceAnimation && !ReducedMotion) {
				_ = RunEntranceAsync(node, index);
			}

			index++;
		}
	}

	private Border BuildNode(RingItemViewModel item) {
		var content = new StackPanel {
			Spacing = 3,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};

		// Icons are drawn on a fixed 24x24 grid. Render at native coords (Stretch=None) inside a
		// fixed 24x24 box, then scale that box uniformly with a Viewbox. This gives EVERY glyph the
		// same scale factor and the same centred origin (so glyphs no longer fill their node to
		// their own bounds and drift), keeping the ring visually consistent.
		double iconSize = item.Orbit == RingOrbit.Inner ? 20 : 26;
		var icon = new Path {
			Data = RingIcons.Get(item.IconKey),
			StrokeThickness = 1.7,
			StrokeLineCap = PenLineCap.Round,
			StrokeJoin = PenLineJoin.Round,
			Stretch = Stretch.None
		};
		icon.Classes.Add("icon");

		var iconBox = new Panel { Width = 24, Height = 24 };
		iconBox.Children.Add(icon);
		var iconView = new Viewbox {
			Width = iconSize,
			Height = iconSize,
			Stretch = Stretch.Uniform,
			Child = iconBox
		};
		content.Children.Add(iconView);

		// Environments carry their short name inside the node; actions use the outward label.
		if (item.Kind == RingItemKind.Environment) {
			var envLabel = new TextBlock { Text = item.Label };
			envLabel.Classes.Add("envlabel");
			content.Children.Add(envLabel);
		}

		var node = new Border {
			Width = item.NodeSize,
			Height = item.NodeSize,
			CornerRadius = new CornerRadius(item.NodeSize / 2.0),
			RenderTransformOrigin = RelativePoint.Center,
			Focusable = true,
			Child = content
		};
		node.Classes.Add("node");
		node.Classes.Add(item.Orbit == RingOrbit.Inner ? "inner" : "outer");

		// Accessible name + tooltip use the full label; the node shows the short one.
		ToolTip.SetTip(node, item.FullLabel);
		AutomationProperties.SetName(node, item.FullLabel);

		RingItemViewModel captured = item;
		node.PointerPressed += (_, _) => Execute(captured);
		node.KeyDown += (_, e) => {
			if (e.Key is Key.Enter or Key.Space) {
				Execute(captured);
				e.Handled = true;
			}
		};

		return node;
	}

	private static void Execute(RingItemViewModel item) {
		if (item.SelectCommand?.CanExecute(item) == true) {
			item.SelectCommand.Execute(item);
		}
	}

	private void SubscribeState(RingItemViewModel item, Border node) {
		PropertyChangedEventHandler handler = (_, e) => {
			if (e.PropertyName is nameof(RingItemViewModel.State) or nameof(RingItemViewModel.Selected)) {
				ApplyStateClasses(node, item);
			}
		};
		item.PropertyChanged += handler;
		_subscriptions.Add((item, handler));
	}

	private void DetachSubscriptions() {
		foreach ((RingItemViewModel item, PropertyChangedEventHandler handler) in _subscriptions) {
			item.PropertyChanged -= handler;
		}

		_subscriptions.Clear();
	}

	private static void ApplyStateClasses(Border node, RingItemViewModel item) {
		node.Classes.Set("running", item.State == NodeState.Running);
		node.Classes.Set("success", item.State == NodeState.Success);
		node.Classes.Set("failure", item.State == NodeState.Failure);
		node.Classes.Set("selected", item.Selected && item.State == NodeState.Idle);
		node.Classes.Set("destructive", item.IsDestructive && item.State == NodeState.Idle);
	}

	/// <summary>Forces the keyboard-focus visual on the nth node (screenshots).</summary>
	public void SetKeyboardFocusNode(int nodeIndex) {
		for (int i = 0; i < _nodes.Count; i++) {
			_nodes[i].Node.Classes.Set("kbfocus", i == nodeIndex);
		}
	}

	private void OnOutputScrollChanged(object? sender, ScrollChangedEventArgs e) {
		if (_outputScroller is null) {
			return;
		}

		// Pause autoscroll when the user scrolls up; resume once back at the bottom.
		double distanceFromBottom =
			_outputScroller.Extent.Height - (_outputScroller.Offset.Y + _outputScroller.Viewport.Height);
		_autoScroll = distanceFromBottom < 6;
	}

	private void AutoScrollOutput() {
		if (!_autoScroll || _outputScroller is null) {
			return;
		}

		Dispatcher.UIThread.Post(
			() => {
				if (_outputScroller is not null) {
					_outputScroller.Offset = new Vector(_outputScroller.Offset.X, _outputScroller.Extent.Height);
				}
			},
			DispatcherPriority.Background);
	}

	private void FocusCompletionCloseIfDone() {
		RingViewModel? viewModel = _boundViewModel;
		if (viewModel is null || viewModel.IsBusy || !viewModel.HasOutcomeBadge) {
			return;
		}

		Dispatcher.UIThread.Post(
			() => this.FindControl<Button>("DrawerCloseButton")?.Focus(),
			DispatcherPriority.Background);
	}

	private void FocusConfirmCancelIfOpen() {
		if (_boundViewModel is null || !_boundViewModel.HasPendingConfirm) {
			return;
		}

		// Default keyboard focus is Cancel (safe default for a destructive prompt).
		Dispatcher.UIThread.Post(
			() => this.FindControl<Button>("ConfirmCancelButton")?.Focus(),
			DispatcherPriority.Background);
	}

	/// <summary>
	/// Staggered fade-in entrance driven by the node's <c>Opacity</c> transition (the animator is
	/// built in — no hand-rolled keyframe Animation, which previously threw "No animator registered
	/// for RenderTransform" on a real windowed launch). We deliberately do NOT set a local
	/// RenderTransform here so the style-driven hover/focus/running transforms keep working.
	/// Fire-and-forget and fully guarded: an optional visual effect must never crash the app.
	/// </summary>
	private static async Task RunEntranceAsync(Border node, int index) {
		try {
			node.Opacity = 0; // set before first paint so it starts hidden, then fades to 1
			await Task.Delay(20 + (index * 28)).ConfigureAwait(true);
			node.Opacity = 1;
		}
		catch (Exception ex) {
			Diagnostics.StartupLog.Log($"entrance animation swallowed: {ex.Message}");
		}
	}
}
