public class Order
{
    public int Id { get; set; }

    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public decimal TotalPrice { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DateTime CreatedAt { get; set; }

    public List<OrderItem> OrderItems { get; set; } = [];


    public string? ShippingRecipientName { get; set; }

    public string? ShippingAddressLine1 { get; set; }

    public string? ShippingAddressLine2 { get; set; }

    public string? ShippingCity { get; set; }

    public string? ShippingProvince { get; set; }

    public string? ShippingPostalCode { get; set; }

    public string? ShippingCountry { get; set; }

    public string? ShippingPhoneNumber { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    = PaymentStatus.NotTracked;

    public string? StripePaymentIntentId { get; set; }

    public DateTime? PaidAt { get; set; }
}