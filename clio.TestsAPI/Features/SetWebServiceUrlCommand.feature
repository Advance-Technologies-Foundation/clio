Feature: SetWebServiceUrlCommand
User can set webservice url in a locked package
See [Set Base WebService Url](https://github.com/Advance-Technologies-Foundation/clio?tab=readme-ov-file#set-base-webservice-url)

@SetWebServiceUrlCommand
Scenario: Service base url is updated
	When user executes command with clio set-webservice-url "<serviceName>" "<baseUrl>"
	Then command clio get-webservice-url "<serviceName>" returns "<baseUrl>"
	
	Examples:
		|serviceName|baseUrl|
		|CreatioMarketplaceApi|https://google.ca|
		|CreatioMarketplaceApi|https://marketplace.creatio.com/api/|
