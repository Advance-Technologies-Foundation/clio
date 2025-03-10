<%@ Page Language="C#" Caption="@Terrasoft.WebApp.Loader,Workspaces.Caption" AutoEventWireup="true"
	CodeBehind="LicManager.aspx.cs" Inherits="Terrasoft.WebApp.Loader.Lic.LicManager" %>

<!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.0 Transitional//EN" "http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd">
<html xmlns="http://www.w3.org/1999/xhtml">
<head id="Head1" runat="server">
	<script language="javascript" type="text/javascript">

		function onClearSearchValue(e) {
			SearchValue.setValue('');
			ToolButtonClearSearch.hide();
			SearchValue.getEl().removeClass('x-qf-text');
			UsedLincensesGrid.refreshData();
		}

		function onSearchValueSpecialKey(el, e) {
			var key = e.getKey();
			var startText = SearchValue.startValue;
			var text = SearchValue.getValue();
			var showToolButton = true;
			var refreshData = true;
			var keyHandled = false;
			switch (key) {
				case e.ENTER:
					keyHandled = true;
					showToolButton = Ext.isEmpty(text) ? false : true;
					break;
				case e.ESC:
					keyHandled = true;
					showToolButton = false;
					refreshData = !Ext.isEmpty(startText);
					SearchValue.setValue('');
					break;
			}
			if (keyHandled === true) {
				if (showToolButton === true) {
					ToolButtonClearSearch.show();
					SearchValue.getEl().addClass('x-qf-text');
				} else {
					ToolButtonClearSearch.hide();
					SearchValue.getEl().removeClass('x-qf-text');
				}
				if (refreshData === true) {
					UsedLincensesGrid.refreshData();
				}
				UsedLincensesGrid.focus();
			}
		}

		function onSearchButtonClientClick(e) {
			ToolButtonClearSearch.show();
			UsedLincensesGrid.refreshData();
		}

	</script>
