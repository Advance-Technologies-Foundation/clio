using System.Collections.Generic;
using Avalonia.Media;

namespace ClioLauncher.Views;

/// <summary>
/// One coherent stroke-based (outline) icon family for ring nodes. Geometries are drawn on a
/// 24x24 canvas and rendered with a stroke (no fill) so they stay legible at small radial sizes
/// and in either theme. Colour is applied by the view to encode STATE, never per-icon.
/// </summary>
public static class RingIcons {
	private static readonly Dictionary<string, Geometry> Cache = new();

	private static readonly Dictionary<string, string> Paths = new() {
		// Environment glyph (single, consistent): globe outline.
		["globe"] = "M12 3 A9 9 0 1 0 12 21 A9 9 0 1 0 12 3 M3 12 L21 12 " +
					"M12 3 C6.8 6 6.8 18 12 21 M12 3 C17.2 6 17.2 18 12 21",
		// info (i in a circle)
		["info"] = "M12 3 A9 9 0 1 0 12 21 A9 9 0 1 0 12 3 M12 11 L12 16.5 M12 7.6 L12 8.4",
		// tag / version
		["tag"] = "M4 11 L11 4 L20 4 L20 13 L13 20 L4 11 Z M15.5 8 L15.6 8",
		// package (cube outline)
		["package"] = "M12 3 L20 7.5 L20 16.5 L12 21 L4 16.5 L4 7.5 Z M4 7.5 L12 12 L20 7.5 M12 12 L12 21",
		// document / docs (centred on 24x24)
		["docs"] = "M6 3 L14 3 L18 7 L18 21 L6 21 Z M14 3 L14 7 L18 7 M9 12 L15 12 M9 16 L13 16",
		// folder (centred on 24x24)
		["folder"] = "M3 6 L9.5 6 L11.5 8 L21 8 L21 18 L3 18 Z",
		// restart / refresh (circular arrow, arc centred on 12,12 r7)
		["restart"] = "M18.5 9.2 A7 7 0 1 0 19 13 M18.5 9.2 L18.9 5.3 M18.5 9.2 L14.6 9.6",
		// trash / delete (lid + handle, tapered body, two ribs) — outline family
		["trash"] = "M4 6.5 L20 6.5 M9.5 6.5 L9.5 4.5 L14.5 4.5 L14.5 6.5 " +
					"M6 6.5 L7 20 L17 20 L18 6.5 M10 10 L10.4 16.5 M14 10 L13.6 16.5",
		// end-state marks
		["check"] = "M5 12.5 L10 17.5 L19 6.5",
		["close"] = "M6.5 6.5 L17.5 17.5 M17.5 6.5 L6.5 17.5",
		// generic fallback
		["dot"] = "M12 8 A4 4 0 1 0 12 16 A4 4 0 1 0 12 8"
	};

	/// <summary>Returns the geometry for <paramref name="key"/>, or the generic dot when unknown.</summary>
	public static Geometry Get(string? key) {
		string k = string.IsNullOrWhiteSpace(key) ? "dot" : key;
		if (Cache.TryGetValue(k, out Geometry? cached)) {
			return cached;
		}

		string data = Paths.TryGetValue(k, out string? path) ? path : Paths["dot"];
		Geometry geo = Geometry.Parse(data);
		Cache[k] = geo;
		return geo;
	}
}
