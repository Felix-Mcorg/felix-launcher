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
        // Au-delà d'un jour, l'estimation n'a aucun sens : on n'affiche rien plutôt qu'un chiffre absurde.
        if (seconds is null || double.IsNaN(seconds.Value) || seconds.Value > 86400)
            return "…";
        var s = (int)seconds.Value;
        if (s < 60)
            return $"{s} s";
        if (s < 3600)
            return $"{s / 60} min {s % 60:00} s";
        return $"{s / 3600} h {s % 3600 / 60:00} min";
    }
}
