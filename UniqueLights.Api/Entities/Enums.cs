namespace UniqueLights.Api.Entities;

public enum OrderStatus
{
    Pending,
    Confirmed,
    Processing,
    Shipped,
    OutForDelivery,
    Delivered,
    Cancelled
}

public enum DeliveryMethod
{
    Standard,
    Express
}

public enum PaymentMethod
{
    CashOnDelivery,
    Card,
    Upi
}

public enum PaymentStatus
{
    Pending,
    Paid,
    Failed,
    Refunded
}

public enum DiscountType
{
    Percentage,
    FixedAmount
}
