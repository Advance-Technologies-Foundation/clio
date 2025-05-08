using System;
using System.Runtime.Serialization;

namespace Clio.Common.Responses;

[Serializable]
[DataContract]
public class BaseResponse
{
    [DataMember(Name = "success")] public bool Success { get; set; }

    [DataMember(Name = "errorInfo")] public ErrorInfo ErrorInfo { get; set; }
}

[DataContract]
public class ErrorInfo
{
    [DataMember(Name = "errorCode")] public string ErrorCode { get; set; }

    [DataMember(Name = "message")] public string Message { get; set; }

    [DataMember(Name = "stackTrace")] public string StackTrace { get; set; }
}
