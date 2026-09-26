public class CheckoutResponseDto
{
    public OrderResponseDto Order { get; set; } = null!;

    public string ClientSecret { get; set; } = string.Empty;
}