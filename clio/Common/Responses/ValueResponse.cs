using System.Runtime.Serialization;

namespace Clio.Common.Responses;

[DataContract]
public class ValueResponse<TValue> : BaseResponse
{
    [DataMember(Name = "value")] public TValue Value { get; set; }
}
