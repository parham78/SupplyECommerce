using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Serialization;

public class CheckoutApiTests
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions =
    new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public CheckoutApiTests(
        CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Checkout_WithValidBasket_CreatesOrderReducesStockAndKeepsBasket()
    {
        // Arrange
        var (token, email) =
            await RegisterAndLoginCustomerAsync();

        using var authenticatedClient =
            CreateAuthenticatedClient(token);

        int productId = 0;

        try
        {
            var address =
                await CreateAddressAsync(
                    authenticatedClient);

            productId =
                await SeedProductAsync(
                    stock: 10,
                    price: 25m);

            var addResponse =
                await authenticatedClient.PostAsJsonAsync(
                    "/api/me/basket/items",
                    new AddBasketItemRequestDto
                    {
                        ProductId = productId,
                        Quantity = 3
                    });

            Assert.Equal(
                HttpStatusCode.OK,
                addResponse.StatusCode);

            // Act
            var checkoutResponse =
                await authenticatedClient.PostAsJsonAsync(
                    "/api/me/checkout",
                    new CheckoutRequestDto
                    {
                        AddressId = address.Id
                    });

            // Assert HTTP response
            Assert.Equal(
                HttpStatusCode.OK,
                checkoutResponse.StatusCode);

            var checkout =
                await checkoutResponse.Content
                    .ReadFromJsonAsync<CheckoutResponseDto>(
                        JsonOptions);

            Assert.NotNull(checkout);
            Assert.False(string.IsNullOrWhiteSpace(checkout.ClientSecret));

            var orderResponse = checkout.Order;

            Assert.NotNull(orderResponse);

            Assert.Equal(
                75m,
                orderResponse.TotalPrice);

            Assert.Equal(
                OrderStatus.Pending,
                orderResponse.Status);

            Assert.Equal(
                "Integration Customer",
                orderResponse.CustomerName);

            Assert.Equal(
                "123 Integration Street",
                orderResponse.ShippingAddressLine1);

            // Verify DATABASE state
            using var scope =
                _factory.Services.CreateScope();

            var context =
                scope.ServiceProvider
                    .GetRequiredService<
                        OrderManagementDbContext>();

            var order =
                await context.Orders
                    .Include(o => o.OrderItems)
                    .SingleAsync(
                        o => o.Id == orderResponse.Id);

            Assert.Equal(
                75m,
                order.TotalPrice);

            Assert.Equal(
                OrderStatus.Pending,
                order.Status);

            Assert.Equal(
                PaymentStatus.Pending,
                order.PaymentStatus);

            Assert.Null(order.PaidAt);
            Assert.False(string.IsNullOrWhiteSpace(order.StripePaymentIntentId));

            Assert.Equal(
                "123 Integration Street",
                order.ShippingAddressLine1);

            var orderItem =
                Assert.Single(
                    order.OrderItems);

            Assert.Equal(
                productId,
                orderItem.ProductId);

            Assert.Equal(
                3,
                orderItem.Quantity);

            Assert.Equal(
                25m,
                orderItem.UnitPrice);

            Assert.Equal(
                "Checkout Integration Product",
                orderItem.ProductName);

            var product =
                await context.Products
                    .SingleAsync(
                        p => p.Id == productId);

            Assert.Equal(
                7,
                product.Stock);

            var basket =
                await context.Baskets
                    .Include(b => b.Items)
                    .SingleAsync(
                        b => b.CustomerId ==
                             order.CustomerId);

            // Checkout reserves stock; the successful-payment webhook clears items.
            var basketItem =
                Assert.Single(
                    basket.Items);

            Assert.Equal(
                productId,
                basketItem.ProductId);

            Assert.Equal(
                3,
                basketItem.Quantity);
        }
        finally
        {
            await CleanupScenarioAsync(
                email,
                productId);
        }
    }

    [Fact]
    public async Task Checkout_WithEmptyBasket_ReturnsBadRequest()
    {
        // Arrange
        var (token, email) =
            await RegisterAndLoginCustomerAsync();

        using var authenticatedClient =
            CreateAuthenticatedClient(token);

        try
        {
            var address =
                await CreateAddressAsync(
                    authenticatedClient);

            // Act
            var response =
                await authenticatedClient.PostAsJsonAsync(
                    "/api/me/checkout",
                    new CheckoutRequestDto
                    {
                        AddressId = address.Id
                    });

            // Assert
            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);

            var problem =
                await response.Content
                    .ReadFromJsonAsync<ProblemDetails>();

            Assert.NotNull(problem);

            Assert.Equal(
                "Your basket is empty.",
                problem.Detail);
        }
        finally
        {
            await CleanupScenarioAsync(
                email);
        }
    }

    [Fact]
    public async Task Checkout_WithAnotherCustomersAddress_ReturnsNotFound()
    {
        // Arrange
        var (customerAToken, customerAEmail) =
            await RegisterAndLoginCustomerAsync();

        var (customerBToken, customerBEmail) =
            await RegisterAndLoginCustomerAsync();

        using var customerAClient =
            CreateAuthenticatedClient(
                customerAToken);

        using var customerBClient =
            CreateAuthenticatedClient(
                customerBToken);

        try
        {
            var customerBAddress =
                await CreateAddressAsync(
                    customerBClient);

            // Customer A tries to use Customer B's address.
            var response =
                await customerAClient.PostAsJsonAsync(
                    "/api/me/checkout",
                    new CheckoutRequestDto
                    {
                        AddressId =
                            customerBAddress.Id
                    });

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);
        }
        finally
        {
            await CleanupScenarioAsync(
                customerAEmail);

            await CleanupScenarioAsync(
                customerBEmail);
        }
    }

    [Fact]
    public async Task Checkout_WhenStockChangedAfterAddingToBasket_ReturnsBadRequest()
    {
        // Arrange
        var (token, email) =
            await RegisterAndLoginCustomerAsync();

        using var authenticatedClient =
            CreateAuthenticatedClient(token);

        int productId = 0;

        try
        {
            var address =
                await CreateAddressAsync(
                    authenticatedClient);

            productId =
                await SeedProductAsync(
                    stock: 10,
                    price: 20m);

            var addResponse =
                await authenticatedClient.PostAsJsonAsync(
                    "/api/me/basket/items",
                    new AddBasketItemRequestDto
                    {
                        ProductId = productId,
                        Quantity = 8
                    });

            Assert.Equal(
                HttpStatusCode.OK,
                addResponse.StatusCode);

            // Simulate inventory changing AFTER
            // the customer added the item.
            using (var scope =
                _factory.Services.CreateScope())
            {
                var context =
                    scope.ServiceProvider
                        .GetRequiredService<
                            OrderManagementDbContext>();

                var product =
                    await context.Products
                        .SingleAsync(
                            p => p.Id == productId);

                product.Stock = 2;

                await context.SaveChangesAsync();
            }

            // Act
            var response =
                await authenticatedClient.PostAsJsonAsync(
                    "/api/me/checkout",
                    new CheckoutRequestDto
                    {
                        AddressId = address.Id
                    });

            // Assert
            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);

            var problem =
                await response.Content
                    .ReadFromJsonAsync<ProblemDetails>();

            Assert.NotNull(problem);

            Assert.Contains(
                "Only 2 units",
                problem.Detail);

            // Verify checkout did NOT remove basket item
            // or create an order.
            using var verificationScope =
                _factory.Services.CreateScope();

            var verificationContext =
                verificationScope.ServiceProvider
                    .GetRequiredService<
                        OrderManagementDbContext>();

            var customer =
                await verificationContext.Customers
                    .SingleAsync(
                        c => c.Email == email);

            var ordersExist =
                await verificationContext.Orders
                    .AnyAsync(
                        o => o.CustomerId ==
                             customer.Id);

            Assert.False(
                ordersExist);

            var basket =
                await verificationContext.Baskets
                    .Include(b => b.Items)
                    .SingleAsync(
                        b => b.CustomerId ==
                             customer.Id);

            var basketItem =
                Assert.Single(
                    basket.Items);

            Assert.Equal(
                8,
                basketItem.Quantity);

            var productAfterFailure =
                await verificationContext.Products
                    .SingleAsync(
                        p => p.Id == productId);

            Assert.Equal(
                2,
                productAfterFailure.Stock);
        }
        finally
        {
            await CleanupScenarioAsync(
                email,
                productId);
        }
    }

    private async Task<
        (string Token, string Email)>
        RegisterAndLoginCustomerAsync()
    {
        var email =
            $"checkout-{Guid.NewGuid():N}@test.local";

        const string password =
            "Customer123!";

        var registerResponse =
            await _client.PostAsJsonAsync(
                "/api/auth/register",
                new RegisterRequestDto
                {
                    Name =
                        "Integration Customer",

                    Email =
                        email,

                    Password =
                        password
                });

        Assert.Equal(
            HttpStatusCode.OK,
            registerResponse.StatusCode);

        var loginResponse =
            await _client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequestDto
                {
                    Email = email,
                    Password = password
                });

        Assert.Equal(
            HttpStatusCode.OK,
            loginResponse.StatusCode);

        var login =
            await loginResponse.Content
                .ReadFromJsonAsync<
                    LoginResponseDto>();

        Assert.NotNull(login);

        Assert.False(
            string.IsNullOrWhiteSpace(
                login.Token));

        return (
            login.Token,
            email);
    }

    private HttpClient CreateAuthenticatedClient(
        string token)
    {
        var client =
            _factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                token);

        return client;
    }

    private async Task<AddressResponseDto>
        CreateAddressAsync(
            HttpClient client)
    {
        var response =
            await client.PostAsJsonAsync(
                "/api/me/addresses",
                new CreateAddressRequestDto
                {
                    Label = "Home",
                    RecipientName =
                        "Integration Customer",

                    AddressLine1 =
                        "123 Integration Street",

                    City = "Montreal",
                    Province = "Quebec",
                    PostalCode = "H1H 1H1",
                    Country = "Canada",
                    PhoneNumber = "5145551234",
                    IsDefault = true
                });

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        var address =
            await response.Content
                .ReadFromJsonAsync<
                    AddressResponseDto>();

        Assert.NotNull(address);

        return address;
    }

    private async Task<int> SeedProductAsync(
    int stock,
    decimal price)
    {
        using var scope =
            _factory.Services.CreateScope();

        var context =
            scope.ServiceProvider
                .GetRequiredService<
                    OrderManagementDbContext>();

        var categoryId =
            await context.Categories
                .Where(c => c.Slug == "workspace")
                .Select(c => c.Id)
                .SingleAsync();

        var product =
            new Product
            {
                Name = "Checkout Integration Product",

                Sku =
                    $"CHECKOUT-{Guid.NewGuid():N}",

                Price = price,
                Stock = stock,
                IsActive = true,
                CategoryId = categoryId
            };

        context.Products.Add(product);

        await context.SaveChangesAsync();

        return product.Id;
    }

    private async Task CleanupScenarioAsync(
        string email,
        int productId = 0)
    {
        using var scope =
            _factory.Services.CreateScope();

        var context =
            scope.ServiceProvider
                .GetRequiredService<
                    OrderManagementDbContext>();

        var userManager =
            scope.ServiceProvider
                .GetRequiredService<
                    UserManager<ApplicationUser>>();

        var customer =
            await context.Customers
                .FirstOrDefaultAsync(
                    c => c.Email == email);

        if (customer != null)
        {
            var orders =
                await context.Orders
                    .Include(o => o.OrderItems)
                    .Where(
                        o => o.CustomerId ==
                             customer.Id)
                    .ToListAsync();

            foreach (var order in orders)
            {
                context.OrderItems.RemoveRange(
                    order.OrderItems);
            }

            context.Orders.RemoveRange(
                orders);

            var basket =
                await context.Baskets
                    .Include(b => b.Items)
                    .FirstOrDefaultAsync(
                        b => b.CustomerId ==
                             customer.Id);

            if (basket != null)
            {
                context.BasketItems.RemoveRange(
                    basket.Items);

                context.Baskets.Remove(
                    basket);
            }

            var addresses =
                await context.Addresses
                    .Where(
                        a => a.CustomerId ==
                             customer.Id)
                    .ToListAsync();

            context.Addresses.RemoveRange(
                addresses);

            context.Customers.Remove(
                customer);

            await context.SaveChangesAsync();
        }

        if (productId > 0)
        {
            var product =
                await context.Products
                    .FindAsync(productId);

            if (product != null)
            {
                context.Products.Remove(
                    product);

                await context.SaveChangesAsync();
            }
        }

        var user =
            await userManager
                .FindByEmailAsync(email);

        if (user != null)
        {
            await userManager.DeleteAsync(
                user);
        }
    }
}
