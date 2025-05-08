namespace Clio.Common;

public interface IConsoleProgressbar
{

    #region Properties: Public

    int MaxActionNameLength { get; set; }

    int Scale { get; set; }

    #endregion

    #region Methods: Public

    string GetBuatifyProgress(string actionName, int value);

    string GetBuatifyProgress(string actionName, int value, int total);

    #endregion

}

public class ConsoleProgressbar : IConsoleProgressbar
{

    #region Properties: Public

    public int MaxActionNameLength { get; set; }

    public int Scale { get; set; } = 10;

    #endregion

    #region Methods: Private

    private string GetProgressResult(int value)
    {
        int starsCount = value / (100 / Scale);
        int dotsCount = Scale - starsCount;
        return new string('*', starsCount) + new string('.', dotsCount);
    }

    #endregion

    #region Methods: Public

    public string GetBuatifyProgress(string actionName, int value)
    {
        string valueString = GetProgressResult(value);

        string padRight = string.Empty;
        if (MaxActionNameLength != 0)
        {
            padRight = new string(' ', MaxActionNameLength - actionName.Length);
        }
        string result = $"{actionName + padRight} [{valueString}] {value}%";

        return result;
    }

    public string GetBuatifyProgress(string actionName, int value, int total)
    {
        int percentValue = value * 100 / total;
        return GetBuatifyProgress(actionName, percentValue);
    }

    #endregion

}
