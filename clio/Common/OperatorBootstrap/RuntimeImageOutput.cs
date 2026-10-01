using System;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Common.OperatorBootstrap;

/// <summary>One row per distribution, with exact references for subsequent runtime creation.</summary>
internal static class RuntimeImageOutput {
	internal static JArray Distributions(JArray images) {
		var rows = new JArray();
		foreach (var group in images.Where(i => !string.IsNullOrWhiteSpace(i.Value<string>("tag")))
			.GroupBy(i => i.Value<string>("tag"), StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Key, StringComparer.OrdinalIgnoreCase)) {
			var variants = group.Where(i => i.Value<bool?>("isDatabaseImage") != true &&
				Regex.IsMatch(i.Value<string>("repository") ?? "", @"(^|/)creatio-(dev|prod)$"))
				.OrderBy(i => i.Value<string>("reference"), StringComparer.Ordinal).ToArray();
			if (variants.Length == 0) continue;
			string tag = group.Key;
			string version = Regex.Match(tag, @"^\d+(?:\.\d+)+").Value;
			string product = Regex.Replace(tag, @"^\d+(?:\.\d+)+_", "");
			product = Regex.Split(product, @"net(?:10|8)?_", RegexOptions.IgnoreCase)[0].TrimEnd('_');
			string runtime = variants.Select(i => i["creatioLabels"]?.Value<string>("org.creatio.runtime.dotnet")).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
			runtime = runtime == null ? Regex.IsMatch(tag, @"net8_", RegexOptions.IgnoreCase) ? ".NET 8" : Regex.IsMatch(tag, @"net(?:10)?_", RegexOptions.IgnoreCase) ? ".NET 10" : "Unknown" : ".NET " + runtime.Split('.')[0];
			var db = new JArray(group.Where(i => i.Value<bool?>("isDatabaseImage") == true).Select(i => i.Value<string>("reference")).Distinct());
			bool ready = group.Any(i => i.Value<string>("templateStatus") == "ready");
			rows.Add(new JObject {
				["tag"] = tag, ["product"] = product, ["version"] = version, ["runtime"] = runtime,
				["databaseImages"] = db, ["databaseTemplateReady"] = ready, ["deployable"] = db.Count > 0 || ready,
				["images"] = new JArray(variants.GroupBy(i => i.Value<string>("reference")).Select(g => new JObject {
					["variant"] = g.First().Value<string>("repository").Split('/').Last().Replace("creatio-", ""),
					["reference"] = g.Key
				}))
			});
		}
		return rows;
	}
	internal static void Write(JArray images, bool json, ILogger logger) {
		JArray rows = Distributions(images);
		if (json) { logger.WriteLine(rows.ToString(Formatting.Indented)); return; }
		if (rows.Count == 0) { logger.WriteLine("No built Creatio distributions found."); return; }
		string[] headers = ["Product", "Version", "Runtime", "Deployable", "DB template"];
		var cells = rows.Select(row => new[] { row.Value<string>("product"), row.Value<string>("version"), row.Value<string>("runtime"), row.Value<bool>("deployable") ? "Yes" : "No", row.Value<bool>("databaseTemplateReady") ? "Ready" : "Not prepared" }).ToArray();
		int[] widths = headers.Select((header, i) => Math.Max(header.Length, cells.Max(row => row[i].Length))).ToArray();
		string Line(string[] values) => string.Join(" | ", values.Select((value, i) => value.PadRight(widths[i])));
		logger.WriteLine(Line(headers));
		logger.WriteLine(string.Join("-+-", widths.Select(width => new string('-', width))));
		foreach (var row in cells) logger.WriteLine(Line(row));
		foreach (var row in rows) {
			logger.WriteLine($"{row["tag"]}");
			foreach (var image in row["images"]) logger.WriteLine($"  {image["variant"]}: {image["reference"]}");
			foreach (var db in row["databaseImages"]) logger.WriteLine($"  database: {db}");
		}
	}
}
