using Microsoft.EntityFrameworkCore;

public class CheckoutService : ICheckoutService
{
    private readonly OrderManagementDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly StripePaymentService _stripePaymentService;

    public CheckoutService(
        OrderManagementDbContext context,
        ICurrentUserService currentUserService,
        StripePaymentService stripePaymentService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _stripePaymentService = stripePaymentService;
    }

    public async Task<CheckoutResponseDto> Checkout(
        CheckoutRequestDto dto)
    {
        var customerId =
            await _currentUserService.GetCustomerId();

        if (customerId == null)
        {
            throw new CustomerNotFoundException(
                "No customer profile is linked to the current user.");
        }

        var customer =
            await _context.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(c =>
                    c.Id == customerId.Value);

        if (customer == null)
        {
            throw new CustomerNotFoundException(
                "Customer was not found.");
        }

        if (!customer.IsActive)
        {
            throw new BadRequestException(
                "Inactive customers cannot checkout.");
        }

        // Find the customer's most recent successful payment.
        //
        // Any old abandoned Pending/Failed checkout that happened
        // BEFORE this successful payment must not be resumed.
        var lastSuccessfulPaymentDate =
            await _context.Orders
                .Where(o =>
                    o.CustomerId == customerId.Value &&
                    o.PaymentStatus == PaymentStatus.Succeeded)
                .MaxAsync(o =>
                    (DateTime?)o.PaidAt);

        // -------------------------------------------------
        // DUPLICATE CHECKOUT PROTECTION
        // -------------------------------------------------
        //
        // Resume an unfinished checkout only when:
        //
        // 1. it belongs to this customer
        // 2. the business order is still Pending
        // 3. payment is Pending or Failed
        // 4. it was created AFTER the customer's most recent
        //    successful payment
        //
        // This prevents old abandoned orders from being reused.
        var existingOrder =
            await _context.Orders
                .Include(o => o.OrderItems)
                .Where(o =>
                    o.CustomerId == customerId.Value &&
                    o.Status == OrderStatus.Pending &&
                    (
                        o.PaymentStatus == PaymentStatus.Pending ||
                        o.PaymentStatus == PaymentStatus.Failed
                    ) &&
                    (
                        lastSuccessfulPaymentDate == null ||
                        o.CreatedAt > lastSuccessfulPaymentDate
                    ))
                .OrderByDescending(o =>
                    o.CreatedAt)
                .FirstOrDefaultAsync();

        if (existingOrder != null)
        {
            var existingPaymentIntent =
                await _stripePaymentService
                    .GetOrCreatePaymentIntentAsync(
                        existingOrder);

            if (
                existingOrder.StripePaymentIntentId !=
                existingPaymentIntent.Id)
            {
                existingOrder.StripePaymentIntentId =
                    existingPaymentIntent.Id;

                await _context.SaveChangesAsync();
            }

            if (string.IsNullOrWhiteSpace(
                    existingPaymentIntent.ClientSecret))
            {
                throw new BadRequestException(
                    "Stripe did not return a client secret for the existing checkout.");
            }

            return new CheckoutResponseDto
            {
                Order =
                    MapOrderResponse(
                        existingOrder,
                        customer.Name),

                ClientSecret =
                    existingPaymentIntent.ClientSecret
            };
        }

        // -------------------------------------------------
        // NO ACTIVE CHECKOUT
        // -------------------------------------------------

        var address =
            await _context.Addresses
                .AsNoTracking()
                .FirstOrDefaultAsync(a =>
                    a.Id == dto.AddressId &&
                    a.CustomerId == customerId.Value);

        if (address == null)
        {
            throw new AddressNotFoundException(
                $"Address {dto.AddressId} was not found.");
        }

        var basket =
            await _context.Baskets
                .Include(b => b.Items)
                    .ThenInclude(bi =>
                        bi.Product)
                .FirstOrDefaultAsync(b =>
                    b.CustomerId == customerId.Value);

        if (
            basket == null ||
            basket.Items.Count == 0)
        {
            throw new BadRequestException(
                "Your basket is empty.");
        }

        foreach (var basketItem in basket.Items)
        {
            var product =
                basketItem.Product;

            if (!product.IsActive)
            {
                throw new BadRequestException(
                    $"Product {product.Id} is no longer available.");
            }

            if (
                basketItem.Quantity >
                product.Stock)
            {
                throw new BadRequestException(
                    $"Only {product.Stock} units of product {product.Id} are currently available.");
            }
        }

        Order order;

        await using (
            var transaction =
                await _context.Database
                    .BeginTransactionAsync())
        {
            try
            {
                order =
                    new Order
                    {
                        CustomerId =
                            customerId.Value,

                        Status =
                            OrderStatus.Pending,

                        PaymentStatus =
                            PaymentStatus.Pending,

                        CreatedAt =
                            DateTime.UtcNow,

                        ShippingRecipientName =
                            address.RecipientName,

                        ShippingAddressLine1 =
                            address.AddressLine1,

                        ShippingAddressLine2 =
                            address.AddressLine2,

                        ShippingCity =
                            address.City,

                        ShippingProvince =
                            address.Province,

                        ShippingPostalCode =
                            address.PostalCode,

                        ShippingCountry =
                            address.Country,

                        ShippingPhoneNumber =
                            address.PhoneNumber
                    };

                decimal totalPrice = 0;

                foreach (
                    var basketItem
                    in basket.Items)
                {
                    var product =
                        basketItem.Product;

                    product.Stock -=
                        basketItem.Quantity;

                    var orderItem =
                        new OrderItem
                        {
                            ProductId =
                                product.Id,

                            Quantity =
                                basketItem.Quantity,

                            UnitPrice =
                                product.Price,

                            ProductName =
                                product.Name,

                            ProductSku =
                                product.Sku
                        };

                    order.OrderItems.Add(
                        orderItem);

                    totalPrice +=
                        basketItem.Quantity *
                        product.Price;
                }

                order.TotalPrice =
                    totalPrice;

                _context.Orders.Add(
                    order);

                await _context
                    .SaveChangesAsync();

                await transaction
                    .CommitAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction
                    .RollbackAsync();

                throw new ConcurrencyConflictException(
                    "Checkout failed because product stock changed. Please review your basket and try again.");
            }
            catch
            {
                await transaction
                    .RollbackAsync();

                throw;
            }
        }

        // The SQL transaction is already finished.
        // Stripe is an external network call, so we do not
        // hold the database transaction open while calling it.
        var paymentIntent =
            await _stripePaymentService
                .GetOrCreatePaymentIntentAsync(
                    order);

        order.StripePaymentIntentId =
            paymentIntent.Id;

        await _context.SaveChangesAsync();

        if (string.IsNullOrWhiteSpace(
                paymentIntent.ClientSecret))
        {
            throw new BadRequestException(
                "Stripe did not return a client secret.");
        }

        return new CheckoutResponseDto
        {
            Order =
                MapOrderResponse(
                    order,
                    customer.Name),

            ClientSecret =
                paymentIntent.ClientSecret
        };
    }

