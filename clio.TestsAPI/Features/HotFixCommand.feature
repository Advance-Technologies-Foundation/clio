@HotFixCommand
Feature: HotFixCommand
See [Enable/Disable pkg hotfix mode](https://github.com/Advance-Technologies-Foundation/clio/tree/hot-fix-mode-refactor?tab=readme-ov-file#enabledisable-pkg-hotfix-mode)

	Scenario: User can enable hotfix mode
		When command is run with "<commandName>" "<packageName> <state>"
		Then package "<packageName>" has property "<propertyname>" with value "<expectedState>"

	Examples:
	| commandName | packageName | state | expectedState | propertyname |
	| hotfix      | Base        | true  | 1             | hotfixState  |

	Scenario: User can disable hotfix mode
		When command is run with "<commandName>" "<packageName> <state>"
		Then package "<packageName>" has property "<propertyname>" with value "<expectedState>"

	Examples:
	| commandName | packageName | state | expectedState | propertyname |
	| hotfix      | Base        | false | 2             | hotfixState  |