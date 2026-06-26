using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using k8s;

namespace Clio;

public class FakeKubernetes : IKubernetes
{
	public FakeKubernetes()
	{
	}

	// No-op: FakeKubernetes is the graceful fallback returned by the IKubernetes factory when no
	// valid kubeconfig is present (see BindingsModule). It owns no unmanaged resources. Dispose must
	// not throw: the MCP server runs each tool call in its own DI scope and disposes that scope when
	// the request completes, so a throwing Dispose surfaced as an opaque MCP InternalError
	// (-32603) for every infrastructure tool resolved on a no-Kubernetes host (ENG-91830).
	public void Dispose() {
	}

	public Task<int> NamespacedPodExecAsync(string name, string @namespace, string container, IEnumerable<string> command, bool tty,
		ExecAsyncCallback action, CancellationToken cancellationToken) {
		throw new NotImplementedException();
	}

	public Task<WebSocket> WebSocketNamespacedPodExecAsync(string name, string @namespace = "default", string command = null,
		string container = null, bool stderr = true, bool stdin = true, bool stdout = true, bool tty = true,
		string webSocketSubProtocol = null, Dictionary<string, List<string>> customHeaders = null,
		CancellationToken cancellationToken = new CancellationToken()) {
		throw new NotImplementedException();
	}

	public Task<WebSocket> WebSocketNamespacedPodExecAsync(string name, string @namespace = "default", IEnumerable<string> command = null,
		string container = null, bool stderr = true, bool stdin = true, bool stdout = true, bool tty = true,
		string webSocketSubProtocol = null, Dictionary<string, List<string>> customHeaders = null,
		CancellationToken cancellationToken = new CancellationToken()) {
		throw new NotImplementedException();
	}

	public Task<IStreamDemuxer> MuxedStreamNamespacedPodExecAsync(string name, string @namespace = "default", IEnumerable<string> command = null,
		string container = null, bool stderr = true, bool stdin = true, bool stdout = true, bool tty = true,
		string webSocketSubProtocol = "v4.channel.k8s.io", Dictionary<string, List<string>> customHeaders = null,
		CancellationToken cancellationToken = new CancellationToken()) {
		throw new NotImplementedException();
	}

	public Task<WebSocket> WebSocketNamespacedPodPortForwardAsync(string name, string @namespace, IEnumerable<int> ports,
		string webSocketSubProtocol = null, Dictionary<string, List<string>> customHeaders = null,
		CancellationToken cancellationToken = new CancellationToken()) {
		throw new NotImplementedException();
	}

	public Task<WebSocket> WebSocketNamespacedPodAttachAsync(string name, string @namespace, string container = null, bool stderr = true,
		bool stdin = false, bool stdout = true, bool tty = false, string webSocketSubProtocol = null,
		Dictionary<string, List<string>> customHeaders = null, CancellationToken cancellationToken = new CancellationToken()) {
		throw new NotImplementedException();
	}

	public Uri BaseUri { get; set; }
	public ICoreOperations Core { get; }
	public ICoreV1Operations CoreV1 { get; }
	public IApisOperations Apis { get; }
	public IAdmissionregistrationOperations Admissionregistration { get; }
	public IAdmissionregistrationV1Operations AdmissionregistrationV1 { get; }
	public IAdmissionregistrationV1alpha1Operations AdmissionregistrationV1alpha1 { get; }
	public IAdmissionregistrationV1beta1Operations AdmissionregistrationV1beta1 { get; }
	public IApiextensionsOperations Apiextensions { get; }
	public IApiextensionsV1Operations ApiextensionsV1 { get; }
	public IApiregistrationOperations Apiregistration { get; }
	public IApiregistrationV1Operations ApiregistrationV1 { get; }
	public IAppsOperations Apps { get; }
	public IAppsV1Operations AppsV1 { get; }
	public IAuthenticationOperations Authentication { get; }
	public IAuthenticationV1Operations AuthenticationV1 { get; }
	public IAuthorizationOperations Authorization { get; }
	public IAuthorizationV1Operations AuthorizationV1 { get; }
	public IAutoscalingOperations Autoscaling { get; }
	public IAutoscalingV1Operations AutoscalingV1 { get; }
	public IAutoscalingV2Operations AutoscalingV2 { get; }
	public IBatchOperations Batch { get; }
	public IBatchV1Operations BatchV1 { get; }
	public ICertificatesOperations Certificates { get; }
	public ICertificatesV1Operations CertificatesV1 { get; }
	public ICertificatesV1alpha1Operations CertificatesV1alpha1 { get; }
	public ICertificatesV1beta1Operations CertificatesV1beta1 { get; }
	public ICoordinationOperations Coordination { get; }
	public ICoordinationV1Operations CoordinationV1 { get; }
	public ICoordinationV1alpha2Operations CoordinationV1alpha2 { get; }
	public ICoordinationV1beta1Operations CoordinationV1beta1 { get; }
	public IDiscoveryOperations Discovery { get; }
	public IDiscoveryV1Operations DiscoveryV1 { get; }
	public IEventsOperations Events { get; }
	public IEventsV1Operations EventsV1 { get; }
	public IFlowcontrolApiserverOperations FlowcontrolApiserver { get; }
	public IFlowcontrolApiserverV1Operations FlowcontrolApiserverV1 { get; }
	public IInternalApiserverOperations InternalApiserver { get; }
	public IInternalApiserverV1alpha1Operations InternalApiserverV1alpha1 { get; }
	public INetworkingOperations Networking { get; }
	public INetworkingV1Operations NetworkingV1 { get; }
	public INetworkingV1beta1Operations NetworkingV1beta1 { get; }
	public INodeOperations Node { get; }
	public INodeV1Operations NodeV1 { get; }
	public IPolicyOperations Policy { get; }
	public IPolicyV1Operations PolicyV1 { get; }
	public IRbacAuthorizationOperations RbacAuthorization { get; }
	public IRbacAuthorizationV1Operations RbacAuthorizationV1 { get; }
	public IResourceOperations Resource { get; }
	public IResourceV1Operations ResourceV1 { get; }
	public IResourceV1alpha3Operations ResourceV1alpha3 { get; }
	public IResourceV1beta1Operations ResourceV1beta1 { get; }
	public IResourceV1beta2Operations ResourceV1beta2 { get; }
	public ISchedulingOperations Scheduling { get; }
	public ISchedulingV1Operations SchedulingV1 { get; }
	public ISchedulingV1alpha1Operations SchedulingV1alpha1 { get; }
	public IStorageOperations Storage { get; }
	public IStorageV1Operations StorageV1 { get; }
	public IStorageV1beta1Operations StorageV1beta1 { get; }
	public IStoragemigrationOperations Storagemigration { get; }
	public IStoragemigrationV1beta1Operations StoragemigrationV1beta1 { get; }
	public ILogsOperations Logs { get; }
	public IVersionOperations Version { get; }
	public ICustomObjectsOperations CustomObjects { get; }
	public IWellKnownOperations WellKnown { get; }
	public IOpenidOperations Openid { get; }
}
