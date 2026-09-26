public interface ICheckoutService
{
    Task<CheckoutResponseDto> Checkout(
        CheckoutRequestDto dto);
}