    private static OrderResponseDto MapOrderResponse(
        Order order,
        string customerName)
    {
        return new OrderResponseDto
        {
            Id =
                order.Id,

            CustomerId =
                order.CustomerId,

            CustomerName =
                customerName,

            TotalPrice =
                order.TotalPrice,

            Status =
                order.Status,

            CreatedAt =
                order.CreatedAt,

            ShippingRecipientName =
                order.ShippingRecipientName,

            ShippingAddressLine1 =
                order.ShippingAddressLine1,

            ShippingAddressLine2 =
                order.ShippingAddressLine2,

            ShippingCity =
                order.ShippingCity,

            ShippingProvince =
                order.ShippingProvince,

            ShippingPostalCode =
                order.ShippingPostalCode,

            ShippingCountry =
                order.ShippingCountry,

            ShippingPhoneNumber =
                order.ShippingPhoneNumber,

            Items =
                order.OrderItems
                    .Select(oi =>
                        new OrderItemResponseDto
                        {
                            Id =
                                oi.Id,

                            ProductId =
                                oi.ProductId,

                            ProductName =
                                oi.ProductName,

                            ProductSku =
                                oi.ProductSku,

                            Quantity =
                                oi.Quantity,

                            UnitPrice =
                                oi.UnitPrice,

                            LineTotal =
                                oi.Quantity *
                                oi.UnitPrice
                        })
                    .ToList()
        };
    }
}