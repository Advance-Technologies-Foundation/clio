using System;
using System.Collections.Generic;
using Clio.Common;
using Newtonsoft.Json;

namespace Clio.Package;

#region Class: PackageDescriptor

public class PackageDescriptor{
	#region Properties: Public

	public Guid UId { get; set; }
	public string PackageVersion { get; set; }
	public string Name { get; set; }
	public PackageType Type { get; set; } = PackageType.General;
	public string ProjectPath { get; set; } = string.Empty;
	public string ModifiedOnUtc { get; set; }
	public string Maintainer { get; set; }
	
	[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
	public int? InstallBehavior { get; set; }
	public IList<PackageDependency> DependsOn { get; set; }

	#endregion

	#region Methods: Private

	/// <summary>
	/// Truncates <paramref name="dt"/> to whole seconds, PRESERVING its <see cref="DateTimeKind"/>.
	/// </summary>
	/// <remarks>
	/// Carrying <c>dt.Kind</c> is the whole point of this overload argument. The component constructor
	/// without it yields <see cref="DateTimeKind.Unspecified"/>, and
	/// <see cref="DateTime.ToUniversalTime"/> — which <see cref="UnixTimeConverter.CovertToUnixDateTime"/>
	/// applies next — treats an Unspecified value as LOCAL. So dropping the kind silently made the
	/// conversion correct for a <see cref="DateTime.Now"/> input and wrong for a
	/// <see cref="DateTime.UtcNow"/> one, shifting the latter back by the local offset. Both inputs are
	/// correct now.
	/// </remarks>
	private static DateTime ClearMilliseconds(DateTime dt) {
		return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Kind);
	}

	#endregion

	#region Methods: Public

	public static string ConvertToModifiedOnUtc(DateTime dateTime) {
		long unixDateTime = UnixTimeConverter.CovertToUnixDateTime(ClearMilliseconds(dateTime));
		return $"/Date({unixDateTime})/";
	}

	#endregion
}

#endregion
