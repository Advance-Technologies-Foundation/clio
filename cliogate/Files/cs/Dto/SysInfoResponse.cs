using System.Runtime.Serialization;
using Terrasoft.Core.ServiceModelContract;

namespace cliogate.Files.cs.Dto
{
	
	[DataContract(Name = nameof(SysInfoResponse))]
	public class SysInfoResponse : BaseResponse
	{

		#region Properties: Public


		[DataMember(Name = nameof(SysInfo), Order = 10)]
		public CreatioPlatformInfo SysInfo { get; set; }
		
		
		#endregion
	}
	
	[DataContract]
	public class CreatioPlatformInfo
	{

		[DataMember(Name = nameof(ProductName), Order = 10)]
		public string ProductName { get; set; }
		
		[DataMember(Name = nameof(CoreVersion), Order = 20)]
		public string CoreVersion { get; set; }
		
		[DataMember(Name = nameof(Runtime), Order = 30)]
		public string Runtime { get; set; }

		[DataMember(Name = nameof(DbEngineType), Order = 40)]
		public string DbEngineType { get; set; }

		[DataMember(Name = nameof(LicenseInfo), Order = 50)]
		public LicenseInfo LicenseInfo { get; set; }
		
		[DataMember(Name = nameof(IsNetCore), Order = 60)]
		public bool IsNetCore { get; set; }
		

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
