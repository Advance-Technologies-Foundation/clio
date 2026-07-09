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

internal static class EntitySchemaPublishHelper
{
	internal const string ODataBuildRequestFailedWarningFragment = "requesting the OData entities rebuild failed";

	// Publish so the saved schema/columns compile into configuration, then rebuild the OData entities so they
	// are reachable. The rebuild request is best-effort: a transport/parse fault warns instead of failing the
	// caller (whose primary work already succeeded). savedContext describes what was saved, for the message.
	internal static void PublishAndRebuildOData(IRemoteEntitySchemaDesignerClient client, ILogger logger,
		RemoteCommandOptions options, string schemaName, string savedContext) {
		Stopwatch stopwatch = Stopwatch.StartNew();
		try {
			client.PublishConfigurationChanges(options);
		} catch (Exception exception) {
			throw new InvalidOperationException(
				$"Schema '{schemaName}' {savedContext}, but publishing the configuration failed: {exception.Message} " +
				"Until the configuration is built (for example via compile-creatio), it stays invisible to lookup " +
				"pickers, sys-setting reference schema lists, and OData.",
				exception);
		}
		stopwatch.Stop();
		logger.WriteInfo(
			$"Schema '{schemaName}' published in {stopwatch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)}s.");
		try {
			client.RunODataBuild(options);
			logger.WriteInfo($"OData entities rebuild requested for '{schemaName}'.");
		} catch (Exception odataException) when (IsExpectedODataBuildFault(odataException)) {
			logger.WriteWarning(
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
