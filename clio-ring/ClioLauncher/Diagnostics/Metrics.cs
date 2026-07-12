using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace ClioLauncher.Diagnostics;

/// <summary>
/// Central, low-overhead instrumentation. All intra-process intervals use
/// <see cref="Stopwatch.GetTimestamp"/> (QPC) for comparability; results are appended as
/// machine-readable CSV rows tagged with machine/runtime/rid/build-mode.
/// </summary>
public static class Metrics {
	private static readonly object Gate = new();

	/// <summary>QPC stamp taken at the very top of the managed entry point.</summary>
	public static long ProcessStartTicks { get; set; }

	/// <summary>QPC stamp of the most recent native WM_HOTKEY callback entry.</summary>
	public static long HotkeyReceivedTicks { get; set; }

	/// <summary>Build mode: "AOT" when dynamic code is unsupported (NativeAOT), else "JIT".</summary>
	public static string BuildMode =>
		RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "AOT";

	/// <summary>Runtime description, e.g. ".NET 10.0.x".</summary>
	public static string Runtime => RuntimeInformation.FrameworkDescription;

	/// <summary>Runtime identifier, e.g. "win-x64".</summary>
	public static string Rid => RuntimeInformation.RuntimeIdentifier;

	/// <summary>Machine name.</summary>
	public static string Machine => Environment.MachineName;

	/// <summary>Directory measurement CSVs are written to (override via CLIO_RING_MEASUREMENTS).</summary>
	public static string MeasurementsDirectory {
		get {
			string? overridden = Environment.GetEnvironmentVariable("CLIO_RING_MEASUREMENTS");
			return string.IsNullOrWhiteSpace(overridden)
				? Path.Combine(AppContext.BaseDirectory, "measurements")
				: overridden;
		}
	}

	/// <summary>Elapsed milliseconds between two QPC stamps.</summary>
	public static double ElapsedMs(long fromTicks, long toTicks) =>
		(toTicks - fromTicks) * 1000.0 / Stopwatch.Frequency;

	/// <summary>Elapsed milliseconds from <paramref name="fromTicks"/> to now.</summary>
	public static double ElapsedMsSince(long fromTicks) =>
		ElapsedMs(fromTicks, Stopwatch.GetTimestamp());

	/// <summary>Common CSV column suffix: machine,runtime,rid,build-mode.</summary>
	public static string ContextColumns() =>
		$"{Escape(Machine)},{Escape(Runtime)},{Escape(Rid)},{BuildMode}";

	/// <summary>Appends one row to <paramref name="fileName"/> inside the measurements directory, writing <paramref name="header"/> first if the file is new.</summary>
	public static void AppendRow(string fileName, string header, string row) {
		lock (Gate) {
			string dir = MeasurementsDirectory;
			Directory.CreateDirectory(dir);
			string path = Path.Combine(dir, fileName);
			bool exists = File.Exists(path);
			using var writer = new StreamWriter(path, append: true, Encoding.UTF8);
			if (!exists) {
				writer.WriteLine(header);
			}
			writer.WriteLine(row);
		}
	}

	/// <summary>Records a cold-start sample (process-start -> first-paint).</summary>
	public static void RecordColdStart(double coldMs) {
		AppendRow(
			"startup.csv",
			"utc,machine,runtime,rid,build_mode,cold_ms",
			$"{Now()},{ContextColumns()},{Fmt(coldMs)}");
	}

	/// <summary>Records a warm hotkey sample (hotkey-received -> interactive).</summary>
	public static void RecordHotkey(int sampleIndex, double hotkeyMs) {
		AppendRow(
			"hotkey.csv",
			"utc,machine,runtime,rid,build_mode,sample,hotkey_ms",
			$"{Now()},{ContextColumns()},{sampleIndex},{Fmt(hotkeyMs)}");
	}

	private static string Now() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

	private static string Fmt(double value) => value.ToString("F3", CultureInfo.InvariantCulture);

	private static string Escape(string value) => value.Contains(',') ? $"\"{value}\"" : value;
}
