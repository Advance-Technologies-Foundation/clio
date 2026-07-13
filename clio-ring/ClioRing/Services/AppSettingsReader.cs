using System;
using System.IO;
using System.Text.Json;
using ClioRing.Models;

namespace ClioRing.Services;

/// <summary>
/// Tolerant reader for <c>app-settings.json</c> next to the executable. Unlike
/// <see cref="WorkspaceService.LoadSettings"/> (which throws for workspace flows), this never
/// throws — it is used at startup to resolve optional settings like the hotkey.
/// </summary>
public static class AppSettingsReader {
	/// <summary>The resolved path to <c>app-settings.json</c> beside the executable.</summary>
	public static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "app-settings.json");

	/// <summary>Reads settings, returning null on any missing-file/parse error.</summary>
	public static AppSettings? TryRead() {
		try {
			if (!File.Exists(SettingsPath)) {
				return null;
			}

			string json = File.ReadAllText(SettingsPath);
			return JsonSerializer.Deserialize(json, RingJsonContext.Default.AppSettings);
		}
		catch (Exception) {
			return null;
		}
	}
}
