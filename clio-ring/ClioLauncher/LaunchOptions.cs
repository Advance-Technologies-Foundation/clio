using System;

namespace ClioLauncher;

/// <summary>
/// Parsed command-line switches controlling the measurement harness modes.
/// Defaults produce a normal, resident, interactive launcher.
/// </summary>
public sealed record LaunchOptions {
	/// <summary>The options for the current process (set once in <c>Main</c>).</summary>
	public static LaunchOptions Current { get; set; } = new();

	/// <summary>Number of automated warm hotkey samples to run after first paint (0 = off).</summary>
	public int BenchHotkeySamples { get; init; }

	/// <summary>When true, runs one clio call after startup so an external sampler can capture peak RSS.</summary>
	public bool AutoClio { get; init; }

	/// <summary>When true, exits shortly after recording first paint (used by the cold-start loop).</summary>
	public bool ExitAfterPaint { get; init; }

	/// <summary>
	/// Real-window startup smoke test: shows the window (exercising the live render/entrance path),
	/// then exits 0 after a short settle. Verifies the windowed path headless capture cannot.
	/// </summary>
	public bool Smoke { get; init; }

	/// <summary>Reduced-motion fallback: no entrance stagger; final state renders synchronously.</summary>
	public bool ReducedMotion { get; init; }

	/// <summary>Interaction soak test: N show/hide/tray/hotkey cycles + a clio run, then assert + exit.</summary>
	public int SoakCycles { get; init; }

	/// <summary>Placement harness: move the window to these pixel coords, persist, then exit (null = off).</summary>
	public int? PlaceX { get; init; }

	/// <summary>Placement harness Y (see <see cref="PlaceX"/>).</summary>
	public int? PlaceY { get; init; }

	/// <summary>Hot-reload harness: edit actions.json (valid then invalid), assert live behaviour, exit.</summary>
	public bool HotReloadTest { get; init; }

	/// <summary>True for non-interactive harness modes that should bypass the single-instance guard.</summary>
	public bool IsHarnessMode => Smoke || ExitAfterPaint || AutoClio || BenchHotkeySamples > 0 || SoakCycles > 0 || PlaceX.HasValue || HotReloadTest;

	/// <summary>Parses the supported switches, ignoring anything else.</summary>
	public static LaunchOptions Parse(string[] args) {
		int benchHotkey = 0;
		bool autoClio = false;
		bool exitAfterPaint = false;
		bool smoke = false;
		bool reducedMotion = false;
		int soak = 0;
		int? placeX = null;
		int? placeY = null;
		bool hotReloadTest = false;

		for (int i = 0; i < args.Length; i++) {
			switch (args[i]) {
				case "--bench-hotkey" when i + 1 < args.Length && int.TryParse(args[i + 1], out int n):
					benchHotkey = n;
					i++;
					break;
				case "--auto-clio":
					autoClio = true;
					break;
				case "--exit-after-paint":
					exitAfterPaint = true;
					break;
				case "--smoke":
					smoke = true;
					break;
				case "--reduced-motion":
					reducedMotion = true;
					break;
				case "--soak" when i + 1 < args.Length && int.TryParse(args[i + 1], out int cycles):
					soak = cycles;
					i++;
					break;
				case "--place" when i + 2 < args.Length
					&& int.TryParse(args[i + 1], out int px) && int.TryParse(args[i + 2], out int py):
					placeX = px;
					placeY = py;
					i += 2;
					break;
				case "--hotreload-test":
					hotReloadTest = true;
					break;
			}
		}

		return new LaunchOptions {
			BenchHotkeySamples = benchHotkey,
			AutoClio = autoClio,
			ExitAfterPaint = exitAfterPaint,
			Smoke = smoke,
			ReducedMotion = reducedMotion,
			SoakCycles = soak,
			PlaceX = placeX,
			PlaceY = placeY,
			HotReloadTest = hotReloadTest
		};
	}
}
