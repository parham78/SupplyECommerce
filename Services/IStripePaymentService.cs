using Stripe;

public interface IStripePaymentService
{
    Task<PaymentIntent> CreatePaymentIntentAsync(
        Order order,
        CancellationToken cancellationToken = default);

    Task<PaymentIntent> GetOrCreatePaymentIntentAsync(
        Order order,
        CancellationToken cancellationToken = default);
}
