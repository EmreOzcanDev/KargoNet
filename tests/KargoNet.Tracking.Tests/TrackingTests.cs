using KargoNet.Contracts.Models;

namespace KargoNet.Tracking.Tests;

public class TrackingTests
{
    [Fact]
    public void ShipmentStatus_ContainsExactlyTheSevenLifecycleValues()
    {
        var expected = new[]
        {
            nameof(ShipmentStatus.Created),
            nameof(ShipmentStatus.PickedUp),
            nameof(ShipmentStatus.InTransit),
            nameof(ShipmentStatus.AtHub),
            nameof(ShipmentStatus.OutForDelivery),
            nameof(ShipmentStatus.Delivered),
            nameof(ShipmentStatus.Exception),
        };

        var actual = Enum.GetValues<ShipmentStatus>()
            .Select(status => status.ToString())
            .ToArray();

        Assert.Equal(expected, actual);
    }
}
