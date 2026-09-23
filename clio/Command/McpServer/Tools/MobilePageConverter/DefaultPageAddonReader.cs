using System;
using Clio.Command.AddonSchemaDesigner;
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Command.McpServer.Tools.MobilePageConverter;

/// <summary>
/// Reads an object's <c>MobileRelatedPage</c>/<c>RelatedPage</c> add-on and turns the answer into what
/// <see cref="MobileActionTargetProbe"/>'s entity tier needs: whether a default MOBILE page exists
/// (<see cref="ReadMobileState"/>), or, for the WEB counterpart, the untyped default page's UId
/// (<see cref="ReadWebDefaultPageUId"/>). Both share one <see cref="AddonGetRequestDto"/> builder instead of
/// each constructing its own copy.
/// </summary>
internal static class DefaultPageAddonReader {

	/// <summary>
	/// Shared with <see cref="ExistingMobilePageProbe.ProbeSourceEntityDefaultMobilePage"/>, which reads the
	/// same add-on for a different object identity (a resolved entity name rather than an already-known UId)
	/// and is not yet routed through this reader.
	/// </summary>
	internal const string MobileRelatedPageAddonName = "MobileRelatedPage";

	/// <summary>
	/// The WEB counterpart of <see cref="MobileRelatedPageAddonName"/> — the same add-on shape, attached to the
	/// same object, but the one <c>create-related-page-addon --schema-type web</c> writes. Read ONLY for a
	/// target already classified <see cref="ActionTargetState.Missing"/>: it turns the object name into a
	/// candidate WEB edit page the caller can offer to convert next, so a missing mobile default is not just
	/// reported but points at something actionable.
	/// </summary>
	private const string RelatedPageAddonName = "RelatedPage";

	/// <summary>
	/// Reads the object's <c>MobileRelatedPage</c> add-on and reports whether it declares a default page.
	/// Degrades per object rather than aborting the probe.
	/// </summary>
	/// <remarks>
	/// The question is "does this object have AT LEAST ONE default mobile page, in ANY package", so the
	/// request is addressed differently from <c>RelatedPageAddonService.BuildAddonGetRequest</c> — and
	/// deliberately, not as a shortcut. That method resolves the object's own package and parent schema
	/// because it backs a read-modify-WRITE against one package; this read only asks, and
	/// <c>UseFullHierarchy</c> makes the server walk the hierarchy itself. Verified against a stand: the same
	/// object read through four different packages returns an identical page set, so <c>TargetPackageUId</c>
	/// does not select the ANSWER. It still matters for a DIFFERENT reason: <c>GetSchema</c> auto-provisions
	/// an empty descriptor when none exists (see <see cref="MobileActionTargetProbe"/>'s class doc), and that
	/// write lands in whichever package <c>TargetPackageUId</c> names. <paramref name="packageUId"/> is
	/// therefore the OBJECT's own package — resolved by <see cref="MobileActionTargetProbe.ReadEntitySchemaRows"/>
	/// in the same batched <c>SysSchema</c> read that already resolves <paramref name="entitySchemaUId"/>, so at
	/// no extra round trip — falling back to the source page's package only when the object's own could not be
	/// resolved. Never the source page's package outright: that would auto-provision a descriptor for someone
	/// else's object inside the page's own package, to travel with it on the next <c>push-pkg</c>.
	/// </remarks>
	internal static ActionTargetState ReadMobileState(
		MobileActionTargetProbe.ProbeContext context, string entitySchemaUId, Guid packageUId) {
		if (!Guid.TryParse(entitySchemaUId, out Guid entityUId)) {
			return ActionTargetState.Unknown;
		}
		try {
			AddonSchemaDto schema =
				context.AddonClient.GetSchema(BuildRequest(MobileRelatedPageAddonName, entityUId, packageUId));
			return MobileActionTargetProbe.ClassifyRelatedPageMetadata(schema?.MetaData);
		} catch (Exception) {
			return ActionTargetState.Unknown;
		}
	}

	/// <summary>
	/// The PER-OBJECT half of resolving the candidate WEB edit page for a verified-missing
	/// <see cref="MobileActionTargetProbe.KindEntityDefaultMobilePage"/> target: reads the WEB
	/// <see cref="RelatedPageAddonName"/> add-on (the mirror of <see cref="ReadMobileState"/>) and returns the
	/// untyped default page's UId. The UId→NAME step is deliberately NOT here — it runs once for the whole
	/// tier, batched, instead of one by-UId round trip per object.
	/// <para>
	/// Fails open to <see langword="null"/> (never a guess): no add-on configured, no untyped default, or an
	/// unparseable add-on body (<see cref="MobileActionTargetProbe.ExtractDefaultPageSchemaUId"/> already
	/// swallows that, and has its own dedicated coverage) all read the same as "no candidate found".
	/// </para>
	/// <para>
	/// A THROW from the add-on read is DIFFERENT: it is surfaced through <paramref name="failure"/> rather
	/// than swallowed, because "the read never answered" and "the object genuinely has no default page" are
	/// not the same fact. So is a declared <c>PageSchemaUId</c> that does not parse as a GUID — it is failed
	/// HERE rather than sent into the batch, where one authored-garbage value would fail the whole chunk and
	/// cost every SIBLING its candidate. The caller folds either into the tier's degradation note; it never
	/// aborts THIS object's own <c>Missing</c> verdict, which was already settled before this method runs, and
	/// never touches any other object's candidate.
	/// </para>
	/// </summary>
	internal static Guid? ReadWebDefaultPageUId(
		MobileActionTargetProbe.ProbeContext context, string entitySchemaUId, Guid packageUId,
		out Exception failure) {
		failure = null;
		if (!Guid.TryParse(entitySchemaUId, out Guid entityUId)) {
			return null;
		}
		try {
			AddonSchemaDto schema =
				context.AddonClient.GetSchema(BuildRequest(RelatedPageAddonName, entityUId, packageUId));
			string pageSchemaUId = MobileActionTargetProbe.ExtractDefaultPageSchemaUId(schema?.MetaData);
			if (string.IsNullOrWhiteSpace(pageSchemaUId)) {
				return null;
			}
			if (!Guid.TryParse(pageSchemaUId, out Guid pageUId)) {
				failure = new InvalidOperationException(
					$"Page schema '{pageSchemaUId}' could not be resolved to a name.");
				return null;
			}
			return pageUId;
		} catch (Exception ex) {
			failure = ex;
			return null;
		}
	}

	private static AddonGetRequestDto BuildRequest(string addonName, Guid entityUId, Guid packageUId) =>
		new() {
			AddonName = addonName,
			TargetSchemaUId = entityUId,
			TargetParentSchemaUId = Guid.Empty,
			TargetPackageUId = packageUId,
			TargetSchemaManagerName = EntitySchemaManagerName,
			UseFullHierarchy = true
		};
}
