using System;
using Clio.Common;
using Clio.Common.Responses;

namespace Clio.Command.EntitySchemaDesigner;

/// <summary>
/// Holds a configuration publish back while a previously started background OData entities build is
/// still running on the target environment.
/// </summary>
public interface IODataBuildGate
{
	/// <summary>
	/// Blocks until the environment reports no running OData build, the wait budget is spent, or the
	/// environment cannot answer the question at all.
	/// </summary>
	/// <param name="options">Remote command options identifying the target environment.</param>
	/// <param name="schemaName">Schema the caller is about to publish, used in progress messages.</param>
	void WaitUntilIdle(RemoteCommandOptions options, string schemaName);

	/// <summary>
	/// Blocks until the target environment reports no running OData build using the caller's authenticated client.
	/// </summary>
	/// <param name="client">Authenticated client for the target environment.</param>
	/// <param name="environmentSettings">Settings identifying the target environment.</param>
	/// <param name="operationName">Operation about to start, used in progress messages.</param>
	void WaitUntilIdle(IApplicationClient client, EnvironmentSettings environmentSettings, string operationName);
}

/// <summary>
/// Default <see cref="IODataBuildGate"/> that polls <c>IsODataBuildRunning</c> on the target environment.
/// </summary>
/// <remarks>
/// The build this waits for is started by <c>RunODataBuild</c>, which returns as soon as the background
/// task is queued while the compilation itself runs for roughly 90-120 seconds and holds
/// <c>conf\_MetaInfo.json</c> open the whole time. A publish that starts inside that window fails on a
/// sharing violation, and no amount of retrying survives it: the platform's own writer already retries
/// six times across about eight seconds and still loses. Waiting for the build to finish removes the
/// collision instead of trying to outlast it.
/// <para>
/// An environment whose platform predates the status method answers with an HTML error page. There the
/// gate returns immediately rather than falling back to a guess: the alternative marker - reading the
/// OData <c>$metadata</c> document until the change appears - costs several megabytes and up to fourteen
/// seconds per poll, which is a heavy price for a question the server will answer cheaply once it can.
/// On such a stand the collision stays possible, and the narrower rebuild rule
/// (<see cref="ODataContractImpact"/>) is what keeps it rare.
/// </para>
/// </remarks>
internal sealed class ODataBuildGate : IODataBuildGate
{
	// 3s x 30 = a 90s budget, sized from the measured 90-120s build against the ~180s ceiling an MCP client
	// imposes on a single tool call: the publish itself takes 25-45s, so a longer budget would let the client
	// abort the call rather than let this return. Expressed as an attempt count, not a deadline, so a test
	// substituting a zero delay still terminates in a fixed number of iterations.
	internal const int PollAttemptCount = 30;
	internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

	private readonly IRemoteEntitySchemaDesignerClient _client;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly IJsonConverter _jsonConverter;
	private readonly IRetryDelay _retryDelay;
	private readonly ILogger _logger;

	// Whether the target environment exposes the status method at all: null until the first probe answers.
	// Scoped to this gate instance and nothing wider on purpose. IODataBuildGate is registered AddTransient,
	// so a fresh gate is resolved per command and WaitUntilIdle runs once on it - a process-wide cache keyed
	// by environment would never record a second hit, and keying it by `options.Uri ?? ""` would collapse
	// every null-URI environment into one entry the moment the lifetime changed. The flag is still worth
	// holding because WaitUntilIdle probes repeatedly within the one call.
	private bool? _statusMethodSupported;

	public ODataBuildGate(IRemoteEntitySchemaDesignerClient client, IServiceUrlBuilder serviceUrlBuilder,
		IJsonConverter jsonConverter, IRetryDelay retryDelay, ILogger logger) {
		_client = client;
		_serviceUrlBuilder = serviceUrlBuilder;
		_jsonConverter = jsonConverter;
		_retryDelay = retryDelay;
		_logger = logger;
	}

