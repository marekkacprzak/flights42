using System.Globalization;

namespace AiCsServer.Tools;

public static class FormatDate
{
    public static string ToDateOnly(string iso)
    {
        return iso.Length >= 10 ? iso[..10] : iso;
    }

    public static string FormatFlightDate(string iso)
    {
        if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime date))
        {
            return iso;
        }

        return date.ToUniversalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.GetCultureInfo("en-GB"));
    }
}
