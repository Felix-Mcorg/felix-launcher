using Felix.Core.Util;

namespace Felix.Core.Tests;

public class ProgressTests
{
    [Theory]
    [InlineData(5, "5 s")]
    [InlineData(65, "1 min 05 s")]
    [InlineData(3900, "1 h 05 min")]
    public void FormatDuration_formats_readable_times(double seconds, string expected) =>
        Assert.Equal(expected, ByteSize.FormatDuration(seconds));

    [Theory]
    [InlineData(null)]
    [InlineData(90000.0)] // plus d'un jour : estimation absurde, on n'affiche rien
    public void FormatDuration_hides_absurd_or_missing_estimates(double? seconds) =>
        Assert.Equal("…", ByteSize.FormatDuration(seconds));

    [Fact]
    public void SpeedMeter_reports_zero_until_a_measurable_interval_has_passed()
    {
        var meter = new SpeedMeter();
        Assert.Equal(0, meter.Update(1_000_000));
    }
}
