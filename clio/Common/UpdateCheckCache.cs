using System;
using System.IO;
using Newtonsoft.Json;

namespace Clio.Common;

public class UpdateCheckCache {

	public DateTime LastCheckedUtc { get; set; }

	public string LatestVersion { get; set; }

	private const string CacheFileName = "update-check.json";

	private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(8);

	public bool IsCheckDue() => DateTime.UtcNow - LastCheckedUtc > CheckInterval;

	public static UpdateCheckCache Load(string folder) {
		try {
			string path = Path.Combine(folder, CacheFileName);
			if (!File.Exists(path)) {
				return new UpdateCheckCache();
			}
			string json = File.ReadAllText(path);
			return JsonConvert.DeserializeObject<UpdateCheckCache>(json) ?? new UpdateCheckCache();
		} catch {
			return new UpdateCheckCache();
		}
	}

	public static void Save(string folder, UpdateCheckCache cache) {
		try {
			if (!Directory.Exists(folder)) {
				Directory.CreateDirectory(folder);
			}
			string path = Path.Combine(folder, CacheFileName);
			string json = JsonConvert.SerializeObject(cache, Formatting.Indented);
			File.WriteAllText(path, json);
		} catch {
			// cache write failure must never break the tool
		}
	}

}
