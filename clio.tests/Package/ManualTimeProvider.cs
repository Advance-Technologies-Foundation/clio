namespace Clio.Tests.Package;

using System;
using System.Threading;
using Clio.Common;

/// <summary>
/// A clock that moves only when <see cref="Advance"/> is called, so a wait can be measured without sleeping.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider {

	#region Fields: Private

	private DateTimeOffset _now = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);

	#endregion

	#region Properties: Public

	/// <inheritdoc />
	public override long TimestampFrequency => TimeSpan.TicksPerSecond;

	#endregion

	#region Methods: Public

	/// <summary>Moves the clock forward.</summary>
	/// <param name="duration">How far to move it.</param>
	public void Advance(TimeSpan duration) => _now += duration;

	/// <inheritdoc />
	public override DateTimeOffset GetUtcNow() => _now;

	/// <inheritdoc />
	public override long GetTimestamp() => _now.UtcTicks;

	#endregion

}

/// <summary>
/// A pause that advances <see cref="ManualTimeProvider"/> by its duration instead of waiting, and counts itself.
/// </summary>
internal sealed class ClockAdvancingDelay : ICancellableDelay {

	#region Fields: Private

	private readonly ManualTimeProvider _clock;

	#endregion

	#region Constructors: Public

	/// <summary>Initializes a new instance of the <see cref="ClockAdvancingDelay" /> class.</summary>
	/// <param name="clock">The clock each pause advances.</param>
	public ClockAdvancingDelay(ManualTimeProvider clock) {
		_clock = clock;
	}

	#endregion

	#region Properties: Public

	/// <summary>How many pauses were taken.</summary>
	public int Calls { get; private set; }

	#endregion

	#region Methods: Public

	/// <inheritdoc />
	public bool WaitOrCancelled(TimeSpan duration, CancellationToken ct) {
		Calls++;
		_clock.Advance(duration);
		return false;
	}

	#endregion

}
