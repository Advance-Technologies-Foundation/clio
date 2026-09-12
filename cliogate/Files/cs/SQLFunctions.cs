namespace ClioGate.Functions.SQL
{
	using System;
	using System.Data;
	using System.Diagnostics;
	using Common.Logging;
	using Newtonsoft.Json;
	using Terrasoft.Core;
	using Terrasoft.Core.DB;

	/// <summary>Executes SQL requests and persists their diagnostic log.</summary>
	public static class SQLFunctions
	{
		private const string LogTable = "ClioSqlRequestLog";
		private static readonly ILog Log = LogManager.GetLogger(typeof(SQLFunctions));

		private static Guid StartRequest(string script, UserConnection userConnection) {
			Guid id = Guid.NewGuid();
			DateTime now = DateTime.UtcNow;
			int inserted = new Insert(userConnection).Into(LogTable)
				.Set("Id", Column.Parameter(id))
				.Set("Name", Column.Parameter(id.ToString()))
				.Set("CreatedOn", Column.Parameter(now))
				.Set("CreatedById", Column.Parameter(userConnection.CurrentUser.ContactId))
				.Set("ModifiedOn", Column.Parameter(now))
				.Set("ModifiedById", Column.Parameter(userConnection.CurrentUser.ContactId))
				.Set("Sql", Column.Parameter(script))
				.Set("RowCount", Column.Parameter(-1))
				.Set("Completed", Column.Parameter(false))
				.Execute();
			if (inserted != 1) {
				throw new InvalidOperationException("Unable to persist SQL request log; SQL was not executed.");
			}
			return id;
		}

		private static void CompleteRequest(Guid id, Stopwatch timer, int? rowCount, string error,
				UserConnection userConnection) {
			timer.Stop();
			try {
				int updated = new Update(userConnection, LogTable)
					.Set("ModifiedOn", Column.Parameter(DateTime.UtcNow))
					.Set("DurationMs", Column.Parameter(timer.Elapsed.TotalMilliseconds))
					.Set("RowCount", Column.Parameter(rowCount ?? -1))
					.Set("Error", Column.Parameter(error ?? string.Empty))
					.Set("Completed", Column.Parameter(true))
					.Where("Id").IsEqual(Column.Parameter(id))
					.Execute();
				if (updated != 1) {
					Log.Error($"Unable to complete SQL request log {id}: record was not found.");
				}
			} catch (Exception e) {
				// SQL may already be committed: preserve its response so callers do not retry a successful write.
				Log.Error($"Unable to complete SQL request log {id}.", e);
			}
		}

		private static bool GetIsChangeStateScript(string script) {
			var result = script.StartsWith("update") || script.StartsWith("insert") ||
				script.StartsWith("delete");
			return result;
		}

		/// <summary>Logs the full executor SQL before running it and records completion afterward.</summary>
		/// <param name="script">SQL, optionally containing the CLI newline marker.</param>
		/// <param name="userConnection">The authorized caller's connection.</param>
		/// <returns>The existing JSON rows, affected-row count, or ExecuteSQL error response.</returns>
		public static string ExecuteSQL(string script, UserConnection userConnection) {
			Guid requestId = Guid.Empty;
			Stopwatch timer = new Stopwatch();
			int? rowCount = null;
			string error = null;
			try {
				script = script.Replace("|nl|", Environment.NewLine);
				requestId = StartRequest(script, userConnection);
				var query = new CustomQuery(userConnection, script);
				var isChangeStateScript = GetIsChangeStateScript(script);
				timer.Start();
				if (isChangeStateScript) {
					var count = query.Execute();
					timer.Stop();
					rowCount = count >= 0 ? (int?)count : null;
					return count.ToString();
				}
				using (var records = query.ExecuteReader(userConnection.EnsureDBConnection()))
				using (var dataTable = new DataTable { TableName = "clioTable" }) {
					dataTable.Load(records);
					timer.Stop();
					int count = dataTable.Columns.Count > 0 ? dataTable.Rows.Count : records.RecordsAffected;
					rowCount = count >= 0 ? (int?)count : null;
					return JsonConvert.SerializeObject(dataTable, Formatting.Indented);
				}
			}
			catch (Exception e) {
				error = e.Message;
				return "ExecuteSQL ERROR: " + e.Message;
			} finally {
				if (requestId != Guid.Empty) {
					CompleteRequest(requestId, timer, rowCount, error, userConnection);
				}
			}
		}
	}
}
