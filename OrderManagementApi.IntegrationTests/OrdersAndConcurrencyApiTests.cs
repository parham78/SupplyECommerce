using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public class OrdersAndConcurrencyApiTests
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

    public OrdersAndConcurrencyApiTests(
        CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ---------------------------------------------------------
    // 14. ORDER OWNERSHIP
    // ---------------------------------------------------------

    [Fact]
    public async Task GetMyOrder_CannotReadAnotherCustomersOrder()
    {
        var scenario =
            await CreateCheckedOutOrderAsync();

        var (customerBToken, customerBEmail) =
            await RegisterAndLoginCustomerAsync();

        using var customerAClient =
            CreateAuthenticatedClient(
                scenario.Token);

        using var customerBClient =
            CreateAuthenticatedClient(
                customerBToken);

        try
        {
            // Owner can read it.
            var ownerResponse =
                await customerAClient.GetAsync(
                    $"/api/me/orders/{scenario.OrderId}");

            Assert.Equal(
                HttpStatusCode.OK,
                ownerResponse.StatusCode);

            // Another customer cannot.
            var otherResponse =
                await customerBClient.GetAsync(
                    $"/api/me/orders/{scenario.OrderId}");

            Assert.Equal(
                HttpStatusCode.NotFound,
                otherResponse.StatusCode);
        }
        finally
        {
            await CleanupCustomerAsync(
                customerBEmail);

            await CleanupCustomerAsync(
                scenario.Email);

            await DeleteProductAsync(
                scenario.ProductId);
        }
    }

    // ---------------------------------------------------------
    // 15. CANCELLATION + STOCK RESTORATION
    // ---------------------------------------------------------

    [Fact]
    public async Task CancelMyOrder_RestoresProductStock()
    {
        var scenario =
            await CreateCheckedOutOrderAsync(
                stock: 5,
                quantity: 2,
                price: 20m);

        using var customerClient =
            CreateAuthenticatedClient(
                scenario.Token);

        try
        {
            Assert.Equal(
                3,
                await GetProductStockAsync(
                    scenario.ProductId));

            var response =
                await customerClient.PatchAsync(
                    $"/api/me/orders/{scenario.OrderId}/cancel",
                    null);

            Assert.Equal(
                HttpStatusCode.OK,
                response.StatusCode);

            var order =
                await response.Content
                    .ReadFromJsonAsync<OrderResponseDto>(
                        JsonOptions);

            Assert.NotNull(order);

            Assert.Equal(
                OrderStatus.Cancelled,
                order.Status);

            Assert.Equal(
                5,
                await GetProductStockAsync(
                    scenario.ProductId));
        }
        finally
        {
            await CleanupCustomerAsync(
                scenario.Email);

            await DeleteProductAsync(
                scenario.ProductId);
        }
    }

    // ---------------------------------------------------------
    // 16. VALID ADMIN STATUS WORKFLOW
    // ---------------------------------------------------------

    [Fact]
    public async Task ChangeStatus_WithValidWorkflow_ReachesCompleted()
    {
        var scenario =
            await CreateCheckedOutOrderAsync();

        var adminToken =
            await LoginAdminAsync();

        using var adminClient =
            CreateAuthenticatedClient(
                adminToken);

        try
        {
            var processing =
                await ChangeStatusAsync(
                    adminClient,
                    scenario.OrderId,
                    OrderStatus.Processing);

            Assert.Equal(
                HttpStatusCode.OK,
                processing.StatusCode);

            var shipped =
                await ChangeStatusAsync(
                    adminClient,
                    scenario.OrderId,
                    OrderStatus.Shipped);

            Assert.Equal(
                HttpStatusCode.OK,
                shipped.StatusCode);

            var completed =
                await ChangeStatusAsync(
                    adminClient,
                    scenario.OrderId,
                    OrderStatus.Completed);

            Assert.Equal(
                HttpStatusCode.OK,
                completed.StatusCode);

            var order =
                await completed.Content
                    .ReadFromJsonAsync<OrderResponseDto>(
                        JsonOptions);

            Assert.NotNull(order);

            Assert.Equal(
                OrderStatus.Completed,
                order.Status);
        }
        finally
        {
            await CleanupCustomerAsync(
                scenario.Email);

            await DeleteProductAsync(
                scenario.ProductId);
        }
    }

    // ---------------------------------------------------------
    // 17. INVALID ADMIN STATUS SKIP
    // ---------------------------------------------------------

    [Fact]
    public async Task ChangeStatus_WhenSkippingStep_ReturnsBadRequest()
    {
        var scenario =
            await CreateCheckedOutOrderAsync();

        var adminToken =
            await LoginAdminAsync();

        using var adminClient =
            CreateAuthenticatedClient(
                adminToken);

        try
        {
            // Pending -> Shipped is illegal.
            var response =
                await ChangeStatusAsync(
                    adminClient,
                    scenario.OrderId,
                    OrderStatus.Shipped);

            Assert.Equal(
                HttpStatusCode.BadRequest,
                response.StatusCode);

            var problem =
                await response.Content
                    .ReadFromJsonAsync<ProblemDetails>();

            Assert.NotNull(problem);

            Assert.Contains(
                "Cannot change order status",
                problem.Detail);
        }
        finally
        {
            await CleanupCustomerAsync(
                scenario.Email);

            await DeleteProductAsync(
                scenario.ProductId);
        }
    }

    // ---------------------------------------------------------
    // 18. PRODUCT OPTIMISTIC CONCURRENCY
    // ---------------------------------------------------------

    [Fact]
    public async Task UpdateStock_WithStaleRowVersion_ReturnsConflict()
    {
        var productId =
            await SeedProductAsync(
                stock: 10,
                price: 40m);

        var adminToken =
            await LoginAdminAsync();

        using var adminClient =
            CreateAuthenticatedClient(
                adminToken);

        try
        {
            // Get current RowVersion.
            var getResponse =
                await adminClient.GetAsync(
                    $"/api/products/admin/{productId}");

            Assert.Equal(
                HttpStatusCode.OK,
                getResponse.StatusCode);

            var originalProduct =
                await getResponse.Content
                    .ReadFromJsonAsync<
                        AdminProductResponseDto>(
                        JsonOptions);

            Assert.NotNull(originalProduct);

            var staleRowVersion =
                originalProduct.RowVersion.ToArray();

            // First request succeeds and changes RowVersion.
            var firstUpdate =
                await adminClient.PutAsJsonAsync(
                    $"/api/products/{productId}/stock",
                    new UpdateStockRequestDto
                    {
                        NewStock = 9,
                        RowVersion = staleRowVersion
                    },
                    JsonOptions);

            Assert.Equal(
                HttpStatusCode.OK,
                firstUpdate.StatusCode);

            // Second request deliberately uses OLD RowVersion.
            var staleUpdate =
                await adminClient.PutAsJsonAsync(
                    $"/api/products/{productId}/stock",
                    new UpdateStockRequestDto
                    {
                        NewStock = 8,
                        RowVersion = staleRowVersion
                    },
                    JsonOptions);

            Assert.Equal(
                HttpStatusCode.Conflict,
                staleUpdate.StatusCode);

            var problem =
                await staleUpdate.Content
                    .ReadFromJsonAsync<ProblemDetails>();

            Assert.NotNull(problem);

            Assert.Contains(
                "stock was changed",
                problem.Detail);
        }
        finally
        {
            await DeleteProductAsync(
                productId);
        }
    }

    // ---------------------------------------------------------
    // 19. CONCURRENT CHECKOUT — FINAL BOSS
    // ---------------------------------------------------------

    [Fact]
    public async Task ConcurrentCheckout_ForLastUnit_DoesNotOversell()
    {
        var (tokenA, emailA) =
            await RegisterAndLoginCustomerAsync();

        var (tokenB, emailB) =
            await RegisterAndLoginCustomerAsync();

        using var customerAClient =
            CreateAuthenticatedClient(tokenA);

        using var customerBClient =
            CreateAuthenticatedClient(tokenB);

        var productId =
            await SeedProductAsync(
                stock: 1,
                price: 50m);

        try
        {
            var addressA =
                await CreateAddressAsync(
                    customerAClient);

            var addressB =
                await CreateAddressAsync(
                    customerBClient);

            var basketA =
                await customerAClient.PostAsJsonAsync(
                    "/api/me/basket/items",
                    new AddBasketItemRequestDto
                    {
                        ProductId = productId,
                        Quantity = 1
                    });

            Assert.Equal(
                HttpStatusCode.OK,
                basketA.StatusCode);

            var basketB =
                await customerBClient.PostAsJsonAsync(
                    "/api/me/basket/items",
                    new AddBasketItemRequestDto
                    {
                        ProductId = productId,
                        Quantity = 1
                    });

            Assert.Equal(
                HttpStatusCode.OK,
                basketB.StatusCode);

            // Start both checkout requests without
            // waiting for the first one to finish.
            var checkoutATask =
                customerAClient.PostAsJsonAsync(
                    "/api/me/checkout",
                    new CheckoutRequestDto
                    {
                        AddressId = addressA.Id
                    });

            var checkoutBTask =
                customerBClient.PostAsJsonAsync(
                    "/api/me/checkout",
                    new CheckoutRequestDto
                    {
                        AddressId = addressB.Id
                    });

            await Task.WhenAll(
                checkoutATask,
                checkoutBTask);

            var responseA =
                await checkoutATask;

            var responseB =
                await checkoutBTask;

            var responses =
                new[]
                {
                    responseA,
                    responseB
                };

            Assert.Equal(
                1,
                responses.Count(
                    r => r.StatusCode ==
                         HttpStatusCode.OK));

            Assert.Equal(
                1,
                responses.Count(
                    r =>
                        r.StatusCode ==
                            HttpStatusCode.Conflict ||
                        r.StatusCode ==
                            HttpStatusCode.BadRequest));

            using var scope =
                _factory.Services.CreateScope();

            var context =
                scope.ServiceProvider
                    .GetRequiredService<
                        OrderManagementDbContext>();

            var product =
                await context.Products
                    .SingleAsync(
                        p => p.Id == productId);

            // Critical business invariant:
            // inventory can never become negative.
            Assert.Equal(
                0,
                product.Stock);

            var customers =
                await context.Customers
                    .Where(c =>
                        c.Email == emailA ||
                        c.Email == emailB)
                    .ToListAsync();

            var customerIds =
                customers
                    .Select(c => c.Id)
                    .ToList();

            var orderCount =
                await context.Orders
                    .CountAsync(o =>
                        customerIds.Contains(
                            o.CustomerId));

            Assert.Equal(
                1,
                orderCount);
        }
        finally
        {
            await CleanupCustomerAsync(
                emailA);

            await CleanupCustomerAsync(
                emailB);

            await DeleteProductAsync(
                productId);
        }
    }

    // =========================================================
    // HELPERS
    // =========================================================

    private async Task<CheckoutScenario>
        CreateCheckedOutOrderAsync(
            int stock = 10,
            int quantity = 2,
            decimal price = 25m)
    {
        var (token, email) =
            await RegisterAndLoginCustomerAsync();

        using var customerClient =
            CreateAuthenticatedClient(token);

        var address =
            await CreateAddressAsync(
                customerClient);

        var productId =
            await SeedProductAsync(
                stock,
                price);

        var addResponse =
            await customerClient.PostAsJsonAsync(
                "/api/me/basket/items",
                new AddBasketItemRequestDto
                {
                    ProductId = productId,
                    Quantity = quantity
                });

        Assert.Equal(
            HttpStatusCode.OK,
            addResponse.StatusCode);

        var checkoutResponse =
            await customerClient.PostAsJsonAsync(
                "/api/me/checkout",
                new CheckoutRequestDto
                {
                    AddressId = address.Id
                });

        Assert.Equal(
            HttpStatusCode.OK,
            checkoutResponse.StatusCode);

        var checkout =
            await checkoutResponse.Content
                .ReadFromJsonAsync<CheckoutResponseDto>(
                    JsonOptions);

        Assert.NotNull(checkout);
        Assert.NotNull(checkout.Order);
        Assert.True(checkout.Order.Id > 0);
        Assert.False(string.IsNullOrWhiteSpace(checkout.ClientSecret));

        return new CheckoutScenario(
            token,
            email,
            productId,
            checkout.Order.Id);
    }

    private async Task<
        (string Token, string Email)>
        RegisterAndLoginCustomerAsync()
    {
        var email =
            $"order-{Guid.NewGuid():N}@test.local";

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
                .ReadFromJsonAsync<LoginResponseDto>();

        Assert.NotNull(login);

        return (
            login.Token,
            email);
    }

    private async Task<string>
        LoginAdminAsync()
    {
        var response =
            await _client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequestDto
                {
                    Email =
                        CustomWebApplicationFactory
                            .TestAdminEmail,

                    Password =
                        CustomWebApplicationFactory
                            .TestAdminPassword
                });

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var login =
            await response.Content
                .ReadFromJsonAsync<LoginResponseDto>();

        Assert.NotNull(login);

        return login.Token;
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
                Name =
                    "Order Integration Product",

                Sku =
                    $"ORDER-{Guid.NewGuid():N}",

                Price = price,
                Stock = stock,
                IsActive = true,
                CategoryId = categoryId
            };

        context.Products.Add(product);

        await context.SaveChangesAsync();

        return product.Id;
    }

    private async Task<HttpResponseMessage>
        ChangeStatusAsync(
            HttpClient client,
            int orderId,
            OrderStatus status)
    {
        return await client.PatchAsJsonAsync(
            $"/api/orders/{orderId}/status",
            new ChangeOrderStatusRequestDto
            {
                Status = status
            },
            JsonOptions);
    }

    private async Task<int>
        GetProductStockAsync(
            int productId)
    {
        using var scope =
            _factory.Services.CreateScope();

        var context =
            scope.ServiceProvider
                .GetRequiredService<
                    OrderManagementDbContext>();

        return await context.Products
            .Where(p => p.Id == productId)
            .Select(p => p.Stock)
            .SingleAsync();
    }

    private async Task CleanupCustomerAsync(
        string email)
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
                    .Where(o =>
                        o.CustomerId ==
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
                        b =>
                            b.CustomerId ==
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
                    .Where(a =>
                        a.CustomerId ==
                        customer.Id)
                    .ToListAsync();

            context.Addresses.RemoveRange(
                addresses);

            context.Customers.Remove(
                customer);

            await context.SaveChangesAsync();
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

    private async Task DeleteProductAsync(
        int productId)
    {
        using var scope =
            _factory.Services.CreateScope();

        var context =
            scope.ServiceProvider
                .GetRequiredService<
                    OrderManagementDbContext>();

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

    private sealed record CheckoutScenario(
        string Token,
        string Email,
        int ProductId,
        int OrderId);
}