</head>
<body>
	<form id="form1" runat="server">
		<TS:ScriptManager ID="ScriptManager" runat="server" AjaxViewStateMode="Include" />
		<TS:EntityDataSource ID="PaidLicensesDataSource" runat="server" SchemaUId="{304B4026-1343-4F34-B937-F65DA9E50BFC}"
			ManagerName="SystemEntitySchemaManager" HierarchicalDepth="1" UseProfile="false"
			PageRowsCount="-1" />
		<TS:VirtualDataSource ID="UsedLincensesDataSource" runat="server" />
		<TS:ControlLayout ID="MainControlLayout" runat="server" IsViewPort="true" Direction="Vertical"
			Height="100%" Width="100%">
			<TS:MessagePanel ID="InformationPanel" runat="server" Width="100%" Closable="true" />
			<TS:ControlLayout ID="ContentControlLayout" runat="server" Direction="Horizontal"
				Height="100%" Width="100%">
				<TS:ControlLayout ID="LeftControlLayout" runat="server" Direction="Vertical" Height="100%"
					Width="220px" Padding="5 0 0 5" HasSplitter="true" Edges="0 1 1 0">
					<TS:TextEdit ID="CustomerIdEdit" runat="server" Width="210px" CaptionPosition="Top"
						Caption="@Terrasoft.WebApp.Loader,LicManager.CustomerIdEdit.Caption">
						<AjaxEvents>
							<Change OnEvent="OnCustomerIdEditChange" />
						</AjaxEvents>
					</TS:TextEdit>
					<TS:Button ID="CreateLicenceRequestButton" runat="server" Width="210px" Margins="5 0 0 0"
						Caption="@Terrasoft.WebApp.Loader,LicManager.CreateLicenceRequest.Caption">
						<AjaxEvents>
							<Click OnEvent="OnCreateLicenceRequestButtonClick" IsUpload="true" />
						</AjaxEvents>
					</TS:Button>
					<TS:Button ID="LoadLicenseButton" runat="server" Width="210px" CaptionPosition="Top" Margins="5 0 0 0"
						Caption="@Terrasoft.WebApp.Loader,LicManager.LoadLicense.Caption">
						<AjaxEvents>
							<Click OnEvent="OnLoadLicenseButtonClick" />
						</AjaxEvents>
					</TS:Button>
					<TS:Button ID="DeleteLicenseButton" runat="server" Width="210px" CaptionPosition="Top" Margins="5 0 0 0"
						Caption="@Terrasoft.WebApp.Loader,LicManager.DeleteLicense.Caption">
						<AjaxEvents>
							<Click OnEvent="OnDeleteLicensesButtonClick" ConfirmationTitle="@Terrasoft.WebApp.Loader,LicManager.DeleteLicense.Caption" ConfirmationMessage="@Terrasoft.WebApp.Loader,LicManager.DeleteLicenseConfirmation.Message" IsConfirmationEnabled="True"/>
						</AjaxEvents>
					</TS:Button>
					<TS:Spacer ID="Spacer1" runat="server" Size="100%" />
				</TS:ControlLayout>
				<TS:ControlLayout ID="TabControlLayout" runat="server" Direction="Vertical" Height="100%" Width="100%">
					<TS:TabPanel ID="LicenseTabPanel" runat="server" Width="100%" Height="100%" Collapsible="false">
						<Tabs>
							<TS:Tab ID="PaidLicenseTab" runat="server" Caption="@Terrasoft.WebApp.Loader,LicManager.PaidLicense.Caption">
								<Body>
									<TS:TreeGrid ID="PaidLicensesGrid" runat="server" Width="100%" Height="100%" IsSummaryVisible="false"
										HideHeaders="false" IsColumnAutowidth="true" DataSourceId="PaidLicensesDataSource">
									</TS:TreeGrid>
								</Body>
							</TS:Tab>
							<TS:Tab ID="UsedLincensesTab" runat="server" Caption="@Terrasoft.WebApp.Loader,LicManager.UsedLincense.Caption">
								<Body>
									<TS:ControlLayout ID="SearchPanel" runat="server" Width="100%" IsCollapsible="false" Collapsed="false" FitHeightByContent="true"
										DisplayStyle="Controls" VerticalAlign="Middle" Direction="Vertical" Margins="5 5 5 5">
										<TS:ControlLayout ID="SearchNamePanel" runat="server" Width="100%" IsCollapsible="false" Collapsed="false" FitHeightByContent="true"
											DisplayStyle="Controls" VerticalAlign="Middle" Direction="Horizontal">
											<TS:TextEdit runat="server" ID="SearchValue" Width="100%" EmptyText="@Terrasoft.WebApp.Loader,LicManager.SearchEmptyText.Caption">
												<Tools>
													<TS:ToolButton ID="ToolButtonClearSearch" runat="server"
														Image="{ResourceManagerName:Terrasoft.UI.WebControls,ResourceItemName:toolbutton-close.gif,Source:ResourceManager}">
														<AjaxEvents>
															<Click OnClientEvent="onClearSearchValue(e)" />
														</AjaxEvents>
													</TS:ToolButton>
												</Tools>
												<AjaxEvents>
													<SpecialKey OnClientEvent="onSearchValueSpecialKey(el, e);" />
												</AjaxEvents>
											</TS:TextEdit>
											<TS:Button runat="server" ID="SearchButton" Caption="@Terrasoft.WebApp.Loader,LicManager.SearchButton.Caption">
												<AjaxEvents>
													<Click OnClientEvent="onSearchButtonClientClick(e)" />
												</AjaxEvents>
											</TS:Button>
										</TS:ControlLayout>
										<TS:ComboBoxEdit ID="ProductComboBox" runat="server" AllowEmpty="true" Width="500"
											Visible="true" Required="false" EmptyText="@Terrasoft.WebApp.Loader,LicManager.ProductComboBox.Caption" ListPrepared="true">
											<AjaxEvents>
												<Change OnEvent="OnLicenseFilterChanged" />
											</AjaxEvents>
										</TS:ComboBoxEdit>
										<TS:CheckBox ID="IsLicensedUsersCheckBox" runat="server" Checked="false" Caption="@Terrasoft.WebApp.Loader,LicManager.IsLicensedUsersCheckBox.Caption"
											Width="100%" Enabled="true" CaptionPosition="Right">
											<AjaxEvents>
												<Check OnEvent="OnLicenseFilterChanged" />
											</AjaxEvents>
										</TS:CheckBox>
									</TS:ControlLayout>
									<TS:TreeGrid ID="UsedLincensesGrid" runat="server" Width="100%" Height="100%" IsSummaryVisible="false"
										ImageList="Terrasoft.WebApp" FooterVisible="false"
										HideHeaders="false" IsColumnAutowidth="true" DataSourceId="UsedLincensesDataSource">
									</TS:TreeGrid>
									<TS:ControlLayout ID="ControlSelectedUsers" runat="server" HorizontalAlign="Right" Padding="5" Width="100%" FitHeightByContent="true">
										<TS:Label runat="server" ID="SelectedUsersLabel" StyleSpec="margin-bottom:5px;" Bold="True" Width="100%" />
									</TS:ControlLayout>
								</Body>
							</TS:Tab>
						</Tabs>
					</TS:TabPanel>
				</TS:ControlLayout>
			</TS:ControlLayout>
			<TS:ControlLayout ID="BottomControlLayout" runat="server" Width="100%" DisplayStyle="Footer">
				<TS:Button runat="server" ID="ContextHelpButton" ImageList="Terrasoft.WebApp" HelpContextId="260"
					ImageAsSprite="true" Hidden="true"
					Image="{Source:ResourceManager,ResourceManagerName:Terrasoft.WebApp.Loader,ResourceItemName:help.png}">
					<AjaxEvents>
						<Click OnClientEvent="Terrasoft.HelpContext.showHelp(null, 'ContextHelpButton')" />
					</AjaxEvents>
				</TS:Button>
				<TS:Spacer ID="Spacer2" runat="server" Size="100%">
				</TS:Spacer>
				<TS:Button ID="CloseButton" runat="server" Caption="@Terrasoft.WebApp.Loader,LicManager.Close.Caption">
					<AjaxEvents>
						<Click OnEvent="OnCloseButtonClick" />
					</AjaxEvents>
				</TS:Button>
			</TS:ControlLayout>
		</TS:ControlLayout>
	</form>
</body>
</html>
