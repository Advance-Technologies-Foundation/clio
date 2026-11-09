using System;
using Clio.Command.McpServer.Tools;
using Clio.Common;

namespace Clio.Command.Branding;

public interface IBrandingBackgroundService
{
	bool TryFindUploadedImage(Guid imageId, out bool exists, out string error);

	bool TryTurnOffPanelIconBackground(string appliedBackground, out string warning);
}

internal sealed class BrandingBackgroundService(IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder, IFeatureStateService featureState) : IBrandingBackgroundService
{
	internal const string PanelIconBackgroundFeatureCode = "UsePanelIconBackground";

	private const string SysImageSchema = "SysImage";

	public bool TryFindUploadedImage(Guid imageId, out bool exists, out string error) {
		bool checkedImage = ODataSingleIdQuery.TryRead(applicationClient, serviceUrlBuilder,
			$"{ODataKeyFormatter.CollectionPath(SysImageSchema)}?$filter=Id eq {imageId}&$select=Id&$top=1",
			out Guid? foundId, out error);
		exists = foundId is not null;
		return checkedImage;
	}

	public bool TryTurnOffPanelIconBackground(string appliedBackground, out string warning) {
		warning = null;
		try {
			featureState.SetFeatureState(PanelIconBackgroundFeatureCode, SysAdminUnitIds.AllEmployees, state: false);
			return true;
		} catch (Exception exception) {
			warning = $"{appliedBackground} was applied, but turning off the {PanelIconBackgroundFeatureCode} " +
				$"feature failed, so the panel may still hide it: {exception.Message}";
			return false;
		}
	}
}
