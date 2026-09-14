using System;
using System.Threading;
using Clio.Tests.Command;
using Microsoft.Extensions.DependencyInjection;

namespace Clio.Tests;

/// <summary>
/// The one container the test assembly resolves shared test helpers from.
/// <para>Thread safety is the whole design constraint here, not an afterthought. Every closed generic of
/// <c>BaseCommandTests&lt;T&gt;</c> — 157 of them — initialises a static field from <see cref="GetService{T}"/>,
/// and <c>[assembly: Parallelizable(ParallelScope.Fixtures)]</c> runs several fixtures at once, so that many
/// static initialisers can be inside this class concurrently. The previous shape published the scope with
/// <c>_scope ??= Init()</c>, a non-atomic read-modify-write, over a SHARED <see cref="ServiceCollection"/> that
/// <c>Init</c> then mutated. Two concurrent registrations landing in one <c>List&lt;ServiceDescriptor&gt;</c>
/// during a capacity resize copy into an array sized from a stale length, which throws "Destination array was
/// not long enough" — and because it happens inside a static field initialiser, the failing test reports only
/// <c>TypeInitializationException</c>, naming neither the collection nor the fixture that lost the race.</para>
/// <para>So there are two guarantees to keep, and dropping either brings the flake back: the factory runs
/// EXACTLY ONCE and its result is published under the same lock (<see cref="Lazy{T}"/> with
/// <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>), and it owns its collection rather than mutating
/// a static one, so there is no shared state left to tear even if a future edit re-entered it.</para>
/// </summary>
public static class ClioTestsSetup
{

	private static readonly Lazy<IServiceScope> Scope =
		new(BuildScope, LazyThreadSafetyMode.ExecutionAndPublication);

	/// <summary>Resolves a shared test helper, building the container on first use.</summary>
	public static T GetService<T>() => Scope.Value.ServiceProvider.GetService<T>();

	private static IServiceScope BuildScope() {
		// Local, never a static field: registration mutates this collection, and a mutable static one is what
		// made concurrent first-use corrupt the descriptor list rather than merely duplicate work.
		ServiceCollection services = new();
		services.AddTransient<ReadmeChecker>();
		return services.BuildServiceProvider().CreateScope();
	}

}
