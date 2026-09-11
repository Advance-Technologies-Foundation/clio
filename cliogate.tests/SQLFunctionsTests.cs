using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using ClioGate.Functions.SQL;
using FluentAssertions;
using Newtonsoft.Json;
using NSubstitute;
using NUnit.Framework;
using Terrasoft.Configuration.Tests;
using Terrasoft.Core;
using Terrasoft.Core.DB;

namespace cliogate.tests {
	[TestFixture]
	[MockSettings(RequireMock.All)]
	[Category("Unit")]
	public class SQLFunctionsTests : BaseMarketplaceTestFixture {
		private readonly List<object[]> _writes = new List<object[]>();
		private PropertyInfo _denyProperty;
		private object _originalDeny;

		protected override void SetUp() {
			base.SetUp();
			_writes.Clear();
			UserConnection.DBEngine = Substitute.ForPartsOf<DBEngine>();
			_denyProperty = typeof(GlobalAppSettings).GetProperty("DenyCustomQueryApiUsage",
				BindingFlags.Static | BindingFlags.NonPublic);
			_originalDeny = _denyProperty.GetValue(null);
			_denyProperty.SetValue(null, false);
			UserConnection.DBExecutor.Execute(Arg.Any<string>(), Arg.Any<QueryParameterCollection>())
				.Returns(call => {
					if (call.Arg<QueryParameterCollection>() != null) {
						_writes.Add(call.Arg<QueryParameterCollection>().Select(p => p.Value).ToArray());
					}
					return 1;
				});
		}

		protected override void TearDown() {
			_denyProperty.SetValue(null, _originalDeny);
			UserConnection.DBExecutor.ClearReceivedCalls();
			base.TearDown();
		}

		[TestCase(0)]
		[TestCase(2)]
		[Description("The full normalized SQL is persisted before execution, then the same log records SELECT count and duration")]
		public void ExecuteSQL_ShouldLogFullSqlAndCount_WhenSelectReturns(int count) {
			// Arrange
			string sql = "SELECT 'Привет 世界' /*" + new string('x', 12000) + "*/|nl|FROM Contact";
			string normalized = sql.Replace("|nl|", Environment.NewLine);
			using (var data = new DataTable()) {
				data.Columns.Add("Name", typeof(string));
				for (int i = 0; i < count; i++) { data.Rows.Add("Contact" + i); }
				DataTableReader reader = data.CreateDataReader();
				UserConnection.DBExecutor.ExecuteReader(normalized).Returns(_ => {
					_writes.Should().HaveCount(1, "the request must be durable before SQL starts");
					_writes[0].Should().Contain(normalized, "the executor SQL must be retained without truncation");
					return reader;
				});

				// Act
				string result = SQLFunctions.ExecuteSQL(sql, UserConnection);

				// Assert
				using (var resultTable = JsonConvert.DeserializeObject<DataTable>(result)) {
					resultTable.Rows.Count.Should().Be(count, "logging must preserve the existing result payload");
					if (count > 0) {
						resultTable.Rows[0]["Name"].Should().Be("Contact0", "logging must preserve result values");
					}
				}
				_writes.Should().HaveCount(2, "one request must create and complete one log record");
				_writes[1][3].Should().Be(count, "the log count must match materialized rows");
				_writes[1][5].Should().Be(true, "the record must indicate completion");
				_writes[1][0].Should().Be(_writes[0][0], "completion must update the initial record");
				Convert.ToDouble(_writes[1][2]).Should().BeGreaterThanOrEqualTo(0, "elapsed time cannot be negative");
				reader.IsClosed.Should().BeTrue("the result reader must be disposed");
			}
		}

