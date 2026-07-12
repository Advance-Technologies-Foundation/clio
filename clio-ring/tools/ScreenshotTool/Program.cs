using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClioRing;
using ClioRing.Services;
using ClioRing.ViewModels;
using ClioRing.Views;

namespace ScreenshotTool;

/// <summary>
/// Offscreen renderer: boots Avalonia on the headless platform with real Skia drawing and renders
/// the ring window in each interaction state at 1x and 1.5x, composited over an opaque dark matte
/// so PNGs are viewer-independent. No visible window, no blocking.
/// </summary>
internal static class Program {
	private static readonly Color MatteColor = Color.Parse("#0B0F14");
	private static readonly double[] Scales = { 1.0, 1.5 };

	[STAThread]
	private static int Main(string[] args) {
		Environment.SetEnvironmentVariable(
			"CLIO_RING_MEASUREMENTS",
			Path.Combine(Path.GetTempPath(), "clio-ring-screenshots"));

		BuildAvaloniaApp().SetupWithoutStarting();

		// Icon-generation mode: render the ring glyph and assemble a multi-size .ico, then exit.
		if (args.Length >= 1 && args[0] == "--make-icon") {
			string icoPath = args.Length >= 2
				? args[1]
				: Path.Combine(AppContext.BaseDirectory, "clio-ring.ico");
			Dispatcher.UIThread.Invoke(() => IconGenerator.Write(icoPath));
			Console.WriteLine($"Wrote icon {icoPath}");
			return 0;
		}

		// Workflow / deploy-wizard capture mode (experimental IPC surface). Renders those windows via
		// design seams — no clio child, no network.
		if (args.Length >= 1 && args[0] == "--workflow") {
			string wfDir = args.Length >= 2 ? args[1] : Path.Combine(AppContext.BaseDirectory, "screenshots");
			Directory.CreateDirectory(wfDir);
			int wf = 0;
			Dispatcher.UIThread.Invoke(() => wf = WorkflowCaptures.CaptureAll(wfDir));
			Console.WriteLine($"Wrote {wf} workflow PNG(s) to {wfDir}");
			return wf > 0 ? 0 : 1;
		}

		string outDir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "screenshots");
		Directory.CreateDirectory(outDir);

		// Static resting frame per capture (no entrance stagger, no pulse).
		RingView.EnableEntranceAnimation = false;

		int count = 0;
		Dispatcher.UIThread.Invoke(() => count = CaptureAll(outDir));

