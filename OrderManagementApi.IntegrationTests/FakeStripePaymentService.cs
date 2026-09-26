using System.Globalization;
using Stripe;

public sealed class FakeStripePaymentService : IStripePaymentService
{
    public Task<PaymentIntent> CreatePaymentIntentAsync(
        Order order,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var paymentIntentId =
            "pi_fake_order_" + order.Id.ToString(CultureInfo.InvariantCulture);

        return Task.FromResult(CreatePaymentIntent(order, paymentIntentId));
    }

    public Task<PaymentIntent> GetOrCreatePaymentIntentAsync(
        Order order,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(order.StripePaymentIntentId))
        {
            return Task.FromResult(
                CreatePaymentIntent(order, order.StripePaymentIntentId));
        }

        return CreatePaymentIntentAsync(order, cancellationToken);
    }

    private static PaymentIntent CreatePaymentIntent(
        Order order,
        string paymentIntentId)
    {
        // Plain data only: no Stripe client, credentials, or payment completion.
        // Stable IDs also model Stripe's per-order create idempotency key.
        return new PaymentIntent
        {
            Id = paymentIntentId,
            ClientSecret = paymentIntentId + "_secret_fake",
            Amount = checked((long)(order.TotalPrice * 100m)),
            Currency = "cad",
            Status = "requires_payment_method",
            Metadata = new Dictionary<string, string>
            {
                ["orderId"] = order.Id.ToString(CultureInfo.InvariantCulture)
            }
        };
    }
}
