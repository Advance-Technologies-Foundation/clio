using System.Collections.Generic;

namespace Clio.Command.PageModalSize;

/// <summary>Identifies the modal page whose size is read, as seen from a package.</summary>
/// <param name="PackageName">The package the page is seen from; also the package a later write goes to.</param>
/// <param name="PageSchemaName">The modal page schema name.</param>
public sealed record PageModalSizeReadRequest(string PackageName, string PageSchemaName);

/// <summary>Sets the size of a modal page in the package the caller names.</summary>
/// <param name="PackageName">The package the <c>AppearanceSettings</c> add-on is written to.</param>
/// <param name="PageSchemaName">The modal page schema name.</param>
/// <param name="Size">A size name or designer caption; see <see cref="PageModalSizeCatalog"/>.</param>
public sealed record PageModalSizeWriteRequest(string PackageName, string PageSchemaName, string Size);

/// <summary>The modal size of a page as the <c>AppearanceSettings</c> add-on stores it.</summary>
/// <param name="PageSchemaName">The page schema name.</param>
/// <param name="PackageName">The package the page was read from.</param>
/// <param name="Size">The stored size name, or <c>null</c> when no size is set or the value is not a designer
/// size.</param>
/// <param name="SizeValue">The stored <c>modalSettings.size</c> value, or <c>null</c> when no size is set.</param>
/// <param name="SourcePackageName">The package of the add-on layer the size comes from, or <c>null</c> when no
/// size is set.</param>
/// <param name="EffectiveSize">The size the window opens with: the stored size, or the runtime default when no
/// size is set.</param>
/// <param name="AvailableSizes">The size names a write accepts.</param>
/// <param name="Warning">Non-fatal warnings, or <c>null</c>: a layer above the package overrides the size the
/// package sees (a write to this package would not change the window), the add-on metadata is not a JSON object, the
/// size the window opens with could not be checked, or the package the size comes from could not be
/// determined.</param>
public sealed record PageModalSizeReadResult(
	string PageSchemaName,
	string PackageName,
	string Size,
	int? SizeValue,
	string SourcePackageName,
	string EffectiveSize,
	IReadOnlyList<string> AvailableSizes,
	string Warning = null);

/// <summary>The outcome of a modal size write.</summary>
/// <param name="PageSchemaName">The page schema name.</param>
/// <param name="PackageName">The package the add-on was written to.</param>
/// <param name="PreviousSize">The size name stored before the write, or <c>null</c> when none was set (the window
/// opened small) or the stored value is not a designer size.</param>
/// <param name="PreviousSizeValue">The raw <c>modalSettings.size</c> stored before the write, or <c>null</c> when
/// none was set.</param>
/// <param name="PreviousSourcePackageName">The package the previous size came from, or <c>null</c> when none
/// was set.</param>
/// <param name="Size">The size written.</param>
/// <param name="Warning">Non-fatal warnings, or <c>null</c>: a package above overrides the written size, the size
/// the window opens with could not be checked, the previous size's package could not be determined, or a post-save
/// cache reset or static-content rebuild failed.</param>
public sealed record PageModalSizeWriteResult(
	string PageSchemaName,
	string PackageName,
	string PreviousSize,
	int? PreviousSizeValue,
	string PreviousSourcePackageName,
	string Size,
	string Warning);

/// <summary>
/// Reads and writes the modal window size of a Freedom UI page, which lives in the page's
/// <c>AppearanceSettings</c> add-on (<c>modalSettings.size</c>), not in the page body.
/// </summary>
public interface IPageModalSizeService {
	/// <summary>Reads the stored size of a modal page and the layer it comes from. Makes no changes.</summary>
	/// <exception cref="System.ArgumentException">A required field is missing.</exception>
	/// <exception cref="System.InvalidOperationException">The page or package cannot be resolved, or the page is
	/// not a modal page.</exception>
	PageModalSizeReadResult Get(PageModalSizeReadRequest request);

	/// <summary>
	/// Writes the size to the page's <c>AppearanceSettings</c> add-on in the requested package, keeping every other
	/// field of the add-on metadata, then refreshes the client script cache and static content. Writes nothing when the
	/// size is already stored in the requested package and the window opens with it.
	/// </summary>
	/// <exception cref="System.ArgumentException">A required field is missing or the size is not supported.</exception>
	/// <exception cref="System.InvalidOperationException">The page or package cannot be resolved, the page is not a
	/// modal page, the add-on metadata is not a JSON object, or a package above the requested one sets the
	/// size.</exception>
	PageModalSizeWriteResult Set(PageModalSizeWriteRequest request);
}
