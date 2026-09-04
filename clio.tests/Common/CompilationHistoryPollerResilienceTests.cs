using System;
using System.Collections.Generic;
using System.Threading;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Common;
using Clio.CreatioModel;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class CompilationHistoryPollerResilienceTests {

	#region Helpers

	/// <summary>
	/// A provider whose <c>GetItems</c> fails the first <c>failuresBeforeSuccess</c> calls the way
	/// <see cref="ClassifyingDataProvider"/> now surfaces a failed round, then succeeds.
	/// </summary>
	private sealed class FailThenSucceedDataProvider : IDataProvider {

		private readonly int _failuresBeforeSuccess;
		private int _calls;

		internal FailThenSucceedDataProvider(int failuresBeforeSuccess) =>
			_failuresBeforeSuccess = failuresBeforeSuccess;

		internal int Calls => _calls;

		public IItemsResponse GetItems(ISelectQuery selectQuery) {
			int call = Interlocked.Increment(ref _calls);
			return call <= _failuresBeforeSuccess
				? throw new InvalidOperationException(
					"Failed reading records from entity schema 'VwCompilationHistory': timeout")
				: new SucceedingDataProvider().GetItems(selectQuery);
		}

		public IDefaultValuesResponse GetDefaultValues(string schemaName) =>
			new SucceedingDataProvider().GetDefaultValues(schemaName);

		public IExecuteResponse BatchExecute(List<IBaseQuery> queries) =>
			new SucceedingDataProvider().BatchExecute(queries);

		public T GetSysSettingValue<T>(string sysSettingCode) => default;

		public bool GetFeatureEnabled(string featureCode) => true;

		public IExecuteProcessResponse ExecuteProcess(IExecuteProcessRequest request) =>
			new SucceedingDataProvider().ExecuteProcess(request);
	}

	/// <summary>
	/// A provider that fails or succeeds per round according to <c>shouldFail</c>, which receives the
	/// 1-based round number. Lets a test describe an exact failure PATTERN - nine then one, or alternating -
	/// rather than only the two extremes.
	/// </summary>
	private sealed class ScriptedDataProvider : IDataProvider {

		private readonly Func<int, bool> _shouldFail;
		private int _calls;

		internal ScriptedDataProvider(Func<int, bool> shouldFail) => _shouldFail = shouldFail;

		internal int Calls => Volatile.Read(ref _calls);

		public IItemsResponse GetItems(ISelectQuery selectQuery) {
			int call = Interlocked.Increment(ref _calls);
			return _shouldFail(call)
				? throw new InvalidOperationException(
					$"Failed reading records from entity schema 'VwCompilationHistory' on round {call}: timeout")
				: new SucceedingDataProvider().GetItems(selectQuery);
		}

		public IDefaultValuesResponse GetDefaultValues(string schemaName) =>
			new SucceedingDataProvider().GetDefaultValues(schemaName);

		public IExecuteResponse BatchExecute(List<IBaseQuery> queries) =>
			new SucceedingDataProvider().BatchExecute(queries);

		public T GetSysSettingValue<T>(string sysSettingCode) => default;

		public bool GetFeatureEnabled(string featureCode) => true;

		public IExecuteProcessResponse ExecuteProcess(IExecuteProcessRequest request) =>
			new SucceedingDataProvider().ExecuteProcess(request);
	}

	// Rounds run back to back so a test that has to exhaust the ten-round budget does not spend ten real
	// seconds asleep. The interval is not what any of these tests is about.
	private const int TestPollIntervalMilliseconds = 1;

	/// <summary>
	/// Runs <see cref="CompilationHistoryPoller.Poll"/> on its own thread until <paramref name="until"/>
	/// holds (or the thread ends on its own), then cancels and joins, returning whatever escaped.
	/// </summary>
	private static Exception RunPollUntil(IDataProvider provider, Func<bool> until) {
		CompilationHistoryPoller sut = new(provider, TestPollIntervalMilliseconds);
		using CancellationTokenSource cts = new();
		Exception thrown = null;
		Thread pollThread = new(() => {
			try {
				sut.Poll(DateTime.UtcNow.AddMinutes(-1), cts.Token, _ => { });
			} catch (Exception exception) {
				thrown = exception;
			}
		});
		pollThread.Start();
		SpinWait.SpinUntil(() => until() || !pollThread.IsAlive, TimeSpan.FromSeconds(30));
		cts.Cancel();
		pollThread.Join(TimeSpan.FromSeconds(10));
		return thrown;
	}

	#endregion

	[Test]
	[Description("A single failed round must not end the poll: before the classifying decorator an unreachable round came back as an empty list and the loop simply retried, and a compile that runs for minutes cannot be abandoned because one read timed out.")]
	public void Poll_ShouldContinue_AfterOneFailedRound() {
		// Arrange
		FailThenSucceedDataProvider provider = new(failuresBeforeSuccess: 1);
		CompilationHistoryPoller sut = new(provider);
		using CancellationTokenSource cts = new();
		List<CompilationHistory> observed = [];

		// Act
		Exception thrown = null;
		Thread pollThread = new(() => {
			try {
				sut.Poll(DateTime.UtcNow.AddMinutes(-1), cts.Token, observed.Add);
			} catch (Exception exception) {
				thrown = exception;
			}
		});
		pollThread.Start();
		SpinWait.SpinUntil(() => provider.Calls >= 2, TimeSpan.FromSeconds(10));
		cts.Cancel();
		pollThread.Join(TimeSpan.FromSeconds(10));

		// Assert
		thrown.Should().BeNull(
			because: "one transient failure is not a reason to stop watching a compile, and an escaping exception on this thread would terminate the whole clio process");
		provider.Calls.Should().BeGreaterThanOrEqualTo(2,
			because: "the loop has to reach a second round for the tolerance to mean anything");
	}

	[Test]
	[Description("A SUSTAINED run of failures does give up, and it does so by throwing so the caller can report the fault instead of polling silently until its own timeout.")]
	public void Poll_ShouldThrow_AfterTooManyConsecutiveFailedRounds() {
		// Arrange - never succeeds, so the consecutive-failure budget is exhausted.
		CompilationHistoryPoller sut = new(new FailThenSucceedDataProvider(failuresBeforeSuccess: int.MaxValue),
			TestPollIntervalMilliseconds);
		using CancellationTokenSource cts = new();

		// Act
		Action act = () => sut.Poll(DateTime.UtcNow.AddMinutes(-1), cts.Token, _ => { });

		// Assert
		InvalidOperationException exception = act.Should().Throw<InvalidOperationException>(
			because: "an environment that cannot answer at all must be reported, not waited on until the compile timeout expires").Which;
		exception.Message.Should().Contain("consecutive failed",
			because: "the operator has to be told the poll gave up rather than that the compile itself failed");
		exception.InnerException.Should().NotBeNull(
			because: "the last underlying failure is the only diagnosable detail available");
	}

	[Test]
	[Description("Cancellation still ends the poll promptly and without a fault, so the normal end-of-compile path is unaffected by the failure tolerance.")]
	public void Poll_ShouldReturnQuietly_WhenCancelled() {
		// Arrange
		CompilationHistoryPoller sut = new(new SucceedingDataProvider());
		using CancellationTokenSource cts = new();
		cts.Cancel();

		// Act
		Action act = () => sut.Poll(DateTime.UtcNow.AddMinutes(-1), cts.Token, _ => { });

		// Assert
		act.Should().NotThrow(
			because: "cancellation is how a completed compile stops the poll, and it is not a failure");
	}

	[Test]
	[Description("The budget boundary itself: one round short of MaxConsecutiveFailures is still tolerated, and the poll goes on to succeed. Without this, the >= comparison could become off-by-one and every other test would stay green.")]
	public void Poll_ShouldTolerate_OneRoundShortOfTheFailureBudget() {
		// Arrange - fails MaxConsecutiveFailures - 1 times in a row, then answers normally.
		int toleratedFailures = CompilationHistoryPoller.MaxConsecutiveFailures - 1;
		ScriptedDataProvider provider = new(round => round <= toleratedFailures);

		// Act - run past the first success so the loop is demonstrably still alive after the near-miss.
		Exception thrown = RunPollUntil(provider, () => provider.Calls >= toleratedFailures + 2);

		// Assert
		thrown.Should().BeNull(
			because: $"{toleratedFailures} consecutive failures is one short of the budget, and the round after them succeeded");
		provider.Calls.Should().BeGreaterThan(toleratedFailures,
			because: "the loop has to reach the succeeding round for the tolerance to mean anything");
	}

	[Test]
	[Description("The other side of the same boundary: the MaxConsecutiveFailures-th consecutive failure is the one that throws - not the one before it, and not a later one.")]
	public void Poll_ShouldThrow_OnExactlyTheBudgetOfConsecutiveFailures() {
		// Arrange - every round fails, so the budget is reached at exactly MaxConsecutiveFailures rounds.
		ScriptedDataProvider provider = new(_ => true);
		CompilationHistoryPoller sut = new(provider, TestPollIntervalMilliseconds);
		using CancellationTokenSource cts = new();

		// Act
		Action act = () => sut.Poll(DateTime.UtcNow.AddMinutes(-1), cts.Token, _ => { });

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "a sustained run of failures has to be reported rather than polled through");
		provider.Calls.Should().Be(CompilationHistoryPoller.MaxConsecutiveFailures,
			because: "the throw must land on the budget-th round exactly - an earlier one abandons a compile too soon, a later one means the comparison drifted");
	}

	[Test]
	[Description("A successful round RESETS the consecutive counter. Drop that reset - make the budget cumulative instead of consecutive - and every other test here still passes, yet a long healthy compile with scattered timeouts would be aborted.")]
	public void Poll_ShouldResetFailureCount_AfterASuccessfulRound() {
		// Arrange - repeating blocks of (budget - 1) failures followed by one success. The cumulative count
		// passes the budget several times over; the consecutive count never does.
		int blockLength = CompilationHistoryPoller.MaxConsecutiveFailures;
		ScriptedDataProvider provider = new(round => round % blockLength != 0);
		int roundsToRun = blockLength * 3;

		// Act
		Exception thrown = RunPollUntil(provider, () => provider.Calls >= roundsToRun);

		// Assert
		thrown.Should().BeNull(
			because: "no run of failures ever reached the budget, so a cumulative interpretation is the only thing that could have thrown here");
		provider.Calls.Should().BeGreaterThanOrEqualTo(roundsToRun,
			because: "the pattern only accumulates past the budget once several blocks have run");
	}
}
