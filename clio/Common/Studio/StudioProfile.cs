using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Common.Studio;

/// <summary>Reads the portable contract without requiring a cluster or a source checkout.</summary>
public static class StudioProfile {

	/// <summary>The supported handoff format.</summary>
	public const string Schema = "creatio-studio-handoff/v1";
	/// <summary>Maximum UTF-8 input size, leaving room for Kubernetes Secret metadata.</summary>
	public const int MaximumBytes = 800 * 1024;
	private static readonly Regex NamePattern = new("^[a-z](?:[a-z0-9-]{0,48}[a-z0-9])?$", RegexOptions.CultureInvariant);

	/// <summary>Reads bounded JSON and validates the common envelope only.</summary>
	public static JObject Read(string path) {
		if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Specify --profile.");
		if (new FileInfo(path).Length > MaximumBytes) throw new ArgumentException("Studio profile exceeds 800 KiB.");
		JObject profile;
		try {
			profile = JObject.Parse(File.ReadAllText(path), new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
		} catch (JsonException) {
			throw new ArgumentException("Studio profile must be valid JSON with unique property names.");
		}
		if (profile.Value<string>("schema") != Schema) throw new ArgumentException($"Studio profile schema must be {Schema}.");
		ValidateName(profile.Value<string>("name"), "profile name");
		return profile;
	}

	/// <summary>Checks names used as resource names and cache directory segments.</summary>
	public static void ValidateName(string name, string field) {
		if (name is null || !NamePattern.IsMatch(name)) throw new ArgumentException($"Studio {field} must start with a lowercase letter and contain at most 50 lowercase letters, digits or hyphens.");
	}

	/// <summary>Validates and materializes the complete resolved source inventory.</summary>
	public static IReadOnlyList<StudioSource> Sources(JObject profile) {
		if (profile["sources"] is not JArray entries || entries.Count == 0) throw new ArgumentException("Checkout requires a non-empty sources array.");
		List<StudioSource> sources = [];
		HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
		HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
		foreach (JObject entry in entries.OfType<JObject>()) {
			string name = entry.Value<string>("name");
			ValidateName(name, "source name");
			string url = entry.Value<string>("url") ?? "";
			bool https = Uri.TryCreate(url, UriKind.Absolute, out Uri uri) && uri.Scheme == "https" &&
				string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
			bool ssh = Regex.IsMatch(url, @"^git@[a-zA-Z0-9.-]+:[a-zA-Z0-9_./-]+(?:\.git)?$", RegexOptions.CultureInvariant);
			if (!https && !ssh) throw new ArgumentException($"Source {name} needs an HTTPS URL without credentials or a git@host:path SSH URL.");
			string commit = entry.Value<string>("commit") ?? "";
			if (!Regex.IsMatch(commit, "^[a-fA-F0-9]{40}$|^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)) throw new ArgumentException($"Source {name} requires a full commit SHA.");
			string branch = entry.Value<string>("branch") ?? "";
			if (branch.Length == 0 || branch.StartsWith('-') || branch.Any(char.IsWhiteSpace)) throw new ArgumentException($"Source {name} requires a branch name.");
			string path = entry.Value<string>("path") ?? name;
			string[] segments = path.Replace('\\', '/').Split('/');
			if (Path.IsPathRooted(path) || segments.Any(s => s.Length == 0 || s is "." or ".." ||
				!Regex.IsMatch(s, "^[a-zA-Z0-9_-][a-zA-Z0-9_.-]*$", RegexOptions.CultureInvariant) || s.EndsWith('.') ||
				s.Equals(".studio-repositories", StringComparison.OrdinalIgnoreCase) ||
				Regex.IsMatch(s, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))) {
				throw new ArgumentException($"Source {name} has an unsafe workspace path.");
			}
			path = string.Join(Path.DirectorySeparatorChar, segments);
			if (!names.Add(name) || !paths.Add(path)) throw new ArgumentException("Source names and workspace paths must be unique.");
			sources.Add(new(name, url, branch, commit.ToLowerInvariant(), path));
		}
		if (sources.Count != entries.Count) throw new ArgumentException("Every source must be an object.");
		return sources.OrderBy(s => s.Path.Count(c => c == Path.DirectorySeparatorChar)).ToArray();
	}
}

/// <summary>A single repository and exact source revision in the handoff.</summary>
public record StudioSource(string Name, string Url, string Branch, string Commit, string Path);
