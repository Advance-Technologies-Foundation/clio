using System.Linq;
using System.Reflection;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests;

/// <summary>
/// Guards the thread safety of the test assembly's own service locator, which 157 static field initialisers
/// reach under parallel fixture execution.
/// <para>The guard is STRUCTURAL rather than a concurrency test, and that is a deliberate choice rather than a
/// shortcut. The racing window opens exactly ONCE per assembly load — at genuine first use, before the scope is
/// published — so an in-process test cannot reopen it: by the time the test body runs, some fixture has already
/// initialised the locator. A hammer-with-N-threads test was written first and PASSED against the original
/// broken implementation on every run, which is the definition of a test that does not guard anything. What can
/// be checked deterministically is the shape that made the window reachable at all.</para>
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class ClioTestsSetupTests {

	[Test]
	[Description("The locator declares no mutable static state. Two faults produced the original flake and both live in that shape: publishing the scope through a writable static field is a non-atomic read-modify-write, so the factory could run more than once, and the factory then mutated a SHARED ServiceCollection — a List<ServiceDescriptor> underneath, whose backing array two concurrent Adds tear during a resize, throwing 'Destination array was not long enough' inside a static initialiser and surfacing as a TypeInitializationException against whichever fixture touched it first.")]
	public void ClioTestsSetup_ShouldDeclareNoMutableStaticState() {
		// Arrange
		FieldInfo[] mutable = typeof(ClioTestsSetup)
			.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
			.Where(field => !field.IsInitOnly && !field.IsLiteral)
			.ToArray();

		// Act & Assert
		mutable.Should().BeEmpty(
			because: "the container must be published once, under a lock, from a factory that owns everything it "
				+ "mutates — Lazy<T> with ExecutionAndPublication does both, and a writable static field is the "
				+ "one shape that reintroduces the race it closed. Found: "
				+ string.Join(", ", mutable.Select(field => field.Name)));
	}

}
