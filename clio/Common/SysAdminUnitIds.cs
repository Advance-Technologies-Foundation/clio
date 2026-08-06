using System;

namespace Clio.Common;

/// <summary>
/// Seeded <c>SysAdminUnit</c> row ids that are identical across every Creatio install.
/// </summary>
internal static class SysAdminUnitIds {

	/// <summary>The "All employees" role — the general audience.</summary>
	internal static readonly Guid AllEmployees = new("a29a3ba5-4b0d-de11-9a51-005056c00008");

	/// <summary>
	/// The portal "All external users" role; Creatio core names this seeded row
	/// <c>SysAdminUnitAllPortalUsersId</c>.
	/// </summary>
	internal static readonly Guid AllPortalUsers = new("720b771c-e7a7-4f31-9cfb-52cd21c3739f");
}
