using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Common.Assertions
{
	/// <summary>
	/// Represents the result of an assertion operation.
	/// </summary>
	public class AssertionResult : IAssertionResult
	{
		/// <summary>
		/// Gets or sets the status of the assertion (pass or fail).
		/// </summary>
		[JsonPropertyName("status")]
		public string Status { get; set; }

		/// <summary>
		/// Gets or sets the scope of the assertion.
		/// </summary>
		[JsonPropertyName("scope")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		[JsonConverter(typeof(JsonStringEnumConverter))]
		public AssertionScope? Scope { get; set; }

		/// <summary>
		/// Gets or sets the phase where the assertion failed, if applicable.
		/// </summary>
		[JsonPropertyName("failedAt")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public AssertionPhase? FailedAt { get; set; }

		/// <summary>
		/// Gets or sets the reason for failure, if applicable.
		/// </summary>
		[JsonPropertyName("reason")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string Reason { get; set; }

		/// <summary>
		/// Gets or sets additional details about the assertion result.
		/// </summary>
		[JsonPropertyName("details")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public Dictionary<string, object> Details { get; set; }

		/// <summary>
		/// Gets or sets data that was resolved during assertion.
		/// </summary>
		[JsonPropertyName("resolved")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public Dictionary<string, object> Resolved { get; set; }

		/// <summary>
		/// Gets or sets context information (e.g., Kubernetes context).
		/// </summary>
		[JsonPropertyName("context")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public Dictionary<string, object> Context { get; set; }

		public AssertionResult()
		{
			Details = new Dictionary<string, object>();
			Resolved = new Dictionary<string, object>();
			Context = new Dictionary<string, object>();
		}

		/// <summary>
		/// Creates a successful assertion result.
		/// </summary>
		public static AssertionResult Success()
		{
			return new AssertionResult { Status = "pass" };
		}

		/// <summary>
		/// Creates a failed assertion result.
		/// </summary>
		public static AssertionResult Failure(AssertionScope scope, AssertionPhase failedAt, string reason)
		{
			return new AssertionResult
			{
				Status = "fail",
				Scope = scope,
				FailedAt = failedAt,
				Reason = reason
			};
		}

		/// <summary>
		/// Converts the result to JSON format.
		/// </summary>
		public string ToJson()
		{
			var options = new JsonSerializerOptions
			{
				WriteIndented = true,
				DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
				Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
				Converters = { new JsonStringEnumConverter() }
			};

			return JsonSerializer.Serialize(this, options);
		}
	}
}
