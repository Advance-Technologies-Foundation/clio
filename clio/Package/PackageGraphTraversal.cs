using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Clio.Package;

/// <summary>Cycle-safe deterministic graph traversal; does not infer schema visibility or write permission.</summary>
internal static class PackageGraphTraversal {
	internal static Dictionary<string, string> Traverse(JArray edges, string start, bool reverse, bool transitive) {
		var parents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [start] = null };
		var queue = new Queue<string>();
		queue.Enqueue(start);
		while (queue.TryDequeue(out string current)) {
			IEnumerable<string> targets = edges.OfType<JObject>()
				.Where(edge => string.Equals((string)edge[reverse ? "dependOnPackageUId" : "packageUId"],
					current, StringComparison.OrdinalIgnoreCase))
				.Select(edge => (string)edge[reverse ? "packageUId" : "dependOnPackageUId"])
				.OrderBy(value => value, StringComparer.Ordinal);
			foreach (string target in targets) {
				if (string.IsNullOrEmpty(target)) {
					throw new InvalidOperationException("Dependency edge is missing its endpoint.");
				}
				if (parents.TryAdd(target, current) && transitive) { queue.Enqueue(target); }
			}
		}
		return parents;
	}

}
