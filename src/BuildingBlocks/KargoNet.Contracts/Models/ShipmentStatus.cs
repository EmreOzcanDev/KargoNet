namespace KargoNet.Contracts.Models;

public enum ShipmentStatus
{
    Created,
    PickedUp,
    InTransit,
    AtHub,
    OutForDelivery,
    Delivered,
    Exception,
}
