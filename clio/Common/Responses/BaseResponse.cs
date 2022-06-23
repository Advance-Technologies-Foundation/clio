namespace Clio.Common.Responses
{
	using System;
	using System.Runtime.Serialization;

	#region Class: BaseResponse

	[Serializable, DataContract]
	public class BaseResponse
	{

		#region Constructors: Public

		public BaseResponse() { }

		#endregion

		#region Properties: Public

		[DataMember(Name = "success")]
		public bool Success { get; set; }
		[DataMember(Name = "errorInfo")]
		public ErrorInfo ErrorInfo { get; set; }

		#endregion

	}

	#endregion

	#region Class: ErrorInfo

	[DataContract]
	public class ErrorInfo
	{

		#region Constructors: Public

		public ErrorInfo() { }

		#endregion

		#region Properties: Public

		[DataMember(Name = "errorCode")]
		public string ErrorCode { get; set; }
		[DataMember(Name = "message")]
		public string Message { get; set; }
		[DataMember(Name = "stackTrace")]
		public string StackTrace { get; set; }

		#endregion

	}

	#endregion

}