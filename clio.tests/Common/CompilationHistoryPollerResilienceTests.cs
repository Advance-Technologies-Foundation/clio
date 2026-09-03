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
		CompilationHistoryPoller sut = new(new FailThenSucceedDataProvider(failuresBeforeSuccess: int.MaxValue));
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
}
