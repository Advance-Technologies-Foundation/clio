Feature: SetWebServiceUrlCommand
	User can set webservice url in locked package

@SetWebServiceUrlCommand
Scenario: Service base url is updated
	When user executes command with clio set-webservice-url "<serviceName>" "<baseUrl>"
	Then service is updated
	Examples:
		|serviceName|baseUrl|
  		|CreatioMarketplaceApi|https://marketplace.creatio.com/api/|
  		|CreatioMarketplaceApi|https://google.ca|