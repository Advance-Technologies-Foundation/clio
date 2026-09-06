namespace Clio.Mcp.E2E.Support.Configuration;

/// <summary>
/// Redirects every clio process a fixture spawns at a private clio home.
/// </summary>
/// <remarks>
/// <para>
/// Setting <c>HOME</c> / <c>LOCALAPPDATA</c> alone does NOT isolate a fixture, and a fixture that
/// does only that silently writes the suite's SHARED catalog instead of its own. The reason is
/// precedence: <c>SettingsRepository.AppSettingsFolderPath</c> returns <c>CLIO_HOME</c> verbatim and
/// only falls through to <c>HOME</c> / <c>LOCALAPPDATA</c> when it is unset
/// (<c>clio/Environment/ConfigurationOptions.cs</c>, "the single source of truth for clio's home
/// directory"), and <see cref="TestConfiguration.Load"/> puts the suite-owned <c>CLIO_HOME</c> into
/// <see cref="McpE2ESettings.ProcessEnvironmentVariables"/> for every spawned process. So the
/// suite-wide value wins over any per-fixture <c>HOME</c>, and the intended isolation is inert.
/// </para>
/// <para>
/// That is not a cosmetic mistake. Nine fixtures rewrite the shared <c>appsettings.json</c> through
/// <see cref="TemporaryClioSettingsOverride"/> with a plain, non-atomic <c>File.WriteAllText</c> that
/// takes none of the cross-process locks a real clio writer takes, and one of them
/// (<c>SettingsHealthToolE2ETests</c>) deliberately installs a catalog whose
/// <c>ActiveEnvironmentKey</c> does not resolve. A catalog in that state needs no file damage at all
/// to break the suite: <c>SettingsBootstrapService</c> reports <c>CanExecuteEnvTools = false</c>
/// purely because the active key does not resolve, and every environment-touching tool then answers
/// "clio settings bootstrap is broken" while every reachability probe fails into
/// <c>Assert.Ignore</c>. Let a second writer interleave with that fixture's snapshot/restore and the
/// broken state stops being transient — it survives to the end of the run.
/// </para>
/// <para>
/// Prefer this helper over assigning the variables by hand, so the decisive one is never the one
/// that gets forgotten.
/// </para>
/// </remarks>
internal static class IsolatedClioHome {

	/// <summary>
	/// Points the child clio processes described by <paramref name="settings"/> at
	/// <paramref name="path"/> as their clio home.
	/// </summary>
	/// <param name="settings">The spawn settings whose process environment is redirected.</param>
	/// <param name="path">The private home directory; created when it does not exist.</param>
	/// <returns><paramref name="path"/>, so call sites can assign and redirect in one expression.</returns>
	internal static string Redirect(McpE2ESettings settings, string path) {
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		Directory.CreateDirectory(path);
		// CLIO_HOME is the one that decides; the rest keep a probe that reads the OS user profile
		// directly (agent detection, per-user caches) inside the same private directory.
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = path;
		settings.ProcessEnvironmentVariables[OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME"] = path;
		settings.ProcessEnvironmentVariables["USERPROFILE"] = path;
		return path;
	}

	/// <summary>
	/// Creates a uniquely named private clio home under the temporary directory and redirects
	/// <paramref name="settings"/> at it.
	/// </summary>
	/// <param name="settings">The spawn settings whose process environment is redirected.</param>
	/// <param name="prefix">A short fixture-identifying prefix for the directory name.</param>
	/// <returns>The full path of the created home.</returns>
	internal static string CreateAndRedirect(McpE2ESettings settings, string prefix) {
		ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
		return Redirect(settings, Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}"));
	}
}
