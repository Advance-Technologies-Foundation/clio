namespace ClioLauncher.Models;

/// <summary>
/// Persisted window position, scoped to a specific monitor + DPI so a restored position is only
/// applied when that monitor still exists; otherwise the launcher re-centres. Coordinates are in
/// screen pixels (matching <c>Window.Position</c>).
/// </summary>
public sealed class WindowPlacement {
	/// <summary>Left position in screen pixels.</summary>
	public int X { get; set; }

	/// <summary>Top position in screen pixels.</summary>
	public int Y { get; set; }

	/// <summary>Identifies the monitor the position belongs to (its pixel bounds).</summary>
	public string ScreenKey { get; set; } = string.Empty;

	/// <summary>Monitor scaling at save time (diagnostic; part of the DPI scope).</summary>
	public double Scaling { get; set; } = 1.0;
}
