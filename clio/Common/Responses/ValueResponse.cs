using System.Runtime.Serialization;

namespace Clio.Common.Responses;

#region Class: ValueResponse

[DataContract]
public class ValueResponse<TValue> : BaseResponse
{

    #region Properties: Public

    [DataMember(Name = "value")]
    public TValue Value { get; set; }

    #endregion

}

#endregion
