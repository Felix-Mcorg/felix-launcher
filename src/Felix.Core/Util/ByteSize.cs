namespace Felix.Core.Util;

public static class ByteSize
{
    /// <param name="provider">Culture d'affichage ; par défaut celle du joueur (« 1,5 Ko » en français).</param>
    public static string Format(double bytes, IFormatProvider? provider = null)
    {
        foreach (var unit in new[] { "o", "Ko", "Mo", "Go" })
        {
            if (bytes < 1024 || unit == "Go")
                return unit == "o" ? $"{bytes.ToString("0", provider)} {unit}" : $"{bytes.ToString("0.0", provider)} {unit}";
            bytes /= 1024;
        }
        return "";
    }

    public static string FormatDuration(double? seconds)
    {
        if (seconds is null)
            return "…";
        var s = (int)seconds.Value;
        return s < 60 ? $"{s} s" : $"{s / 60} min {s % 60:00} s";
    }
}
