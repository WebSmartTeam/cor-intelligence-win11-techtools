namespace CORCleanup.Tests;

public class SmokeTests
{
    [Fact]
    public void PingResult_IsSuccess_ReturnsTrueForSuccessStatus()
    {
        var result = new Core.Models.PingResult
        {
            Timestamp = DateTime.UtcNow,
            Status = System.Net.NetworkInformation.IPStatus.Success,
            RoundtripMs = 12,
            Ttl = 128,
            Target = "8.8.8.8"
        };

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void PingResult_IsSuccess_ReturnsFalseForTimedOut()
    {
        var result = new Core.Models.PingResult
        {
            Timestamp = DateTime.UtcNow,
            Status = System.Net.NetworkInformation.IPStatus.TimedOut,
            RoundtripMs = 0,
            Ttl = 0,
            Target = "192.168.1.254"
        };

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void WindowsEdition_HasExpectedValues()
    {
        Assert.Equal(0, (int)Core.Models.WindowsEdition.Unknown);
        Assert.Equal(1, (int)Core.Models.WindowsEdition.Home);
        Assert.Equal(2, (int)Core.Models.WindowsEdition.Pro);
    }
}
