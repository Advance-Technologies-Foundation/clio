<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="LicLoader.aspx.cs" Inherits="Terrasoft.WebApp.Loader.Lic.LicLoader" %>

<!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.0 Transitional//EN" "http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd">
<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
	<title></title>
</head>
<body>
	<form id="form1" runat="server">
	<TS:ScriptManager ID="ScriptManager" runat="server" AjaxViewStateMode="Include" />
	<TS:ControlLayout ID="MainControlLayout" runat="server" IsViewPort="true" Direction="Vertical"
		Height="100%" Width="100%">
		<TS:MessagePanel ID="InformationPanel" runat="server" Width="100%" Closable="true" Edges="0 0 1 0" />
		<TS:ControlLayout ID="ContentControlLayout" runat="server" Direction="Vertical"
			Height="100%" Width="100%" DisplayStyle="Controls" Padding="5">
			<TS:Label runat="server" Width="100%" Caption="@Terrasoft.WebApp.Loader,LicLoader.FileUpload.Caption" Margins="5 0 5 0">
			</TS:Label>
			<TS:FileUploadEdit ID="LoadLicenseButtonFileUploadEdit" runat="server"
				Width="100%" >
				<AjaxEvents>
					<Change OnEvent="OnLoadLicenseButtonFileUploadEditChange" />
				</AjaxEvents>
			</TS:FileUploadEdit>
		</TS:ControlLayout>
		<TS:ControlLayout ID="BottomControlLayout" runat="server" Width="100%" DisplayStyle="Footer">
			<TS:Spacer ID="Spacer2" runat="server" Size="100%">
			</TS:Spacer>
			<TS:Button ID="OkButton" runat="server"
				Caption="@Terrasoft.WebApp.Loader,LicLoader.OkButton.Caption" ShowLoadMask="true">
				<AjaxEvents>
					<Click OnEvent="OnOkButtonClick" ShowLoadMask ="true" />
				</AjaxEvents>
			</TS:Button>
			<TS:Button ID="CancelButton" runat="server" Caption="@Terrasoft.WebApp.Loader,LicLoader.CancelButton.Caption">
				<AjaxEvents>
					<Click OnEvent="OnCancelButtonClick" />
				</AjaxEvents>
			</TS:Button>
		</TS:ControlLayout>
	</TS:ControlLayout>
	</form>
</body>
</html>