		Console.WriteLine($"Wrote {count} PNG(s) to {outDir}");
		return count > 0 ? 0 : 1;
	}

	private static AppBuilder BuildAvaloniaApp() =>
		AppBuilder.Configure<App>()
			.UseSkia()
			.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
			.WithInterFont();

	private static int CaptureAll(string outDir) {
		int count = 0;

		// 1) DEFAULT — compact, ring only, calm.
		count += CaptureState(outDir, "default", _ => { });

		// 2) FOCUSED — keyboard focus on an action node (strong accent ring).
		count += CaptureState(outDir, "focused", (window) => {
			View(window).SetKeyboardFocusNode(2);
		});

		// 2b) PICKER-OPEN — env palette with a highlighted row + typed query filtering the 23-env set.
		//     Rendered at 1x, 1.5x AND 2x to confirm no clipping / focus visibility at high DPI.
		Action<Window> pickerArrange = (window) => {
			RingViewModel vm = Vm(window);
			vm.TogglePinCommand.Execute("work");   // demonstrate a pinned favourite
			vm.OpenPickerCommand.Execute(null);
			vm.SearchQuery = "a";                  // fuzzy filter across all envs
			vm.MoveHighlight(1);                   // move highlight onto a RESULTS row
		};
		count += Capture(outDir, "picker", 1.0, pickerArrange);
		count += Capture(outDir, "picker", 1.5, pickerArrange);
		count += Capture(outDir, "picker", 2.0, pickerArrange);

		// 3) RUNNING — drawer open, active env node glowing, RUNNING badge.
		count += CaptureState(outDir, "running", (window) => {
			RingViewModel vm = Vm(window);
			MarkEnv(vm, NodeState.Running);
			vm.DesignShowOutput(
				"clio get-info -e ve",
				RunOutcome.Running,
				"clio 8.1.0.76\r\nConnecting to environment 've'...\r\nAuthenticating...");
		});

		// 4) SUCCESS — completed run, green node + SUCCESS badge.
		count += CaptureState(outDir, "success", (window) => {
			RingViewModel vm = Vm(window);
			MarkEnv(vm, NodeState.Success);
			vm.DesignShowOutput(
				"clio get-info -e ve",
				RunOutcome.Success,
				"clio 8.1.0.76\r\nEnvironment: ve\r\nProductName: Studio Enterprise\r\n" +
				"Db: PostgreSQL 15.3\r\nFramework: NetCore\r\nExit 0");
		});

		// 5) FAILURE — failed run, red node + FAILED badge.
		count += CaptureState(outDir, "failure", (window) => {
			RingViewModel vm = Vm(window);
			MarkEnv(vm, NodeState.Failure);
			vm.DesignShowOutput(
				"clio get-info -e ve",
				RunOutcome.Failure,
				"clio 8.1.0.76\r\nConnecting to environment 've'...\r\n! Connection refused (401 Unauthorized)\r\nExit 1");
		});

		// 6) DESTRUCTIVE CONFIRM — overlay dialog (1x only).
		count += Capture(outDir, "destructive-confirm", 1.0, (window) => {
			RingViewModel vm = Vm(window);
			RingItemViewModel? destructive = vm.Items.FirstOrDefault(i => i.IsDestructive);
			if (destructive?.SelectCommand?.CanExecute(destructive) == true) {
				destructive.SelectCommand.Execute(destructive);
			}
		});

		// 7) HOTKEY CONFLICT — loud non-modal notice banner (1x only).
		count += Capture(outDir, "hotkey-conflict", 1.0, (window) => {
			RingViewModel vm = Vm(window);
			vm.SetHotkeyInfo("Ctrl+Alt+C", @"%LOCALAPPDATA%\..\app-settings.json");
			vm.SetHotkeyNotice("Hotkey Ctrl+Alt+C is unavailable (already in use). Change it in Settings.");
		});

		// 8) SETTINGS / HOTKEY panel (1x only).
		count += Capture(outDir, "settings", 1.0, (window) => {
			RingViewModel vm = Vm(window);
			vm.SetHotkeyInfo("Ctrl+Alt+C", @"C:\Projects\clio\clio-ring\ClioRing.Desktop\app-settings.json");
			vm.OpenSettingsCommand.Execute(null);
		});

		// 9) COLLAPSED OUTPUT BAR — output collapsed to the persistent, re-expandable bar.
		count += CaptureState(outDir, "collapsed-output", (window) => {
			RingViewModel vm = Vm(window);
			MarkEnv(vm, NodeState.Success);
			vm.DesignShowOutput(
				"clio get-info -e ve",
				RunOutcome.Success,
				"clio 8.1.0.76\r\nEnvironment: ve\r\nExit 0");
			vm.CollapseOutputCommand.Execute(null);
		});

		// 10) INVALID CONFIG — load-time actions.json validation error notice (1x only).
		count += Capture(outDir, "invalid-config", 1.0, (window) => {
			RingViewModel vm = Vm(window);
			vm.DesignSetCatalogError(
				"Action 'clio-restart' in 'actions.json' declares Kind=OpenUrl but its 'OpenUrl' block is missing.");
		});

		return count;
	}

	private static int CaptureState(string outDir, string name, Action<Window> arrange) {
		int n = 0;
		foreach (double scale in Scales) {
			n += Capture(outDir, name, scale, arrange);
		}

		return n;
	}

	private static int Capture(string outDir, string name, double scale, Action<Window> arrange) {
		RingViewModel vm = new(new SampleClioAdapter(), new ActionCatalogLoader(), new InMemoryEnvStateStore(), new NullActionCatalogWatcher());
		var window = new RingWindow(vm, new LaunchOptions(), ClioRing.Interop.HotkeyGesture.Default, new InMemoryWindowPlacementStore());
		window.Show();
		Pump();

		arrange(window);
		Pump();

		string suffix = Math.Abs(scale - 1.0) < 0.0001 ? "@1x" : "@1.5x";
		RenderTargetBitmap? rtb = RenderMatted(window, scale);
		window.Close();
		if (rtb is null) {
			return 0;
		}

		string path = Path.Combine(outDir, $"{name}{suffix}.png");
		rtb.Save(path, new PngBitmapEncoderOptions());
		Console.WriteLine($"Saved {path} ({rtb.PixelSize.Width}x{rtb.PixelSize.Height})");

		// Format-compatibility diagnostic: export the two states some viewers decoded as
		// partial (focused, failure) at 1x also as JPEG + BMP. Not a UI decision.
		if (Math.Abs(scale - 1.0) < 0.0001 && (name == "focused" || name == "failure")) {
			string diag = Path.Combine(outDir, "diag");
			Directory.CreateDirectory(diag);
			rtb.Save(Path.Combine(diag, $"{name}.jpg"), new JpegBitmapEncoderOptions { Quality = 92 });
			SaveBmp(rtb, Path.Combine(diag, $"{name}.bmp"));
			Console.WriteLine($"Diag: wrote {name}.jpg + {name}.bmp");
		}

		return 1;
	}

	internal static RenderTargetBitmap? RenderMatted(Window window, double scale) {
		if (window.Content is not Control root) {
			return null;
		}

		Size sz = root.Bounds.Size;
		if (sz.Width < 1 || sz.Height < 1) {
			sz = root.DesiredSize;
		}

		var px = new PixelSize(
			Math.Max(1, (int)Math.Ceiling(sz.Width * scale)),
			Math.Max(1, (int)Math.Ceiling(sz.Height * scale)));
		var dpi = new Vector(96 * scale, 96 * scale);

		// Render the (translucent, transparent-margin) content, then matte-composite.
		var content = new RenderTargetBitmap(px, dpi);
		content.Render(root);

		var final = new RenderTargetBitmap(px, dpi);
		var dipRect = new Rect(0, 0, sz.Width, sz.Height);
		using (DrawingContext ctx = final.CreateDrawingContext()) {
			ctx.FillRectangle(new SolidColorBrush(MatteColor), dipRect);
			ctx.DrawImage(content, dipRect);
		}

		return final;
	}

	// Minimal 32-bit top-down BMP writer (Skia has no BMP encoder). Pixels are BGRA8888 and,
	// because we matte over an opaque colour, alpha is 255 so premultiplied == straight.
	private static void SaveBmp(RenderTargetBitmap rtb, string path) {
		int w = rtb.PixelSize.Width;
		int h = rtb.PixelSize.Height;
		int stride = w * 4;
		int size = stride * h;

		var buffer = new byte[size];
		GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
		try {
			rtb.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), size, stride);
		}
		finally {
			handle.Free();
		}

		const int headerSize = 14 + 40;
		using var fs = File.Create(path);
		using var bw = new BinaryWriter(fs);
		bw.Write((byte)'B');
		bw.Write((byte)'M');
		bw.Write(headerSize + size); // file size
		bw.Write(0);                 // reserved
		bw.Write(headerSize);        // pixel data offset
		bw.Write(40);                // BITMAPINFOHEADER size
		bw.Write(w);
		bw.Write(-h);                // negative => top-down
		bw.Write((short)1);          // planes
		bw.Write((short)32);         // bits per pixel
		bw.Write(0);                 // BI_RGB
		bw.Write(size);
		bw.Write(2835);              // ~72 dpi (px/m)
		bw.Write(2835);
		bw.Write(0);
		bw.Write(0);
		bw.Write(buffer);
	}

	private static RingView View(Window window) =>
		window.GetVisualDescendants().OfType<RingView>().First();

	private static RingViewModel Vm(Window window) => (RingViewModel)window.DataContext!;

	private static void MarkEnv(RingViewModel vm, NodeState state) {
		RingItemViewModel? env = vm.Items.FirstOrDefault(i => i.Kind == RingItemKind.Environment);
		if (env is not null) {
			env.State = state;
		}
	}

	internal static void Pump() {
		for (int i = 0; i < 8; i++) {
			Dispatcher.UIThread.RunJobs();
		}
	}
}
