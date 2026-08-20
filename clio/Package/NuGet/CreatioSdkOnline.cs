using Clio.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Clio.Project.NuGet
{
	public class CreatioSdkOnline : ICreatioSdk
	{
		private List<Version> _versions = null;
		private readonly ILogger logger;

		/// <summary>
		/// Message shown when the SDK version list cannot be read from NuGet.
		/// </summary>
		internal const string VersionsUnavailableMessage =
			"Creatio SDK versions could not be read from https://api.nuget.org, so the latest SDK version is " +
			"unknown. Check connectivity to api.nuget.org (a proxy or a corporate perimeter can block it), or " +
			"set ApplicationVersion in .clio/workspaceSettings.json explicitly.";

		// NuGet's well-known public API host, not a configurable deployment target.
		[SuppressMessage("Major Code Smell", "S1075:URIs should not be hardcoded",
			Justification = "This is NuGet's well-known public API endpoint, not a configurable resource path.")]
		private const string NuGetApiBaseAddress = "https://api.nuget.org";

		private List<Version> Versions {
			get {
				// A failed lookup caches an EMPTY list, and an empty cache is re-fetched rather than treated
				// as an answer: a single unreachable moment on api.nuget.org must not turn into "no SDK
				// version exists" for every later call on the same instance.
				if (_versions == null || _versions.Count == 0) {
					InitVersionsFromNuget();
				}
				return _versions;
			}
		}

		private void InitVersionsFromNuget() {
			try {
				// Disposed on every path: the empty-list gate above lets a failed lookup be retried, so an
				// undisposed client per failed attempt would leak a handler and its sockets.
				using var client = new HttpClient() {
					BaseAddress = new Uri(NuGetApiBaseAddress),
					// The default is 100 s. A perimeter that drops packets rather than refusing the
					// connection would otherwise make a workspace command look hung for that long.
					Timeout = TimeSpan.FromSeconds(15)
				};

				string json = default;
				Task.Run(async () => {
					var response = await client.GetAsync("/v3/registration5-semver1/creatiosdk/index.json");
					json = await response.Content.ReadAsStringAsync();
				}).Wait();

				var items = JsonSerializer.Deserialize<Model>(json);
				var _ver = items.TopItems.FirstOrDefault().Items.Select(i => i.CatalogEntry.Version);
				if (_versions == null) {
					_versions = new List<Version>();
				}
				foreach (var item in _ver) {
					_versions.Add(new Version(item));
				}
				_versions.Sort();
				_versions.Reverse();
			} catch (Exception e) {
				logger.WriteError($"Error while getting Creatio SDK versions from NuGet: {e.Message}");
				_versions = new List<Version>();
			}
		}

		/// <summary>
		/// Newest CreatioSDK version published on NuGet.
		/// </summary>
		/// <exception cref="InvalidOperationException">
		/// The version list could not be read from api.nuget.org, so there is no version to return. Before,
		/// this surfaced as "Index was out of range", which named neither the cause nor the fix (issue #1119).
		/// </exception>
		public Version LastVersion => NewestOrThrow(Versions);

		/// <summary>
		/// Returns the newest version in an already-read list, or reports that the feed could not be read.
		/// </summary>
		/// <param name="versions">The version list, newest first.</param>
		/// <returns>The newest published SDK version.</returns>
		/// <exception cref="InvalidOperationException">The list is empty, so the feed gave no answer.</exception>
		private static Version NewestOrThrow(List<Version> versions) {
			return versions.Count > 0
				? versions[0]
				: throw new InvalidOperationException(VersionsUnavailableMessage);
		}

		public CreatioSdkOnline(ILogger logger)
		{
			this.logger = logger;
		}

		public Version FindLatestSdkVersion(Version applicationVersion)
		{
			// A workspace created while api.nuget.org was unreachable records no application version, so the
			// argument can legitimately be null here; fall back to the newest published SDK instead of
			// dereferencing it.
			if (applicationVersion == null) {
				return LastVersion;
			}
			// Read into a local: `Versions.FirstOrDefault(...) ?? LastVersion` re-enters the getter, and
			// while the feed is unreachable the getter re-fetches, so the unreachable case would pay for two
			// full network attempts before reporting it.
			List<Version> versions = Versions;
			return versions.FirstOrDefault(v => 
				v.Major == applicationVersion.Major && 
				v.Minor == applicationVersion.Minor && 
				v.Build == applicationVersion.Build) ?? NewestOrThrow(versions);
		}
	}


	public class Model
	{

		[JsonPropertyName("items")]
		public List<TopItems> TopItems { get; set; }
	}

	public class TopItems
	{

		[JsonPropertyName("items")]
		public List<InnerItems> Items { get; set; }
	}

	public class InnerItems
	{

		[JsonPropertyName("catalogEntry")]
		public CatalogEntry CatalogEntry { get; set; }
	}

	public class CatalogEntry
	{

		[JsonPropertyName("version")]
		public string Version { get; set; }
	}
}
