using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Clio.Common;

namespace Clio.Command.EntitySchemaDesigner;

/// <summary>
/// Publishes saved entity-schema changes and, when the change is visible in the OData contract,
/// requests the OData entities rebuild.
/// </summary>
internal interface IEntitySchemaPublisher
{
	/// <summary>
	/// Publishes the configuration so a saved schema or column compiles into it, then requests the OData
	/// entities rebuild when <paramref name="impact"/> says the OData contract changed.
	/// </summary>
	/// <param name="options">Remote command options identifying the target environment.</param>
	/// <param name="schemaName">Schema whose changes are being published, used in progress messages.</param>
	/// <param name="savedContext">What was saved, phrased for the failure message.</param>
	/// <param name="impact">Whether the saved change alters the published OData contract.</param>
	void PublishSavedChanges(RemoteCommandOptions options, string schemaName, string savedContext,
		ODataContractImpact impact);
}

/// <inheritdoc />
internal sealed class EntitySchemaPublisher : IEntitySchemaPublisher
{
	internal const string ODataBuildRequestFailedWarningFragment = "requesting the OData entities rebuild failed";

	private readonly IRemoteEntitySchemaDesignerClient _client;
	private readonly IODataBuildGate _oDataBuildGate;
	private readonly ILogger _logger;

	public EntitySchemaPublisher(IRemoteEntitySchemaDesignerClient client, IODataBuildGate oDataBuildGate,
		ILogger logger) {
		_client = client;
		_oDataBuildGate = oDataBuildGate;
		_logger = logger;
	}

	/// <inheritdoc />
	public void PublishSavedChanges(RemoteCommandOptions options, string schemaName, string savedContext,
		ODataContractImpact impact) {
		// A rebuild left running by an earlier mutation holds the configuration metadata file open, and a
		// publish that starts inside that window fails on a sharing violation. Wait it out first.
		_oDataBuildGate.WaitUntilIdle(options, schemaName);
		Stopwatch stopwatch = Stopwatch.StartNew();
		try {
			_client.PublishConfigurationChanges(options);
		} catch (Exception exception) {
			throw new InvalidOperationException(
				$"Schema '{schemaName}' {savedContext}, but publishing the configuration failed: {exception.Message} " +
				"Until the configuration is built (for example via compile-creatio), it stays invisible to lookup " +
				"pickers, sys-setting reference schema lists, and OData.",
				exception);
		}
		stopwatch.Stop();
		_logger.WriteInfo(
			$"Schema '{schemaName}' published in {stopwatch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)}s.");
		if (impact == ODataContractImpact.Unchanged) {
			// Nothing to rebuild: the published contract is identical to the one already on disk, so the
			// 90-120s background compilation would produce the same document and only get in the way of the
			// next publish. See ODataContractImpact for what the contract actually carries.
			return;
		}
		try {
			_client.RunODataBuild(options);
			_logger.WriteInfo($"OData entities rebuild requested for '{schemaName}'.");
		} catch (Exception odataException) when (IsExpectedODataBuildFault(odataException)) {
			_logger.WriteWarning(
				$"Schema '{schemaName}' was published, but {ODataBuildRequestFailedWarningFragment}: " +
				$"{odataException.Message} It is usable; it may not be reachable over OData until an OData build runs.");
		}
	}

	// Creatio's client runs via Task.Result, so transport faults arrive wrapped in AggregateException — unwrap
	// recursively. Allow-list, not a blanket catch, so genuine programming errors still surface.
	private static bool IsExpectedODataBuildFault(Exception exception) {
		if (exception is AggregateException aggregate) {
			// Count > 0: an empty aggregate has no diagnosable fault (All is vacuously true), so let it surface.
			ReadOnlyCollection<Exception> inner = aggregate.Flatten().InnerExceptions;
			return inner.Count > 0 && inner.All(IsExpectedODataBuildFault);
		}
		return exception is InvalidOperationException
			or HttpRequestException
			or WebException
			or SocketException
			or IOException
			or OperationCanceledException
			or Newtonsoft.Json.JsonException;
	}
}
