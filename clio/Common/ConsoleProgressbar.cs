namespace Clio.Common
{

	public interface IConsoleProgressbar
{
	string GetBuatifyProgress(string actionName, int value);
	string GetBuatifyProgress(string actionName, int value, int total);

	int Scale { get; set; }
}

	public class ConsoleProgressbar : IConsoleProgressbar
	{
		public int Scale { get; set; } = 10;

		public ConsoleProgressbar() {
		}

		private string GetProgressResult(int value) {
			int starsCount = value / (100 / Scale);
			int dotsCount = Scale - starsCount;
			return new string('*', starsCount) + new string('.', dotsCount);
		}

		public string GetBuatifyProgress(string actionName, int value) {
			string valueString = GetProgressResult(value);
			string result = $"{actionName}: [{valueString}] {value}%";
			return result;
		}

		public string GetBuatifyProgress(string actionName, int value, int total) {
			int percentValue = value * 100 / total;
			return GetBuatifyProgress(actionName, percentValue);
		}
	}


}