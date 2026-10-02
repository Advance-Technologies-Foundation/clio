using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using Clio.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Package;

/// <summary>Reads the stable Creatio dependency explorer contract without release-number fallbacks.</summary>
public interface IPackageExplorerClient {
	/// <summary>Checks v1 support and reads one advertised operation. Unknown response fields are retained.</summary>
	JObject Read(ServiceUrlBuilder.KnownRoute route, JObject request, int timeoutMs = 90_000);
	/// <summary>Reads transitive dependency names within a total timeout, refusing unsupported/incomplete graphs.</summary>
	IReadOnlyList<string> GetReachablePackageNames(Guid packageUId, string packageName, int timeoutMs);
}

/// <inheritdoc />
public sealed class PackageExplorerClient(IApplicationClient client, IServiceUrlBuilder urls)
	: IPackageExplorerClient {
	private static readonly IReadOnlyDictionary<ServiceUrlBuilder.KnownRoute, string> Operations =
		new Dictionary<ServiceUrlBuilder.KnownRoute, string> {
			[ServiceUrlBuilder.KnownRoute.DependencyGraphV1] = "graph",
			[ServiceUrlBuilder.KnownRoute.DependencySearchV1] = "searchSchemas",
			[ServiceUrlBuilder.KnownRoute.DependencyResolveV1] = "resolveSchema",
			[ServiceUrlBuilder.KnownRoute.DependencyReasonsV1] = "reasons",
			[ServiceUrlBuilder.KnownRoute.DependencyDropV1] = "dropImpact",
			[ServiceUrlBuilder.KnownRoute.DependencyAddV1] = "addImpact"
		};

	/// <inheritdoc />
	public JObject Read(ServiceUrlBuilder.KnownRoute route, JObject request, int timeoutMs = 90_000) {
		var clock = Stopwatch.StartNew();
		if (!Operations.TryGetValue(route, out string operation)) {
			throw new ArgumentException("Unsupported dependency operation.", nameof(route));
		}
		JObject capabilities = Post(ServiceUrlBuilder.KnownRoute.DependencyCapabilities, new JObject(), timeoutMs);
		JObject contract = (capabilities["contracts"] as JArray)?.OfType<JObject>()
			.SingleOrDefault(item => (int?)item["major"] == 1);
		if (contract?["operations"] is not JArray operations || !operations.Values<string>().Contains(operation)) {
			throw new NotSupportedException("Creatio does not advertise dependency contract v1 operation " + operation + ".");
		}
		string feature = route == ServiceUrlBuilder.KnownRoute.DependencySearchV1 && (string)request["matchMode"] == "contains"
			? "schemaSearch.literalContains"
			: route == ServiceUrlBuilder.KnownRoute.DependencyResolveV1
				? "schemaContext.entity" + ((string)request["purpose"] == "extend" ? "Extend" : "Reference") : null;
		if (feature != null && !(contract["features"] as JArray ?? []).Values<string>().Contains(feature)) {
			throw new NotSupportedException("Creatio does not advertise required feature " + feature + ".");
		}
		int remaining = timeoutMs - (int)clock.ElapsedMilliseconds;
		if (remaining <= 0) { throw new TimeoutException("Dependency inspection exceeded its total time budget."); }
		JObject response = Post(route, request, remaining);
		if ((int?)response["contractMajor"] != 1) {
			throw new InvalidOperationException("Creatio returned an incompatible dependency contract major.");
		}
		if (route is ServiceUrlBuilder.KnownRoute.DependencyDropV1 or ServiceUrlBuilder.KnownRoute.DependencyAddV1
				&& (string)response["assessment"] is not ("blocked" or "noKnownBlockers" or "unknown")) {
			throw new InvalidOperationException("Creatio returned an unknown assessment; no safe verdict is available.");
		}
		return response;
	}

	/// <inheritdoc />
	public IReadOnlyList<string> GetReachablePackageNames(Guid packageUId, string packageName, int timeoutMs) {
		JObject graph = Read(ServiceUrlBuilder.KnownRoute.DependencyGraphV1, new JObject(), timeoutMs);
		if (graph["packages"] is not JArray packages || graph["dependencies"] is not JArray edges
				|| graph["missingDependencies"] is not JArray missing || missing.Count != 0) {
			throw new InvalidOperationException("Dependency graph is incomplete; dependency candidates remain unverified.");
		}
		string id = packageUId.ToString();
		if (!packages.OfType<JObject>().Any(package => string.Equals((string)package["uId"], id, StringComparison.OrdinalIgnoreCase))) {
			throw new ArgumentException($"Package '{packageName}' is not present in the dependency graph.");
		}
		var reached = PackageGraphTraversal.Traverse(edges, id, false, true);
		return packages.OfType<JObject>().Where(package => reached.ContainsKey((string)package["uId"]))
			.Select(package => (string)package["name"]).ToArray();
	}

	private JObject Post(ServiceUrlBuilder.KnownRoute route, JObject request, int timeoutMs) {
		ICreatioApplicationClient transport = client as ICreatioApplicationClient
			?? throw new NotSupportedException("The configured transport must expose HTTP response status.");
		using var response = transport.ExecutePostRequestAsync(urls.Build(route), request.ToString(Formatting.None),
			requestTimeout: timeoutMs, maxAttempts: 1).GetAwaiter().GetResult();
		if (response.StatusCode == HttpStatusCode.NotFound) {
			throw new NotSupportedException("This Creatio environment does not support the versioned package dependency API. "
				+ "Install a Creatio build providing dependency contract v1; no legacy fallback is used.");
		}
		response.EnsureSuccessStatusCode();
		string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
		JObject result = JObject.Parse(json);
		if (result["success"]?.Type != JTokenType.Boolean || (bool)result["success"] != true) {
			throw new InvalidOperationException($"Creatio dependency API: {result["errorInfo"]?["errorCode"]}: "
				+ (string)result["errorInfo"]?["message"]);
		}
		return result;
	}
}
