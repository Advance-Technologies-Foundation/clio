using System;
using System.Collections.Generic;

namespace ClioRing.Interop;

/// <summary>
/// A parsed global-hotkey gesture: Win32 modifier flags + virtual-key code + a display string.
/// Parsing is culture-invariant and tolerant; an unparneable value yields <c>null</c> so the
/// caller can fall back to <see cref="Default"/>.
/// </summary>
public sealed record HotkeyGesture(uint Modifiers, uint VirtualKey, string Display) {
	private const uint ModAlt = 0x0001;
	private const uint ModControl = 0x0002;
	private const uint ModShift = 0x0004;
	private const uint ModWin = 0x0008;
	private const uint ModNoRepeat = 0x4000;

	/// <summary>Default gesture when config is absent/invalid: Ctrl+Alt+C.</summary>
	public static HotkeyGesture Default { get; } = Parse("Ctrl+Alt+C")!;

	/// <summary>Parses e.g. "Ctrl+Alt+C" or "Ctrl+Shift+Backquote". Returns null if invalid.</summary>
	public static HotkeyGesture? Parse(string? value) {
		if (string.IsNullOrWhiteSpace(value)) {
			return null;
		}

		uint mods = 0;
		uint vk = 0;
		var display = new List<string>();

		foreach (string raw in value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
			switch (raw.ToLowerInvariant()) {
				case "ctrl":
				case "control":
					mods |= ModControl;
					display.Add("Ctrl");
					break;
				case "alt":
					mods |= ModAlt;
					display.Add("Alt");
					break;
				case "shift":
					mods |= ModShift;
					display.Add("Shift");
					break;
				case "win":
				case "windows":
				case "meta":
					mods |= ModWin;
					display.Add("Win");
					break;
				default:
					if (!TryParseKey(raw, out vk, out string keyDisplay)) {
						return null;
					}

					display.Add(keyDisplay);
					break;
			}
		}

		if (vk == 0 || mods == 0) {
			return null; // require at least one modifier + a key
		}

		return new HotkeyGesture(mods | ModNoRepeat, vk, string.Join("+", display));
	}

	private static bool TryParseKey(string token, out uint vk, out string display) {
		vk = 0;
		display = token;

		if (token.Length == 1) {
			char c = char.ToUpperInvariant(token[0]);
			if (c is >= 'A' and <= 'Z') {
				vk = c;
				display = c.ToString();
				return true;
			}

			if (c is >= '0' and <= '9') {
				vk = c;
				display = c.ToString();
				return true;
			}
		}

		switch (token.ToLowerInvariant()) {
			case "space":
				vk = 0x20;
				display = "Space";
				return true;
			case "backquote":
			case "tilde":
			case "`":
				vk = 0xC0; // VK_OEM_3
				display = "`";
				return true;
			case "enter":
			case "return":
				vk = 0x0D;
				display = "Enter";
				return true;
		}

		if ((token.Length == 2 || token.Length == 3)
			&& (token[0] is 'f' or 'F')
			&& int.TryParse(token.AsSpan(1), out int fn)
			&& fn is >= 1 and <= 12) {
			vk = (uint)(0x70 + (fn - 1)); // VK_F1..VK_F12
			display = "F" + fn;
			return true;
		}

		return false;
	}
}
