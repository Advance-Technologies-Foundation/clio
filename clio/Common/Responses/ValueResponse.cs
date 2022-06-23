namespace Clio.Common.Responses
{
	using System.Runtime.Serialization;

	#region Class: ValueResponse

	[DataContract]
	public class ValueResponse<TValue> : BaseResponse
	{

		#region Constructors: Public

		public ValueResponse() { }

		#endregion

		#region Properties: Public

		[DataMember(Name = "value")]
		public TValue Value { get; set; }

		#endregion

	}

	#endregion

}