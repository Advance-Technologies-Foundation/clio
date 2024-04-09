Feature: SysSetting
Clio can perform INSERT, READ, UPDATE operations on Creatio System settings.
***Further read***: **[SysSetting Documentation](https://github.com/Advance-Technologies-Foundation/clio?tab=readme-ov-file#system-settings)**

	@SysSetting @UPSERT
	Scenario Outline: User can UPSERT sys-setting and read its value
		When command is run with "set-syssetting" "<sysSettingName> <sysSettingValue> <valueNameType>"
		Then SysSetting exists in Creatio with "<sysSettingName>" "<expectedValueNameType>"
		When command is run with "set-syssetting" "<sysSettingName> --GET"
		Then the output should be [INF] - SysSetting "<sysSettingName>" : "<expectedValue>"

		Examples:
		| valueNameType | sysSettingName   | sysSettingValue      | expectedValue        | expectedValueNameType |
		| Text          | ClioText         | ClioTextValue        | ClioTextValue        | Text                  |
		| ShortText     | ClioShortText    | ClioShortTextValue   | ClioShortTextValue   | ShortText             |
		| MediumText    | ClioMediumText   | ClioMediumTextValue  | ClioMediumTextValue  | MediumText            |
		| LongText      | ClioLongText     | ClioLongTextValue    | ClioLongTextValue    | LongText              |
		| SecureText    | ClioSecureText   | ClioSecureTextValue  | ClioSecureTextValue  | SecureText            |
		| MaxSizeText   | ClioMaxSizeText  | ClioMaxSizeTextValue | ClioMaxSizeTextValue | MaxSizeText           |
		| Boolean       | ClioBooleanOne   | True                 | True                 | Boolean               |
		| Boolean       | ClioBooleanTwo   | true                 | True                 | Boolean               |
		| Boolean       | ClioBooleanThree | False                | False                | Boolean               |
		| Boolean       | ClioBooleanFour  | false                | False                | Boolean               |
		| DateTime      | ClioDateTime     | "21-Jan-2024 18:00"  | 21-Jan-2024 18:00    | DateTime              |
		| Date          | ClioDate         | 21-Jan-2024          | 21-Jan-2024          | Date                  |
		| Time          | ClioTime         | 18:00                | 18:00                | Time                  |
		| Integer       | ClioInteger      | 10                   | 10                   | Integer               |
		| Currency      | ClioCurrency     | 11.50                | 11.50                | Money                 |
		| Decimal       | ClioDecimal      | 12.50                | 12.50                | Float                 |