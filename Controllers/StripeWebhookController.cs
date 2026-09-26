using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;

[ApiController]
[Route("api/webhooks/stripe")]
[AllowAnonymous]
public class StripeWebhookController : ControllerBase
{
    private readonly OrderManagementDbContext _context;
    private readonly StripeOptions _stripeOptions;

    public StripeWebhookController(
        OrderManagementDbContext context,
        IOptions<StripeOptions> stripeOptions)
    {
        _context = context;
        _stripeOptions = stripeOptions.Value;
    }

    [HttpPost]
    public async Task<IActionResult> Handle(
        CancellationToken cancellationToken)
    {
        var json =
            await new StreamReader(Request.Body)
                .ReadToEndAsync(cancellationToken);

        var signature =
            Request.Headers["Stripe-Signature"]
                .ToString();

        Event stripeEvent;

        try
        {
            stripeEvent =
                EventUtility.ConstructEvent(
                    json,
                    signature,
                    _stripeOptions.WebhookSecret);
        }
        catch (StripeException)
        {
            return BadRequest();
        }

        if (
            stripeEvent.Data.Object
            is not PaymentIntent paymentIntent)
        {
            return Ok();
        }

        var order =
            await _context.Orders
                .FirstOrDefaultAsync(
                    o =>
                        o.StripePaymentIntentId ==
                        paymentIntent.Id,
                    cancellationToken);

        if (order == null)
        {
            return Ok();
        }

        switch (stripeEvent.Type)
        {
            case "payment_intent.succeeded":
                await HandleSucceeded(
                    order,
                    cancellationToken);

                break;

            case "payment_intent.payment_failed":
                HandleFailed(order);
                break;

            case "payment_intent.canceled":
                HandleCancelled(order);
                break;
        }

        await _context.SaveChangesAsync(
            cancellationToken);

        return Ok();
    }

    private async Task HandleSucceeded(
        Order order,
        CancellationToken cancellationToken)
    {
        if (
            order.PaymentStatus ==
            PaymentStatus.Succeeded)
        {
            return;
        }

        order.PaymentStatus =
            PaymentStatus.Succeeded;

        order.PaidAt =
            DateTime.UtcNow;

        var basket =
            await _context.Baskets
                .Include(b => b.Items)
                .FirstOrDefaultAsync(
                    b =>
                        b.CustomerId ==
                        order.CustomerId,
                    cancellationToken);

        if (basket != null)
        {
            _context.BasketItems.RemoveRange(
                basket.Items);
        }
    }

    private static void HandleFailed(
        Order order)
    {
        if (
            order.PaymentStatus ==
            PaymentStatus.Succeeded)
        {
            return;
        }

        order.PaymentStatus =
            PaymentStatus.Failed;
    }

    private static void HandleCancelled(
        Order order)
    {
        if (
            order.PaymentStatus ==
            PaymentStatus.Succeeded)
        {
            return;
        }

        order.PaymentStatus =
            PaymentStatus.Cancelled;
    }
}