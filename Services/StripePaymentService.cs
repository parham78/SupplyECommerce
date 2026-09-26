using Microsoft.Extensions.Options;
using Stripe;

public class StripePaymentService
{
    private readonly PaymentIntentService _paymentIntentService;

    public StripePaymentService(
        IOptions<StripeOptions> options)
    {
        var stripeClient =
            new StripeClient(options.Value.SecretKey);

        _paymentIntentService =
            new PaymentIntentService(stripeClient);
    }

    public async Task<PaymentIntent>
        CreatePaymentIntentAsync(
            Order order,
            CancellationToken cancellationToken = default)
    {
        var amountInCents =
            checked((long)(order.TotalPrice * 100m));

        var options =
            new PaymentIntentCreateOptions
            {
                Amount = amountInCents,

                Currency = "cad",

                AutomaticPaymentMethods =
                    new PaymentIntentAutomaticPaymentMethodsOptions
                    {
                        Enabled = true
                    },

                Metadata =
                    new Dictionary<string, string>
                    {
                        ["orderId"] =
                            order.Id.ToString()
                    }
            };

        var requestOptions =
            new RequestOptions
            {
                IdempotencyKey =
                    $"supply-order-{order.Id}"
            };

        return await _paymentIntentService
            .CreateAsync(
                options,
                requestOptions,
                cancellationToken);
    }
}