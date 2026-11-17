using System.Collections.Generic;
using System.Linq;
using NSubstitute;

namespace Clio.Tests.Command.ObjectRights;

/// <summary>
/// The calls a substitute received, by method name, so a test asserts them with FluentAssertions and a
/// <c>because</c>, as the repository's test style asks of every assertion (NSubstitute's <c>Received()</c> takes none).
/// </summary>
internal static class ReceivedCallLog {

	/// <summary>The arguments of every call <paramref name="substitute"/> received to <paramref name="method"/>, in
	/// order.</summary>
	/// <param name="substitute">The substitute.</param>
	/// <param name="method">The method's name, for example <c>nameof(IReader.Read)</c>.</param>
	/// <returns>One argument array per call.</returns>
	internal static IReadOnlyList<object[]> CallsTo(object substitute, string method) =>
		substitute.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == method)
			.Select(call => call.GetArguments())
			.ToArray();
}