		[TestCase(3)]
		[TestCase(0)]
		[TestCase(-1)]
		[Description("DML responses are preserved and only nonnegative affected-row counts are logged")]
		public void ExecuteSQL_ShouldLogKnownAffectedCount_WhenDmlCompletes(int count) {
			// Arrange
			const string sql = "update Contact set Name = 'Test'";
			UserConnection.DBExecutor.Execute(sql).Returns(count);

			// Act
			string result = SQLFunctions.ExecuteSQL(sql, UserConnection);

			// Assert
			result.Should().Be(count.ToString(), "the affected-row response must remain compatible");
			_writes.Should().HaveCount(2, "the request must be logged and completed");
			_writes[1][3].Should().Be(count >= 0 ? count : -1,
				"unknown affected-row counts must remain unknown");
		}

		[Test]
		[TestCase(3)]
		[TestCase(-1)]
		[Description("Statements without result columns retain their available RecordsAffected count on the reader path")]
		public void ExecuteSQL_ShouldUseAffectedCount_WhenReaderHasNoColumns(int affected) {
			// Arrange
			var reader = Substitute.For<IDataReader>();
			reader.FieldCount.Returns(0);
			reader.RecordsAffected.Returns(affected);
			UserConnection.DBExecutor.ExecuteReader(Arg.Any<string>()).Returns(reader);

			// Act
			string result = SQLFunctions.ExecuteSQL("UPDATE Contact SET Name = 'Test'", UserConnection);

			// Assert
			result.Should().Be("[]", "logging must preserve the existing no-result response");
			_writes[1][3].Should().Be(affected, "available affected counts must not become a fabricated zero");
			reader.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "Dispose").Should().Be(1,
				"readers without results must also be disposed");
		}

		[Test]
		[Description("SQL failures complete the log with an error and no fabricated row count")]
		public void ExecuteSQL_ShouldCompleteLogWithError_WhenSqlFails() {
			// Arrange
			UserConnection.DBExecutor.ExecuteReader(Arg.Any<string>())
				.Returns(_ => { throw new InvalidOperationException("invalid SQL"); });

			// Act
			string result = SQLFunctions.ExecuteSQL("select broken", UserConnection);

			// Assert
			result.Should().Be("ExecuteSQL ERROR: invalid SQL", "the existing error response must be preserved");
			_writes.Should().HaveCount(2, "failed SQL must complete its initial log");
			_writes[1][3].Should().Be(-1, "a failure has no known row count");
			_writes[1][4].Should().Be("invalid SQL", "the failure must be diagnosable");
			_writes[1][5].Should().Be(true, "a failed request is no longer pending");
		}

		[TestCase(true)]
		[TestCase(false)]
		[Description("SQL is never executed when the initial log insert fails or inserts no row")]
		public void ExecuteSQL_ShouldNotExecute_WhenInitialLogFails(bool throws) {
			// Arrange
			UserConnection.DBExecutor.Execute(Arg.Any<string>(), Arg.Any<QueryParameterCollection>())
				.Returns(_ => throws ? throw new InvalidOperationException("log unavailable") : 0);

			// Act
			string result = SQLFunctions.ExecuteSQL("select 1", UserConnection);

			// Assert
			result.Should().StartWith("ExecuteSQL ERROR:", "unlogged execution must be refused");
			UserConnection.DBExecutor.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "ExecuteReader")
				.Should().Be(0, "the SQL executor must not run before a successful log insert");
		}

		[Test]
		[Description("Completion logging failures cannot misreport a committed SQL write as failed")]
		public void ExecuteSQL_ShouldPreserveSuccess_WhenCompletionLogFails() {
			// Arrange
			int calls = 0;
			UserConnection.DBExecutor.Execute(Arg.Any<string>(), Arg.Any<QueryParameterCollection>())
				.Returns(_ => ++calls == 1 ? 1 : throw new InvalidOperationException("log update unavailable"));
			UserConnection.DBExecutor.Execute("delete from Contact").Returns(2);

			// Act
			string result = SQLFunctions.ExecuteSQL("delete from Contact", UserConnection);

			// Assert
			result.Should().Be("2", "reporting committed SQL as failed could cause an unsafe retry");
			calls.Should().Be(2, "completion persistence must have been attempted");
		}
	}
}
