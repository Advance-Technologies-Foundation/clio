using System;
using System.Collections.Generic;
using System.IO;
using AbstractionsFileSystem = System.IO.Abstractions.IFileSystem;
using System.Linq;
using System.Security.Cryptography;
using Clio.Command;
using Clio.Common.RuntimeAttachment;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Common.OperatorBootstrap;

/// <summary>Installs a bundled operator profile into an explicitly selected local cluster.</summary>
public interface IOperatorInstaller {
	/// <summary>Preflights Rancher, preserves existing data and credentials, then waits for the operator.</summary>
	void Install(InstallOperatorOptions options);
}

/// <inheritdoc/>
public class OperatorInstaller(IAttachmentProcess process, AbstractionsFileSystem files, ILogger logger,
	IRancherDesktopTarget target) : IOperatorInstaller {
	internal const string Profile = "rancher-desktop-v1";
	internal const string Annotation = "clio.creatio.com/install-profile";
	private const string SystemNamespace = "creatio-system";
	private const string InfraNamespace = "clio-infrastructure";
	private const string AdminSecret = "creatio-operator-dashboard-admin";
	private const string SharedResource = "creatiosharedinfrastructures.apps.creatio.io";

	/// <inheritdoc/>
	public void Install(InstallOperatorOptions options) {
		Validate(options);
		JObject crds = ReadBundle("crds.json");
		JObject resources = ReadBundle("operator.json");
		string image = options.Image ?? ReadBundle("provenance.json").Value<string>("image");
		foreach (JObject deployment in resources["items"].OfType<JObject>().Where(d => d.Value<string>("kind") == "Deployment")) {
			deployment["spec"]["template"]["spec"]["containers"][0]["image"] = image;
		}
		Preflight(options.Context, crds, resources);
		logger.WriteInfo($"Installing operator: context={options.Context}, namespace={SystemNamespace}. No local registry.");
		Apply(options.Context, crds);
		foreach (JToken crd in crds["items"]) {
			Kubectl(options.Context, ["wait", "--for=condition=Established", $"crd/{crd["metadata"]["name"]}", "--timeout=90s"]);
		}
		EnsureNamespace(options.Context, SystemNamespace);
		EnsureNamespace(options.Context, InfraNamespace);
		EnsureInfrastructure(options.Context);
		EnsureCredentials(options.Context);
		Apply(options.Context, resources);
		logger.WriteInfo("Waiting for operator readiness...");
		Kubectl(options.Context, ["rollout", "status", "deployment/creatio-operator", "-n", SystemNamespace, "--timeout=110s"]);
		logger.WriteInfo("Operator deployment ready. Dashboard URL: http://creatio-operator.localhost");
		logger.WriteInfo($"Dashboard credentials are in Secret {SystemNamespace}/{AdminSecret} (keys username/password). Existing credentials are preserved.");
	}

	private static void Validate(InstallOperatorOptions options) {
		if (options.Target != "rancher-desktop") {
			throw new ArgumentException("Supported --target: rancher-desktop.");
		}
		if (string.IsNullOrWhiteSpace(options.Context)) {
			throw new ArgumentException("A non-empty --context is required.");
		}
		if (options.Image is not null && (string.IsNullOrWhiteSpace(options.Image) || options.Image.Any(char.IsWhiteSpace))) {
			throw new ArgumentException("--image must be a container image reference without whitespace.");
		}
	}

	private void Preflight(string context, JObject crds, JObject resources) {
		target.Verify(context);
		Kubectl(context, ["get", "ingressclass", "traefik", "-o", "name"]);
		ValidateResourceOwnership(context, crds["items"].Concat(resources["items"]));
		string crd = Kubectl(context, ["get", "crd", SharedResource, "--ignore-not-found", "-o", "name"]);
		if (!string.IsNullOrWhiteSpace(crd)) {
			ValidateExistingInfrastructure(ReadInfrastructure(context));
		}
	}

	private void ValidateResourceOwnership(string context, IEnumerable<JToken> resources) {
		foreach (JToken resource in resources.Where(r => r.Value<string>("kind") != "Namespace")) {
			string kind = resource.Value<string>("kind");
			string name = resource["metadata"].Value<string>("name");
			List<string> args = ["get", kind, name, "--ignore-not-found", "-o", "json"];
			string ns = resource["metadata"].Value<string>("namespace");
			if (!string.IsNullOrEmpty(ns)) { args.AddRange(["-n", ns]); }
			string existing = Kubectl(context, args);
			if (!string.IsNullOrWhiteSpace(existing) && JObject.Parse(existing)["metadata"]?["annotations"]?.Value<string>(Annotation) != Profile) {
				throw new InvalidOperationException($"Existing {kind}/{name} is outside this Clio installation profile. Refusing to replace it; migrate it explicitly first.");
			}
		}
	}

	private JObject ReadBundle(string name) => JObject.Parse(files.File.ReadAllText(
		Path.Combine(AppContext.BaseDirectory, "tpl", "operator", "rancher-desktop", name)));

	private void EnsureNamespace(string context, string name) {
		if (string.IsNullOrWhiteSpace(Kubectl(context, ["get", "namespace", name, "--ignore-not-found", "-o", "name"]))) {
			Create(context, new JObject { ["apiVersion"] = "v1", ["kind"] = "Namespace", ["metadata"] = new JObject { ["name"] = name } });
		}
	}

	private string ReadInfrastructure(string context) => Kubectl(context,
		["get", SharedResource, "default", "-n", InfraNamespace, "--ignore-not-found", "-o", "json"]);

	private static void ValidateExistingInfrastructure(string existing) {
		if (!string.IsNullOrWhiteSpace(existing) && !string.Equals(
			JObject.Parse(existing)["spec"]?["nexus"]?.Value<string>("mode"), "External", StringComparison.OrdinalIgnoreCase)) {
			throw new InvalidOperationException("Existing shared infrastructure enables Nexus. The registry-free profile requires an explicit migration; no resources were removed.");
		}
	}

	private void EnsureInfrastructure(string context) {
		string existing = ReadInfrastructure(context);
		ValidateExistingInfrastructure(existing);
		if (!string.IsNullOrWhiteSpace(existing)) { return; }
		// Create before starting the controller: its legacy automatic default enables Nexus.
		JObject policy = JObject.Parse("""
		{
		  "apiVersion": "apps.creatio.io/v1alpha1", "kind": "CreatioSharedInfrastructure",
		  "metadata": {"name": "default", "namespace": "clio-infrastructure"},
		  "spec": {
		    "postgres": {
		      "mode": "Managed", "image": {"repository": "postgres", "tag": "18.6"},
		      "resources": {"cpuRequest": "500m", "cpuLimit": "4", "memoryRequest": "1Gi", "memoryLimit": "4Gi"},
		      "storage": {"data": {"size": "20Gi", "storageClassName": "local-path"}, "backupImages": {"size": "20Gi", "storageClassName": "local-path"}},
		      "exposure": {"mode": "Disabled"}
		    },
		    "redis": {
		      "mode": "Managed", "image": {"repository": "redis", "tag": "7.4"},
		      "resources": {"cpuRequest": "100m", "cpuLimit": "1", "memoryRequest": "256Mi", "memoryLimit": "1Gi"},
		      "databaseCount": 100, "exposure": {"mode": "Disabled"}
		    },
		    "mssql": {"mode": "External"}, "nexus": {"mode": "External"}
		  }
		}
		""");
		foreach ((string component, string service) in new[] {
			("postgres", "postgres-service-internal"), ("redis", "redis-service-internal") }) {
			if (!string.IsNullOrWhiteSpace(Kubectl(context, ["get", "service", service, "-n", InfraNamespace, "--ignore-not-found", "-o", "name"]))) {
				policy["spec"][component]["mode"] = "External";
			}
		}
		Create(context, policy);
	}

	private void EnsureCredentials(string context) {
		if (!string.IsNullOrWhiteSpace(Kubectl(context, ["get", "secret", AdminSecret, "-n", SystemNamespace, "--ignore-not-found", "-o", "name"]))) {
			return;
		}
		JObject secret = new() {
			["apiVersion"] = "v1", ["kind"] = "Secret", ["type"] = "Opaque",
			["metadata"] = new JObject { ["name"] = AdminSecret, ["namespace"] = SystemNamespace },
			["stringData"] = new JObject { ["username"] = "admin", ["password"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) }
		};
		try { Create(context, secret); }
		catch (InvalidOperationException) {
			// kubectl diagnostics can include the rejected object; never propagate credentials.
			throw new InvalidOperationException("Could not create the operator dashboard Secret. Check Kubernetes permissions and retry.");
		}
	}

	private void Apply(string context, JObject resource) => Kubectl(context, ["apply", "-f", "-"], resource.ToString(Formatting.None));
	private void Create(string context, JObject resource) => Kubectl(context, ["create", "-f", "-"], resource.ToString(Formatting.None));
	private string Kubectl(string context, IReadOnlyList<string> args, string input = null) =>
		process.Run("kubectl", new[] { "--context", context }.Concat(args).ToArray(), input);
}
