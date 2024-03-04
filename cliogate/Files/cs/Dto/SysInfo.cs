using System.Runtime.Serialization;
using Terrasoft.Core.ServiceModelContract;

namespace cliogate.Files.cs.Dto
{
	
	[DataContract(Name = nameof(SysInfo))]
	public class SysInfo : BaseResponse
	{

		#region Properties: Public

		[DataMember(Name = nameof(CoreVersion))]
		public string CoreVersion { get; set; }

		[DataMember(Name = nameof(DbInfo))]
		public DbInfo DbInfo { get; set; }

		[DataMember(Name = nameof(IsNetFramework))]
		public bool IsNetFramework { get; set; }

		[DataMember(Name = nameof(LicenseInfo))]
		public LicenseInfo LicenseInfo { get; set; }

		[DataMember(Name = nameof(OsInfo))]
		public OsInfo OsInfo { get; set; }

		#endregion

	}

	[DataContract(Name = nameof(DbInfo))]
	public class DbInfo
	{

		#region Properties: Public

		[DataMember(Name = nameof(DbDescription))]
		public string DbDescription { get; set; }

		[DataMember(Name = nameof(DbEngineType))]
		public string DbEngineType { get; set; }

		#endregion

	}

	[DataContract(Name = nameof(OsInfo))]
	public class OsInfo
	{

		#region Properties: Public

		[DataMember(Name = nameof(FrameworkDescription))]
		public string FrameworkDescription { get; set; }

		[DataMember(Name = nameof(OsArchitecture))]
		public string OsArchitecture { get; set; }

		[DataMember(Name = nameof(OsDescription))]
		public string OsDescription { get; set; }

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