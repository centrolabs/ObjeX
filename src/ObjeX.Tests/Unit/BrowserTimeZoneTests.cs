using ObjeX.Web.Services;

namespace ObjeX.Tests.Unit;

public class BrowserTimeZoneTests
{
    static readonly DateTime Winter = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
    static readonly DateTime Summer = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Defaults_To_Utc()
    {
        var tz = new BrowserTimeZone();
        Assert.Equal(TimeZoneInfo.Utc, tz.Zone);
        Assert.Equal("2026-01-15 12:00", tz.Format(Winter));
    }

    [Fact]
    public void Zurich_Applies_Standard_And_Daylight_Offset()
    {
        var tz = new BrowserTimeZone();
        tz.Set("Europe/Zurich");

        Assert.Equal("2026-01-15 13:00", tz.Format(Winter));
        Assert.Equal("2026-07-15 14:00", tz.Format(Summer));
    }

    [Fact]
    public void Unspecified_Kind_Is_Read_As_Utc()
    {
        var tz = new BrowserTimeZone();
        tz.Set("Europe/Zurich");

        Assert.Equal("2026-01-15 13:00:00", tz.FormatSeconds(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void DateTimeOffset_Converts_Through_Utc()
    {
        var tz = new BrowserTimeZone();
        tz.Set("Europe/Zurich");

        Assert.Equal("2026-01-15 13:00", tz.Format(new DateTimeOffset(2026, 1, 15, 14, 0, 0, TimeSpan.FromHours(2))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not/AZone")]
    [InlineData("../../etc/passwd")]
    public void Unusable_Id_Falls_Back_To_Utc(string? id)
    {
        var tz = new BrowserTimeZone();
        tz.Set("Europe/Zurich");
        tz.Set(id);

        Assert.Equal(TimeZoneInfo.Utc, tz.Zone);
    }

    [Fact]
    public void Over_Long_Id_Falls_Back_To_Utc()
    {
        var tz = new BrowserTimeZone();
        tz.Set(new string('a', 65));

        Assert.Equal(TimeZoneInfo.Utc, tz.Zone);
    }

    [Fact]
    public void Null_Timestamp_Renders_A_Dash()
    {
        var tz = new BrowserTimeZone();

        Assert.Equal("—", tz.Format((DateTime?)null));
        Assert.Equal("—", tz.Format((DateTimeOffset?)null));
    }
}