	/// <inheritdoc />
	public void WaitUntilIdle(RemoteCommandOptions options, string schemaName) {
		ArgumentNullException.ThrowIfNull(options);
		WaitUntilIdleCore(() => _client.TryGetIsODataBuildRunning(options), schemaName);
	}

	/// <inheritdoc />
	public void WaitUntilIdle(IApplicationClient client, EnvironmentSettings environmentSettings, string operationName) {
		ArgumentNullException.ThrowIfNull(client);
		ArgumentNullException.ThrowIfNull(environmentSettings);
		WaitUntilIdleCore(() => Probe(client, environmentSettings), operationName);
	}

	private void WaitUntilIdleCore(Func<bool?> probe, string operationName) {
		ArgumentNullException.ThrowIfNull(probe);
		ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
		if (_statusMethodSupported == false) {
			return;
		}
		bool? isRunning = Probe(probe, out bool probeFaulted);
		if (probeFaulted) {
			return;
		}
		if (isRunning is null) {
			// Unknown, not idle: the server has no such method. Recorded so a second WaitUntilIdle on this
			// gate returns without re-probing an environment that has already said it cannot answer.
			_statusMethodSupported = false;
			return;
		}
		_statusMethodSupported = true;
		if (isRunning == false) {
			return;
		}
		_logger.WriteInfo(
			$"Waiting for the running OData entities build to finish before '{operationName}'.");
		for (int attempt = 1; attempt <= PollAttemptCount; attempt++) {
			_retryDelay.Wait(PollInterval);
			bool? pollResult = Probe(probe, out bool pollFaulted);
			if (pollFaulted || pollResult != true) {
				return;
			}
		}
		// Budget spent. The publish still goes ahead: the caller's work is legitimate, and failing it here
		// would turn a slow environment into a command failure. If the collision does happen, the existing
		// publish error already explains it.
		_logger.WriteWarning(
			$"The OData entities build is still running after {(PollAttemptCount * PollInterval).TotalSeconds:0}s; " +
			$"starting '{operationName}' anyway. If the operation fails on a locked configuration file, retry the " +
			"command once the build has finished.");
	}

	// The gate runs AFTER the schema has already been saved and BEFORE the publisher's own try block, so
	// anything this probe throws would abort a mutation that is already persisted and leave it unpublished -
	// the user would get a raw exception instead of the publisher's actionable "stays invisible to lookup
	// pickers ... and OData" message. The probe only decides whether to wait, so EVERY fault is absorbed, not
	// just the ODataBuildFaults allow-list: a TimeoutException, a re-auth failure or a programming error here
	// must not be the thing that strands a saved schema. The caller stops waiting and publishes, exactly as
	// it does when the wait budget is spent. The support flag is left untouched on a fault - a dropped
	// connection says nothing about whether the platform exposes the method.
	private bool? Probe(Func<bool?> probe, out bool faulted) {
		faulted = false;
		try {
			return probe();
		} catch (Exception exception) {
			faulted = true;
			// The allow-list still earns its keep as a DIAGNOSTIC: an expected environment fault is ordinary
			// and reads as such, while anything outside it says so, so a real defect is not silently reduced
			// to "the environment was busy".
			string cause = ODataBuildFaults.IsExpected(exception)
				? exception.Message
				: $"unexpected {exception.GetType().Name}: {exception.Message}";
			_logger.WriteWarning(
				$"Could not read the OData entities build status: {cause} Publishing without waiting; " +
				"if the publish fails on a locked configuration file, retry the command once the build has finished.");
			return null;
		}
	}

	private bool? Probe(IApplicationClient client, EnvironmentSettings environmentSettings) {
		string url = _serviceUrlBuilder.Build(
			"ServiceModel/WorkspaceExplorerService.svc/IsODataBuildRunning", environmentSettings);
		string response = client.ExecutePostRequest(url, "{}", requestTimeout: 10_000, maxAttempts: 1, delaySec: 0);
		return _jsonConverter.DeserializeObject<BoolResponse>(response)?.Value;
	}
}
