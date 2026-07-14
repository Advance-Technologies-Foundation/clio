using System;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
// This fixture spawns real threads and blocks on multi-second signals to prove the per-key lock
// serializes/overlaps work. Left in the parallel pool (Parallelizable(Fixtures)) it adds thread-pool
// pressure that perturbs timing-sensitive fixtures on net8; run it serially so it neither starves nor
// destabilizes its neighbours.
[NonParallelizable]
public sealed class SectionCreateSerializationGuardTests {
	private static readonly TimeSpan GenerousWait = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan SignalWait = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan NonEntryWindow = TimeSpan.FromMilliseconds(300);

	[Test]
	[Description("Serializes two concurrent Run calls for the same environment + application: the second work does not start until the first releases the per-key lock (ENG-93089 AC-01).")]
	public void Run_ShouldSerializeWork_WhenSameEnvironmentAndApplication() {
		// Arrange
		SectionCreateSerializationGuard guard = new(new NullLogger());
		using ManualResetEventSlim firstEntered = new(false);
		using ManualResetEventSlim releaseFirst = new(false);
		using ManualResetEventSlim secondEntered = new(false);

		// Act
		Task first = Task.Run(() => guard.Run("prod", "UsrApp", GenerousWait, () => {
			firstEntered.Set();
			releaseFirst.Wait(SignalWait);
			return 0;
		}));
		firstEntered.Wait(SignalWait).Should().BeTrue(
			because: "the first Run must acquire the lock and enter its work");
		Task second = Task.Run(() => guard.Run("prod", "UsrApp", GenerousWait, () => {
			secondEntered.Set();
			return 0;
		}));
		bool secondEnteredWhileFirstHeld = secondEntered.Wait(NonEntryWindow);
		releaseFirst.Set();
		Task.WaitAll([first, second], SignalWait);

		// Assert
		secondEnteredWhileFirstHeld.Should().BeFalse(
			because: "a same-key Run must wait for the holder to release before entering its work");
		secondEntered.Wait(SignalWait).Should().BeTrue(
			because: "the second Run must proceed once the first releases the per-key lock");
	}

	[Test]
	[Description("Allows two concurrent Run calls for different application codes to overlap — creations for different applications are not serialized (ENG-93089 AC-02 / FR-10).")]
	public void Run_ShouldAllowOverlap_WhenDifferentApplicationCode() {
		// Arrange
		SectionCreateSerializationGuard guard = new(new NullLogger());
		using ManualResetEventSlim firstEntered = new(false);
		using ManualResetEventSlim releaseFirst = new(false);
		using ManualResetEventSlim secondEntered = new(false);

		// Act
		Task first = Task.Run(() => guard.Run("prod", "UsrAppOne", GenerousWait, () => {
			firstEntered.Set();
			releaseFirst.Wait(SignalWait);
			return 0;
		}));
		firstEntered.Wait(SignalWait).Should().BeTrue(
			because: "the first Run must acquire its application's lock");
		Task second = Task.Run(() => guard.Run("prod", "UsrAppTwo", GenerousWait, () => {
			secondEntered.Set();
			return 0;
		}));
		bool secondOverlapped = secondEntered.Wait(SignalWait);
		releaseFirst.Set();
		Task.WaitAll([first, second], SignalWait);

		// Assert
		secondOverlapped.Should().BeTrue(
			because: "a different application code maps to a different lock, so the two creations run in parallel");
	}

	[Test]
	[Description("Treats environment + application-code case-insensitively: a differently-cased key maps to the same lock and is still serialized (ENG-93089 AC-05).")]
	public void Run_ShouldSerialize_WhenKeyDiffersOnlyByCase() {
		// Arrange
		SectionCreateSerializationGuard guard = new(new NullLogger());
		using ManualResetEventSlim firstEntered = new(false);
		using ManualResetEventSlim releaseFirst = new(false);
		using ManualResetEventSlim secondEntered = new(false);

		// Act
		Task first = Task.Run(() => guard.Run("PROD", "USRAPP", GenerousWait, () => {
			firstEntered.Set();
			releaseFirst.Wait(SignalWait);
			return 0;
		}));
		firstEntered.Wait(SignalWait).Should().BeTrue(
			because: "the first Run must acquire the lock");
		Task second = Task.Run(() => guard.Run("prod", "usrapp", GenerousWait, () => {
			secondEntered.Set();
			return 0;
		}));
		bool secondEnteredWhileFirstHeld = secondEntered.Wait(NonEntryWindow);
		releaseFirst.Set();
		Task.WaitAll([first, second], SignalWait);

		// Assert
		secondEnteredWhileFirstHeld.Should().BeFalse(
			because: "'PROD/USRAPP' and 'prod/usrapp' must map to the same lock and serialize");
	}

