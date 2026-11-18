using Cccc.Web.Features.Data;

namespace Cccc.Web.Tests;

/// <summary>Hours as a person types them, and minutes alone as minutes.</summary>
public class HoursTextTests
{
    [Theory]
    [InlineData("6", 360)]
    [InlineData("6.5", 390)]
    [InlineData("6:30", 390)]
    [InlineData("6h 30m", 390)]
    [InlineData("2h", 120)]
    [InlineData("0:30", 30)]
    [InlineData("30m", 30)]
    [InlineData("45 min", 45)]
    [InlineData("90M", 90)]
    public void HoursAndMinutesAreReadAsTheyAreMeant(string typed, int minutes)
    {
        Assert.True(HoursText.TryRead(typed, out var value));
        Assert.Equal(TimeSpan.FromMinutes(minutes), value);
    }

    [Theory]
    [InlineData("m")]
    [InlineData("-5m")]
    [InlineData("1.5m")]
    [InlineData("ten")]
    [InlineData("6:75")]
    public void WhatIsNotATimeIsRefused(string typed) => Assert.False(HoursText.TryRead(typed, out _));

    [Fact]
    public void NothingTypedIsNone()
    {
        Assert.True(HoursText.TryRead("  ", out var value));
        Assert.Null(value);
    }
}
