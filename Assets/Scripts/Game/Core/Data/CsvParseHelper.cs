namespace Game.Core.Data
{
    // 파서 3종이 공유하는 잔손질. CsvHelper 예외 메시지는 여러 줄 안내문이라 첫 줄만 남긴다
    internal static class CsvParseHelper
    {
        internal static string FirstLine(string message)
        {
            int cut = message.IndexOf('\n');
            return cut < 0 ? message : message.Substring(0, cut).TrimEnd('\r');
        }
    }
}
