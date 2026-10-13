using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Clio.Command.McpServer.Tools;
using Clio.Common;

namespace Clio.Command.RuntimeVerification;

/// <summary>Deletes the records a verified action created, children before the parents they reference.</summary>
public interface ICreatedRecordCleaner {

	/// <summary>
	/// Deletes <paramref name="records"/> one by one through OData. An entity whose lookup columns reference another
	/// entity in the set goes first; within an entity the newest record goes first. A record refused in one pass is
	/// tried again after the others, while a pass still deletes something.
	/// </summary>
	/// <param name="records">The records to delete; nothing else is ever deleted.</param>
	/// <param name="referencesByEntity">Per entity, the entities its lookup columns reference.</param>
	/// <param name="timeoutMs">Bound of each request, in milliseconds.</param>
	/// <param name="stopAt">
	/// No request runs past this instant: each is bounded by the time left, and one with less than a second left is not
	/// started and is reported as failed.
	/// </param>
	/// <param name="cancellationToken">
	/// Checked before every DELETE: once it fires, nothing more is deleted and the rest is reported as failed, not
	/// attempted. A DELETE already sent is not taken back.
	/// </param>
	/// <returns>What was deleted and what was not.</returns>
	/// <remarks>
	/// Entity names are compared case-insensitively, as the platform resolves them. The children-first order is a
	/// schema-level heuristic over the entities in the set only - a lookup to an entity outside it orders nothing - so
	/// it approximates a reverse foreign-key order; the retry passes cover what it misses.
	/// </remarks>
	CleanupReport Delete(IReadOnlyList<CreatedRecord> records,
		IReadOnlyDictionary<string, IReadOnlyCollection<string>> referencesByEntity, int timeoutMs,
		DateTimeOffset? stopAt = null, CancellationToken cancellationToken = default);
}

internal sealed class CreatedRecordCleaner(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder)
	: ICreatedRecordCleaner {

	private const int MaxPasses = 3;
	private const int MinimumDeleteMs = 1_000;
	private const string NotAttemptedCancelled = "not attempted: the call was cancelled";

	/// <inheritdoc />
	public CleanupReport Delete(IReadOnlyList<CreatedRecord> records,
		IReadOnlyDictionary<string, IReadOnlyCollection<string>> referencesByEntity, int timeoutMs,
		DateTimeOffset? stopAt = null, CancellationToken cancellationToken = default) {
		CleanupReport report = new();
		List<CreatedRecord> pending = OrderForDeletion(records, referencesByEntity).ToList();
		Dictionary<CreatedRecord, string> lastError = [];
		for (int pass = 0; pass < MaxPasses && pending.Count > 0; pass++) {
			List<CreatedRecord> refused = [];
			foreach (CreatedRecord record in pending) {
				// After a cancel a record refused earlier keeps its real reason.
				string error = cancellationToken.IsCancellationRequested
					? lastError.GetValueOrDefault(record, NotAttemptedCancelled)
					: DeleteBeforeStop(record, timeoutMs, stopAt);
				if (error is null) {
					report.Deleted.Add(record);
				}
				else {
					lastError[record] = error;
					refused.Add(record);
				}
			}
			bool progressed = refused.Count < pending.Count;
			pending = refused;
			if (!progressed) {
				break;
			}
		}
		report.Failed.AddRange(pending.Select(record => new CleanupFailure(record.Entity, record.Id, lastError[record])));
		return report;
	}

	/// <summary>
	/// Children first: an entity that references another entity of the set precedes it; within an entity, newest
	/// first. A reference cycle falls back to the caller's order for the entities in it.
	/// </summary>
	internal static IEnumerable<CreatedRecord> OrderForDeletion(IReadOnlyList<CreatedRecord> records,
		IReadOnlyDictionary<string, IReadOnlyCollection<string>> referencesByEntity) {
		// Case-insensitive throughout: a schema name resolves in any case, and the references hold the platform's.
		StringComparer names = StringComparer.OrdinalIgnoreCase;
		Dictionary<string, IReadOnlyCollection<string>> referencesOf = new(names);
		foreach ((string entity, IReadOnlyCollection<string> refs) in referencesByEntity) {
			referencesOf.TryAdd(entity, refs);
		}
		List<string> entities = records.Select(record => record.Entity).Distinct(names).ToList();
		HashSet<string> present = entities.ToHashSet(names);
		// Edges child -> parent, self references ignored (handled by the newest-first order inside an entity).
		Dictionary<string, HashSet<string>> parentsOf = entities.ToDictionary(entity => entity,
			entity => (referencesOf.TryGetValue(entity, out IReadOnlyCollection<string> refs) ? refs : [])
				.Where(parent => present.Contains(parent) && !names.Equals(parent, entity))
				.ToHashSet(names), names);
		List<string> ordered = [];
		HashSet<string> placed = new(names);
		while (placed.Count < entities.Count) {
			// An entity no unplaced entity references can go now: nothing left depends on it.
			string next = entities.FirstOrDefault(entity => !placed.Contains(entity)
				&& !entities.Any(other => !placed.Contains(other) && !names.Equals(other, entity)
					&& parentsOf[other].Contains(entity)));
			next ??= entities.First(entity => !placed.Contains(entity));
			ordered.Add(next);
			placed.Add(next);
		}
		return ordered.SelectMany(entity => records
			.Where(record => names.Equals(record.Entity, entity))
			.OrderByDescending(record => record.CreatedOn, StringComparer.Ordinal));
	}

	// Each DELETE is bounded by the time left before stopAt, and none starts with less than a second left.
	private string DeleteBeforeStop(CreatedRecord record, int timeoutMs, DateTimeOffset? stopAt) {
		if (stopAt is null) {
			return DeleteOne(record, timeoutMs);
		}
		double left = (stopAt.Value - DateTimeOffset.UtcNow).TotalMilliseconds;
		return left < MinimumDeleteMs
			? "not attempted: the response deadline was reached"
			: DeleteOne(record, (int)Math.Min(timeoutMs, left));
	}

	private string DeleteOne(CreatedRecord record, int timeoutMs) {
		try {
			string url = serviceUrlBuilder.Build(ODataKeyFormatter.KeyPath(record.Entity, record.Id));
			string response = applicationClient.ExecuteDeleteRequest(url, string.Empty, Math.Max(1, timeoutMs));
			return ODataKeyedWrite.ValidateWriteResponse(response);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException) {
			return SensitiveErrorTextRedactor.Redact(exception.Message);
		}
	}
}
