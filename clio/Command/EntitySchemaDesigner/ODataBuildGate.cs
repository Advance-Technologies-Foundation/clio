using System;
using System.Collections.Concurrent;
using Clio.Common;

namespace Clio.Command.EntitySchemaDesigner;

/// <summary>
/// Holds a configuration publish back while a previously started background OData entities build is
/// still running on the target environment.
/// </summary>
internal interface IODataBuildGate
{
	/// <summary>
	/// Blocks until the environment reports no running OData build, the wait budget is spent, or the
	/// environment cannot answer the question at all.
	/// </summary>
	/// <param name="options">Remote command options identifying the target environment.</param>
	/// <param name="schemaName">Schema the caller is about to publish, used in progress messages.</param>
	void WaitUntilIdle(RemoteCommandOptions options, string schemaName);
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
	private readonly IRetryDelay _retryDelay;
	private readonly ILogger _logger;

	// Whether an environment exposes the status method at all. Asked once per environment rather than once
	// per publish: the answer is a property of the deployed platform and cannot change while a command runs.
	private readonly ConcurrentDictionary<string, bool> _statusMethodSupport = new(StringComparer.OrdinalIgnoreCase);

	public ODataBuildGate(IRemoteEntitySchemaDesignerClient client, IRetryDelay retryDelay, ILogger logger) {
		_client = client;
		_retryDelay = retryDelay;
		_logger = logger;
	}

	/// <inheritdoc />
	public void WaitUntilIdle(RemoteCommandOptions options, string schemaName) {
		ArgumentNullException.ThrowIfNull(options);
		string environmentKey = options.Uri ?? string.Empty;
		if (_statusMethodSupport.TryGetValue(environmentKey, out bool isSupported) && !isSupported) {
			return;
		}
		bool? isRunning = Probe(options, out bool probeFaulted);
		if (probeFaulted) {
			return;
		}
		if (isRunning is null) {
			// Unknown, not idle: the server has no such method. Record it so the remaining publishes in this
			// process do not pay for the probe again.
			_statusMethodSupport[environmentKey] = false;
			return;
		}
		_statusMethodSupport[environmentKey] = true;
		if (isRunning == false) {
			return;
		}
		_logger.WriteInfo(
			$"Waiting for the running OData entities build to finish before publishing '{schemaName}'.");
		for (int attempt = 1; attempt <= PollAttemptCount; attempt++) {
			_retryDelay.Wait(PollInterval);
			bool? pollResult = Probe(options, out bool pollFaulted);
			if (pollFaulted || pollResult != true) {
				return;
			}
		}
		// Budget spent. The publish still goes ahead: the caller's work is legitimate, and failing it here
		// would turn a slow environment into a command failure. If the collision does happen, the existing
		// publish error already explains it.
		_logger.WriteWarning(
			$"The OData entities build is still running after {(PollAttemptCount * PollInterval).TotalSeconds:0}s; " +
			$"publishing '{schemaName}' anyway. If the publish fails on a locked configuration file, retry the " +
			"command once the build has finished.");
	}

	// The gate runs AFTER the schema has already been saved and BEFORE the publisher's own try block, so
	// anything this probe throws would abort a mutation that is already persisted and leave it unpublished.
	// The probe only decides whether to wait, so every environment or transport fault is absorbed: the caller
	// stops waiting and publishes, exactly as it does when the wait budget is spent. The support flag is left
	// untouched on a fault - a dropped connection says nothing about whether the platform exposes the method.
	private bool? Probe(RemoteCommandOptions options, out bool faulted) {
		faulted = false;
		try {
			return _client.TryGetIsODataBuildRunning(options);
		} catch (Exception exception) when (ODataBuildFaults.IsExpected(exception)) {
			faulted = true;
			_logger.WriteWarning(
				$"Could not read the OData entities build status: {exception.Message} Publishing without waiting; " +
				"if the publish fails on a locked configuration file, retry the command once the build has finished.");
			return null;
		}
	}
}
