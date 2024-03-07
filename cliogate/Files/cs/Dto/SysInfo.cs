using System.Runtime.Serialization;
using Terrasoft.Core.ServiceModelContract;

namespace cliogate.Files.cs.Dto
{
	
	[DataContract(Name = nameof(SysInfo))]
	public class SysInfo : BaseResponse
	{

		#region Properties: Public

		[DataMember(Name = nameof(ProductName), Order = 10)]
		public string ProductName { get; set; }
		
		[DataMember(Name = nameof(CoreVersion), Order = 20)]
		public string CoreVersion { get; set; }
		
		[DataMember(Name = nameof(IsNetFramework), Order = 30)]
		public bool IsNetFramework { get; set; }

		[DataMember(Name = nameof(DbEngineType), Order = 40)]
		public string DbEngineType { get; set; }

		[DataMember(Name = nameof(LicenseInfo), Order = 50)]
		public LicenseInfo LicenseInfo { get; set; }
		
		#endregion
	}
	

	[DataContract(Name = nameof(LicenseInfo))]
	public class LicenseInfo
	{

		#region Properties: Public
		[DataMember(Name = nameof(CustomerId))]
		public string CustomerId { get; set; }

		[DataMember(Name = nameof(IsDemoMode))]
		public bool IsDemoMode { get; set; }

		#endregion

	}
}