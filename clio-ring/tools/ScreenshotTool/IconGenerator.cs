using System;
using System.IO;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ScreenshotTool;

/// <summary>
/// Renders the "clio ring" glyph (orange accent ring + centre dot on a dark rounded tile) at
/// several sizes and assembles them into a multi-size Windows <c>.ico</c> (PNG-compressed entries,
/// valid on Windows Vista+). Used for the EXE/window/tray icon so the app shows its own identity.
/// </summary>
internal static class IconGenerator {
	private static readonly int[] Sizes = { 16, 20, 24, 32, 48, 64, 128, 256 };

	public static void Write(string path) {
		string? dir = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(dir)) {
			Directory.CreateDirectory(dir);
		}

		var pngs = new byte[Sizes.Length][];
		for (int i = 0; i < Sizes.Length; i++) {
			pngs[i] = RenderPng(Sizes[i]);
		}

		using var fs = File.Create(path);
		using var bw = new BinaryWriter(fs);

		// ICONDIR
		bw.Write((ushort)0);              // reserved
		bw.Write((ushort)1);              // type = icon
		bw.Write((ushort)Sizes.Length);   // image count

		int offset = 6 + (16 * Sizes.Length);
		for (int i = 0; i < Sizes.Length; i++) {
			int size = Sizes[i];
			bw.Write((byte)(size >= 256 ? 0 : size)); // width (0 => 256)
			bw.Write((byte)(size >= 256 ? 0 : size)); // height
			bw.Write((byte)0);                        // palette colours
			bw.Write((byte)0);                        // reserved
			bw.Write((ushort)1);                      // colour planes
			bw.Write((ushort)32);                     // bits per pixel
			bw.Write((uint)pngs[i].Length);           // bytes in resource
			bw.Write((uint)offset);                   // image offset
			offset += pngs[i].Length;
		}

		foreach (byte[] png in pngs) {
			bw.Write(png);
		}
	}

	private static byte[] RenderPng(int size) {
		var rtb = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
		double s = size;
		using (DrawingContext ctx = rtb.CreateDrawingContext()) {
			var bg = new SolidColorBrush(Color.Parse("#12161F"));
			var accent = new SolidColorBrush(Color.Parse("#F97316"));

			double corner = s * 0.22;
			ctx.DrawRectangle(bg, null, new RoundedRect(new Rect(0, 0, s, s), corner));

			var center = new Point(s / 2.0, s / 2.0);
			double ringThickness = Math.Max(1.5, s * 0.10);
			var pen = new Pen(accent, ringThickness);
			double radius = s * 0.30;
			ctx.DrawEllipse(null, pen, center, radius, radius);

			ctx.DrawEllipse(accent, null, center, s * 0.075, s * 0.075);
		}

		using var ms = new MemoryStream();
		rtb.Save(ms, new PngBitmapEncoderOptions());
		return ms.ToArray();
	}
}
