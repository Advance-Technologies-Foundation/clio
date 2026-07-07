using System;
using Clio.Common.Responses;

namespace Clio.Common
{

	#region Interface: IJsonResponseFormater

	public interface IJsonResponseFormater
	{

		#region Methods: Public

		string Format<T>(T value);
		string Format(Exception exception);

		/// <summary>
		/// Serializes a successful result into the unified command <c>--json</c> envelope
		/// (<see cref="Clio.Common.Responses.CommandEnvelope{TData}"/>): <c>ok:true</c>, the payload in
		/// <c>data</c>, and <c>error:null</c>.
		/// </summary>
		/// <typeparam name="T">Type of the command success payload.</typeparam>
		/// <param name="command">Canonical kebab-case command name (e.g. <c>list-packages</c>).</param>
		/// <param name="data">The command success payload.</param>
		/// <returns>The serialized envelope as a single JSON document.</returns>
		string FormatEnvelope<T>(string command, T data);

		/// <summary>
		/// Serializes a failure into the unified command <c>--json</c> envelope: <c>ok:false</c>,
		/// <c>data:null</c>, and an <c>error</c> object carrying the stable code and message.
		/// </summary>
		/// <param name="command">Canonical kebab-case command name (e.g. <c>list-packages</c>).</param>
		/// <param name="errorCode">Stable, machine-readable error code (see <see cref="Clio.Common.CommandErrorCodes"/>).</param>
		/// <param name="errorMessage">User-friendly, actionable error message.</param>
		/// <returns>The serialized error envelope as a single JSON document.</returns>
		string FormatEnvelope(string command, string errorCode, string errorMessage);

		#endregion

	}

	#endregion

	#region Class: JsonResponseFormater

	public class JsonResponseFormater : IJsonResponseFormater
	{

		#region Fields: Private

		private readonly IJsonConverter _jsonConverter;

		#endregion

		#region Constructors: Public

		public JsonResponseFormater(IJsonConverter jsonConverter) {
			jsonConverter.CheckArgumentNull(nameof(jsonConverter));
			_jsonConverter = jsonConverter;
		}

		#endregion

		#region Methods: Public

		public string Format<T>(T value) {
			var valueResponse = new ValueResponse<T> {
				Value = value,
				Success = true,
				ErrorInfo = null
			};
			return _jsonConverter.SerializeObject(valueResponse);
		}

		public string Format(Exception exception) {
			var valueResponse = new ValueResponse<object> {
				Value = null,
				Success = false,
				ErrorInfo = new ErrorInfo {
					Message = exception.Message,
					StackTrace = exception.StackTrace,
					ErrorCode = string.Empty
				}
			};
			return _jsonConverter.SerializeObject(valueResponse);
		}

		public string FormatEnvelope<T>(string command, T data) {
			var envelope = new CommandEnvelope<T> {
				Ok = true,
				Command = command,
				Data = data,
				Error = null
			};
			return _jsonConverter.SerializeObject(envelope);
		}

		public string FormatEnvelope(string command, string errorCode, string errorMessage) {
			var envelope = new CommandEnvelope<object> {
				Ok = false,
				Command = command,
				Data = null,
				Error = new CommandError(errorCode, errorMessage)
			};
			return _jsonConverter.SerializeObject(envelope);
		}

		#endregion

	}

	#endregion

}