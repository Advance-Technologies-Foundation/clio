using System.Collections.Generic;

namespace Clio.Common.Assertions
{
	/// <summary>
	/// Represents the result of an assertion operation.
	/// </summary>
	public interface IAssertionResult
	{
		/// <summary>
		/// Gets the status of the assertion (pass or fail).
		/// </summary>
		string Status { get; }

		/// <summary>
		/// Gets the scope of the assertion.
		/// </summary>
		AssertionScope? Scope { get; }

		/// <summary>
		/// Gets the phase where the assertion failed, if applicable.
		/// </summary>
		AssertionPhase? FailedAt { get; }

		/// <summary>
		/// Gets the reason for failure, if applicable.
		/// </summary>
		string Reason { get; }

		/// <summary>
		/// Gets additional details about the assertion result.
		/// </summary>
		Dictionary<string, object> Details { get; }

		/// <summary>
		/// Converts the result to JSON format.
		/// </summary>
		string ToJson();
	}
}
