<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="ErrorPage.aspx.cs" Inherits="Terrasoft.WebApp.Loader.ErrorPage" %>

<!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.0 Transitional//EN" "http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd">
<html xmlns="http://www.w3.org/1999/xhtml" style="height:100%;">
<head runat="server">
	<script type="text/javascript">
		function showDetail() {
			var labelDetailTextEl = Ext.get('lblDetailMessage');
			var displayStyle = labelDetailTextEl.dom.style['display'];
			labelDetailTextEl.dom.style['display'] = (displayStyle == 'none' ? 'block' : 'none');
		}
	</script>
	<title></title>
</head>
<body style="height:100%;background:#f6f9fa;">
	<form id="FormError" runat="server" style="height:100%;overflow:auto;">
		<TS:ScriptManager ID="ScriptManager" runat="server"></TS:ScriptManager>
		<div style="position:absolute;left:0px;top:0px;">
			<TS:ImageBox ID="ErrorIcon" runat="server" Cls="application-ico-error" />
		</div>
		<div style="margin:20px 20px 20px 90px;">
			<TS:Label runat="server" Cls="x-label-black" ID="ErrorOccures" style="font-weight:bold;" Caption="@Terrasoft.UI.WebControls.MessagePanel,Message.OccuresAnError"></TS:Label>
			<br />
			<asp:HyperLink CssClass="x-label" NavigateUrl="#" ID="lblMessage" runat="server"></asp:HyperLink>
			<TS:Label runat="server" Cls="x-label-black" ID="ErrorSupportInfo" style="font-weight:bold;" Caption="@Terrasoft.Web.Common,ErrorPageMessages.Support.Caption"></TS:Label>
			<br />
			<asp:Label runat="server" Cls="x-label-black" ID="ErrorInfoLabel"></asp:Label>
			<br />
			<asp:HyperLink CssClass="x-label" NavigateUrl="#" ID="SendReportLabel" runat="server"></asp:HyperLink>
			<br />
			<br />
			<asp:HyperLink onclick="showDetail();return false;" CssClass="x-label" NavigateUrl="#" ID="ShowDetailLabel" runat="server"></asp:HyperLink>
			<asp:Label ID="lblDetailMessage" Cls="x-label-black" runat="server" style="display:none;clear:both;white-space:pre-wrap"></asp:Label>
		</div>
	</form>
</body>
</html>
