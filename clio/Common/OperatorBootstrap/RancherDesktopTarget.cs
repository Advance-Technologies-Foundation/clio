using System;
using Clio.Common.RuntimeAttachment;
using Newtonsoft.Json.Linq;

namespace Clio.Common.OperatorBootstrap;

/// <summary>Verifies that a context points to the local Rancher Moby engine.</summary>
public interface IRancherDesktopTarget {
	/// <summary>Rejects unsupported engines or a context pointing at another cluster before mutation.</summary>
	void Verify(string context);
}

/// <inheritdoc/>
public class RancherDesktopTarget(IAttachmentProcess process) : IRancherDesktopTarget {
	/// <inheritdoc/>
	public void Verify(string context) {
		if (string.IsNullOrWhiteSpace(context)) { throw new ArgumentException("Specify --context explicitly."); }
		JObject settings = JObject.Parse(process.Run("rdctl", ["list-settings"]));
		if (settings["containerEngine"]?.Value<string>("name") != "moby") {
			throw new InvalidOperationException("This profile requires Rancher Desktop's Moby (dockerd) engine.");
		}
		string localUid = process.Run("kubectl", ["--context", "rancher-desktop", "get", "namespace", "kube-system", "-o", "jsonpath={.metadata.uid}"]);
		string targetUid = process.Run("kubectl", ["--context", context, "get", "namespace", "kube-system", "-o", "jsonpath={.metadata.uid}"]);
		if (string.IsNullOrWhiteSpace(localUid) || localUid != targetUid) {
			throw new InvalidOperationException("Selected context does not identify the local Rancher Desktop cluster.");
		}
	}
}
