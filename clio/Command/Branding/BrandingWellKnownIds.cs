using System;

namespace Clio.Command.Branding;

/// <summary>
/// Platform-stable ids the branding flow keys on. They live in one place because the apply side
/// (<see cref="PanelIconBackgroundFeatureManager"/>) and the delivery side
/// (<see cref="BrandingBindingService"/>) must reason about the very same rows: if one copy drifted, the
/// service would silently refuse to package the state row the manager had just written, and the run would
/// report a delivery gap that does not exist.
/// </summary>
internal static class BrandingWellKnownIds {

	/// <summary>
	/// The "All employees" <c>SysAdminUnit</c> — the All-Users role whose setting values and feature states
	/// branding applies to and ships. Stable across Creatio installs, which is what makes an All-Users row
	/// safe to deliver by natural key.
	/// </summary>
	internal static readonly Guid AllUsersAdminUnit = new("a29a3ba5-4b0d-de11-9a51-005056c00008");
}
