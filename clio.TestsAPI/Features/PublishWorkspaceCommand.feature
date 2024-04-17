@PublishWorkspaceCommand
Feature: PublishWorkspaceCommand
**Docs missing for PublishWorkspaceCommand**

	@PublishWorkspaceCommand
	Scenario: User can specify temp dir
		Given I set env variable CLIO_WORKING_DIRECTORY to "C:\TempDir"
		When I execute publish-app with args:
		| arg | value                  |
		| -h  | C:\AppHub2             |
		| -r  | C:\MyWorkspace         |
		| -v  | 0.0.2                  |
		| -a  | MrktHootsuiteConnector |

		Then I assert that clio uses CLIO_WORKING_DIRECTORY directory

	@PublishWorkspaceCommand
	Scenario: User can omit specifing temp dir
		Given I do not set env variable CLIO_WORKING_DIRECTORY
		When I execute publish-app with args:
		| arg | value                  |
		| -h  | C:\AppHub2             |
		| -r  | C:\MyWorkspace         |
		| -v  | 0.0.2                  |
		| -a  | MrktHootsuiteConnector |

		Then I assert that clio uses default directory