	[Test]
	[Description("Distinct environment + application pairs whose naive concatenation would be identical map to different gates (the Unit Separator control character prevents the collision); this assertion does not depend on any specific separator glyph (F8).")]
	public void Run_ShouldAllowOverlap_WhenConcatenationWouldBeAmbiguousWithoutSeparator() {
		// Arrange
		// Without a separator, ("ab","c") and ("a","bc") both concatenate to "abc"; the separator must keep
		// them on distinct gates so they run in parallel.
		SectionCreateSerializationGuard guard = new(new NullLogger());
		using ManualResetEventSlim firstEntered = new(false);
		using ManualResetEventSlim releaseFirst = new(false);
		using ManualResetEventSlim secondEntered = new(false);

		// Act
		Task first = Task.Run(() => guard.Run("ab", "c", GenerousWait, () => {
			firstEntered.Set();
			releaseFirst.Wait(SignalWait);
			return 0;
		}));
		firstEntered.Wait(SignalWait).Should().BeTrue(
			because: "the first Run must acquire its pair's gate");
		Task second = Task.Run(() => guard.Run("a", "bc", GenerousWait, () => {
			secondEntered.Set();
			return 0;
		}));
		bool secondOverlapped = secondEntered.Wait(SignalWait);
		releaseFirst.Set();
		Task.WaitAll([first, second], SignalWait);

		// Assert
		secondOverlapped.Should().BeTrue(
			because: "('ab','c') and ('a','bc') are distinct pairs, so the separator must map them to different gates and let them overlap");
	}

	[Test]
	[Description("Degrades to best-effort when the per-key wait times out: the work still runs (unserialized) and a warning is logged instead of failing (ENG-93089 AC-03 / NFR-02).")]
	public void Run_ShouldProceedUnserializedAndWarn_WhenWaitTimesOut() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		SectionCreateSerializationGuard guard = new(logger);
		using ManualResetEventSlim firstEntered = new(false);
		using ManualResetEventSlim releaseFirst = new(false);
		using ManualResetEventSlim secondEntered = new(false);
		Task first = Task.Run(() => guard.Run("prod", "UsrApp", GenerousWait, () => {
			firstEntered.Set();
			releaseFirst.Wait(SignalWait);
			return 0;
		}));
		firstEntered.Wait(SignalWait).Should().BeTrue(
			because: "the first Run must hold the lock so the second call's wait can time out");

		// Act
		Task second = Task.Run(() => guard.Run("prod", "UsrApp", TimeSpan.FromMilliseconds(100), () => {
			secondEntered.Set();
			return 0;
		}));

		// Assert
		secondEntered.Wait(SignalWait).Should().BeTrue(
			because: "when the bounded wait times out the guard proceeds without the lock so the work is never blocked forever");
		releaseFirst.Set();
		Task.WaitAll([first, second], SignalWait);
		// A wait-timeout degrade must be surfaced as a warning, not swallowed silently.
		logger.Received().WriteWarning(Arg.Is<string>(message =>
			message.Contains("without serialization", StringComparison.OrdinalIgnoreCase)));
	}

	[Test]
	[Description("Releases the per-key lock when the work throws, so a later Run for the same key can still acquire it (no semaphore leak) and the original exception propagates unchanged (ENG-93089 AC-04).")]
	public void Run_ShouldReleaseLockAndPropagate_WhenWorkThrows() {
		// Arrange
		SectionCreateSerializationGuard guard = new(new NullLogger());

		// Act
		Action throwing = () => guard.Run<int>("prod", "UsrApp", GenerousWait, () =>
			throw new InvalidOperationException("boom"));

		// Assert
		throwing.Should().Throw<InvalidOperationException>(
				because: "the guard must not swallow the work's exception")
			.WithMessage("boom");
		// A short bounded wait would time out (and log a warning) if the lock had leaked; instead it acquires
		// immediately and returns, proving the semaphore was released in the finally.
		int result = guard.Run("prod", "UsrApp", TimeSpan.FromMilliseconds(100), () => 42);
		result.Should().Be(42,
			because: "the lock must have been released on the throwing call so the next Run acquires it immediately");
	}

	[Test]
	[Description("Runs the work and returns its result on the uncontended path (ENG-93089 AC-ERR / NFR-01).")]
	public void Run_ShouldReturnWorkResult_WhenUncontended() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		SectionCreateSerializationGuard guard = new(logger);

		// Act
		int result = guard.Run("prod", "UsrApp", GenerousWait, () => 7);

		// Assert
		result.Should().Be(7,
			because: "the uncontended path must run the work and return its value unchanged");
		logger.DidNotReceive().WriteWarning(Arg.Any<string>());
	}

	[Test]
	[Description("Rejects a null work delegate with ArgumentNullException before touching the lock registry.")]
	public void Run_ShouldThrowArgumentNullException_WhenWorkIsNull() {
		// Arrange
		SectionCreateSerializationGuard guard = new(new NullLogger());

		// Act
		Action nullWork = () => guard.Run<int>("prod", "UsrApp", GenerousWait, null!);

		// Assert
		nullWork.Should().Throw<ArgumentNullException>(
			because: "the guard cannot run a null work delegate");
	}
}
