using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using k8s;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Common.OperatorBootstrap;

/// <summary>Reads the selected operator's existing image inventory without inspecting deployed instances.</summary>
public interface IRuntimeImageCatalog {
	/// <summary>Reads the dashboard inventory through the authenticated Kubernetes service proxy.</summary>
	JArray Read(string context, string operatorNamespace);
}

/// <inheritdoc/>
public class RuntimeImageCatalog : IRuntimeImageCatalog {
	/// <inheritdoc/>
	public JArray Read(string context, string operatorNamespace) {
		using var client = new k8s.Kubernetes(KubernetesClientConfiguration.BuildConfigFromConfigFile(currentContext: context));
		using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
		string prefix = $"/api/v1/namespaces/{Uri.EscapeDataString(operatorNamespace)}/services/creatio-operator:8080/proxy";
		HttpResponseMessage Send(string path, string body = null, string cookie = null) {
			using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, new Uri(client.BaseUri, prefix + path));
			if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
			if (cookie != null) request.Headers.Add("Cookie", cookie);
			client.Credentials?.ProcessHttpRequestAsync(request, timeout.Token).GetAwaiter().GetResult();
			return client.HttpClient.SendAsync(request, timeout.Token).GetAwaiter().GetResult();
		}
		using var initial = Send("/api/dashboard");
		if (initial.IsSuccessStatusCode) return Parse(initial);
		if (initial.StatusCode != HttpStatusCode.Unauthorized) throw Failure(initial);
		// Reuse the deployed operator's configured credentials; never put them in command arguments or output.
		var deployment = client.AppsV1.ReadNamespacedDeploymentAsync("creatio-operator", operatorNamespace, cancellationToken: timeout.Token).GetAwaiter().GetResult();
		var env = deployment.Spec.Template.Spec.Containers.SelectMany(c => c.Env ?? []).ToArray();
		string Setting(string name) {
			var entry = env.FirstOrDefault(e => e.Name == "Operator__DashboardAuth__" + name)
				?? throw new InvalidOperationException("Operator dashboard credentials are not configured in its deployment.");
			if (entry.Value != null) return entry.Value;
			var key = entry.ValueFrom?.SecretKeyRef ?? throw new InvalidOperationException("Operator dashboard credentials must use a value or Secret reference.");
			var secret = client.CoreV1.ReadNamespacedSecretAsync(key.Name, operatorNamespace, cancellationToken: timeout.Token).GetAwaiter().GetResult();
			return Encoding.UTF8.GetString(secret.Data[key.Key]);
		}
		using var login = Send("/api/auth/login", JsonConvert.SerializeObject(new { username = Setting("AdminUsername"), password = Setting("AdminPassword") }));
		if (!login.IsSuccessStatusCode) throw Failure(login);
		if (!login.Headers.TryGetValues("Set-Cookie", out var cookies)) throw new InvalidOperationException("Operator login returned no session cookie.");
		using var dashboard = Send("/api/dashboard", cookie: string.Join("; ", cookies.Select(c => c.Split(';')[0])));
		if (!dashboard.IsSuccessStatusCode) throw Failure(dashboard);
		return Parse(dashboard);
	}
	private static JArray Parse(HttpResponseMessage response) => JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())["images"] as JArray
		?? throw new InvalidOperationException("Operator returned no image catalogue. Check operator compatibility.");
	private static Exception Failure(HttpResponseMessage response) => new InvalidOperationException($"Cannot read operator image catalogue (HTTP {(int)response.StatusCode}). Check Kubernetes service-proxy permissions and operator availability.");
}
