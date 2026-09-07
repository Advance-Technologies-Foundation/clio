using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace Clio.Command.EntitySchemaDesigner;

/// <summary>
/// Classifies the faults an OData-entities-build request or status probe can legitimately produce on a
/// target environment, so callers can degrade instead of failing a mutation that is already persisted.
/// </summary>
/// <remarks>
/// An allow-list, not a blanket catch, so genuine programming errors still surface. Creatio's client runs
/// via <c>Task.Result</c>, so transport faults arrive wrapped in <see cref="AggregateException"/> - unwrap
/// recursively. <see cref="InvalidOperationException"/> covers both a <c>success:false</c> service answer
/// and a non-JSON body, since the non-JSON guard derives from it.
/// </remarks>
internal static class ODataBuildFaults
{
	/// <summary>
	/// Whether the exception is an environment or transport fault the caller may absorb.
	/// </summary>
	/// <param name="exception">Exception raised by the build request or the status probe.</param>
	/// <returns><see langword="true"/> when the fault is expected and may be absorbed.</returns>
	public static bool IsExpected(Exception exception) {
		if (exception is AggregateException aggregate) {
			// Count > 0: an empty aggregate has no diagnosable fault (All is vacuously true), so let it surface.
			ReadOnlyCollection<Exception> inner = aggregate.Flatten().InnerExceptions;
			return inner.Count > 0 && inner.All(IsExpected);
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
