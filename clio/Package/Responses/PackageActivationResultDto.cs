using System.Runtime.Serialization;

namespace Clio.Package.Responses
{
	#region Class: PackageActivationResultDto

	public class PackageActivationResultDto
	{

		#region Properties: Public

		/// <summary>
		/// Is activation successful.
		/// </summary>
		[DataMember(Name = "success")]
		public bool Success { get; set; }

		/// <summary>
		/// Package name.
		/// </summary>
		[DataMember(Name = "packageName")]
		public string PackageName { get; set; }

		/// <summary>
		/// Activation error message.
		/// </summary>
		[DataMember(Name = "message")]
		public string Message { get; set; }

		#endregion

	}

	#endregion

}
