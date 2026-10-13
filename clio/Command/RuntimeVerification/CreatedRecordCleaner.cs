using System;
using System.Collections.Generic;
using System.Linq;

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
	/// <returns>What was deleted and what was not.</returns>
	CleanupReport Delete(IReadOnlyList<CreatedRecord> records,
		IReadOnlyDictionary<string, IReadOnlyCollection<string>> referencesByEntity, int timeoutMs,
		DateTimeOffset? stopAt = null);
}

internal sealed class CreatedRecordCleaner(IRecordDeletionService deletionService) : ICreatedRecordCleaner {

	private const int MaxPasses = 3;
	private const int MinimumDeleteMs = 1_000;

	/// <inheritdoc />
	public CleanupReport Delete(IReadOnlyList<CreatedRecord> records,
		IReadOnlyDictionary<string, IReadOnlyCollection<string>> referencesByEntity, int timeoutMs,
		DateTimeOffset? stopAt = null) {
		CleanupReport report = new();
		List<CreatedRecord> pending = OrderForDeletion(records, referencesByEntity).ToList();
		Dictionary<CreatedRecord, string> lastError = [];
		for (int pass = 0; pass < MaxPasses && pending.Count > 0; pass++) {
			List<CreatedRecord> refused = [];
			foreach (CreatedRecord record in pending) {
				string error = DeleteBeforeStop(record, timeoutMs, stopAt);
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
		List<string> entities = records.Select(record => record.Entity).Distinct(StringComparer.Ordinal).ToList();
		HashSet<string> present = entities.ToHashSet(StringComparer.Ordinal);
		// Edges child -> parent, self references ignored (handled by the newest-first order inside an entity).
		Dictionary<string, HashSet<string>> parentsOf = entities.ToDictionary(entity => entity,
			entity => (referencesByEntity.TryGetValue(entity, out IReadOnlyCollection<string> refs) ? refs : [])
				.Where(parent => present.Contains(parent) && !string.Equals(parent, entity, StringComparison.Ordinal))
				.ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
		List<string> ordered = [];
		HashSet<string> placed = new(StringComparer.Ordinal);
		while (placed.Count < entities.Count) {
			// An entity no unplaced entity references can go now: nothing left depends on it.
			string next = entities.FirstOrDefault(entity => !placed.Contains(entity)
				&& !entities.Any(other => !placed.Contains(other) && !string.Equals(other, entity, StringComparison.Ordinal)
					&& parentsOf[other].Contains(entity)));
			next ??= entities.First(entity => !placed.Contains(entity));
			ordered.Add(next);
			placed.Add(next);
		}
		return ordered.SelectMany(entity => records
			.Where(record => string.Equals(record.Entity, entity, StringComparison.Ordinal))
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
		RecordDeleteOutcome outcome = deletionService.Delete(record.Entity, record.Id, timeoutMs);
		if (outcome.Success) {
			return null;
		}
		return outcome.ServerDetail is null ? outcome.Error : $"{outcome.Error} The server's own reason: {outcome.ServerDetail}.";
	}
}
