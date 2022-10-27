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

		#endregion

	}

	#endregion

}