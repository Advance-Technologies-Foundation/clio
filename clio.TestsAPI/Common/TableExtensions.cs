namespace clio.ApiTest.Common;

public static class TableExtensions
{

	#region Methods: Public

	public static Dictionary<string, string> ToDictionary(Table table) =>
		table.Rows.ToDictionary(row => row[0], row => row[1]);

	#endregion

}