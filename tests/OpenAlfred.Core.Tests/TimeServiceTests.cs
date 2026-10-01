using OpenAlfred.Core.Time;
using Xunit;

namespace OpenAlfred.Core.Tests;

public class TimeServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Empty_Argument_Returns_Local_Time()
    {
        var info = TimeService.Resolve("", FixedNow);
        Assert.Equal(TimeStatus.Ok, info.Status);
        Assert.Equal(FixedNow, info.Now);
    }

    [Fact]
    public void Resolves_City_In_Chinese()
    {
        var info = TimeService.Resolve("东京", FixedNow);
        Assert.Equal(TimeStatus.Ok, info.Status);
        Assert.Equal(TimeSpan.FromHours(9), info.Now.Offset);
        Assert.Equal(13, info.Now.Hour);
    }

    [Fact]
    public void Resolves_Utc_Offset()
    {
        var info = TimeService.Resolve("UTC-5", FixedNow);
        Assert.Equal(TimeStatus.Ok, info.Status);
        Assert.Equal(TimeSpan.FromHours(-5), info.Now.Offset);
    }

    [Fact]
    public void Relative_Time_Adds_Days()
    {
        var info = TimeService.Resolve("+3d", FixedNow);
        Assert.Equal(TimeStatus.Ok, info.Status);
        Assert.Equal(FixedNow.AddDays(3), info.Now);
    }

    [Fact]
    public void Unknown_Zone_Reports_Error()
    {
        var info = TimeService.Resolve("Atlantis", FixedNow);
        Assert.Equal(TimeStatus.UnknownZone, info.Status);
        Assert.NotNull(info.Error);
    }

    [Fact]
    public void Weekday_Names_In_Chinese()
    {
        Assert.Equal("星期四", TimeService.WeekdayName(DayOfWeek.Thursday));
        Assert.Equal("星期日", TimeService.WeekdayName(DayOfWeek.Sunday));
    }
}
