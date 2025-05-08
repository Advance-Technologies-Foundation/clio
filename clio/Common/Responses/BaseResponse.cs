using System;
using System.Runtime.Serialization;

namespace Clio.Common.Responses;

#region Class: BaseResponse

[Serializable]
[DataContract]
public class BaseResponse
{

    #region Properties: Public

    [DataMember(Name = "errorInfo")]
    public ErrorInfo ErrorInfo { get; set; }

    [DataMember(Name = "success")]
    public bool Success { get; set; }

    #endregion

}

#endregion

#region Class: ErrorInfo

[DataContract]
public class ErrorInfo
{

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
