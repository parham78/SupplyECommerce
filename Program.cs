using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json.Serialization;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter());
    });

builder.Services.AddScoped<ICategoryService, CategoryService>();

builder.Services.AddScoped<
    IAdminDashboardService,
    AdminDashboardService>();

builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.Configure<StripeOptions>(
    builder.Configuration.GetSection("Stripe"));
builder.Services.AddScoped<StripePaymentService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAddressService, AddressService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IBasketService, BasketService>();
builder.Services.AddScoped<
    ICheckoutService,
    CheckoutService>();
builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection("Jwt"))
    .ValidateDataAnnotations()
    .Validate(
        options =>
            Encoding.UTF8.GetByteCount(options.Key) >= 32,
        "Jwt:Key must be at least 32 bytes long.")
    .ValidateOnStart();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;

        var jwtOptions =
            builder.Configuration
                .GetSection("Jwt")
                .Get<JwtOptions>()!;

        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtOptions.Issuer,

                ValidateAudience = true,
                ValidAudience = jwtOptions.Audience,

                ValidateLifetime = true,

                ValidateIssuerSigningKey = true,
                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtOptions.Key)),

                NameClaimType = JwtRegisteredClaimNames.Sub,
                RoleClaimType = "role"
            };
    });
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddHttpContextAccessor();

builder.Services.AddDbContext<OrderManagementDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddOpenApi();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<OrderManagementDbContext>();

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var roleManager =
        scope.ServiceProvider
            .GetRequiredService<RoleManager<IdentityRole>>();

    var userManager =
        scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();

    await IdentitySeeder.SeedRolesAndAdminAsync(
        roleManager,
        userManager,
        app.Configuration);
}

app.UseExceptionHandler();

// Configure the HTTP request pipeline.

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// app.UseHttpsRedirection();
app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();
if (app.Environment.IsDevelopment())
{
    app.MapPost(
        "/api/dev/stripe-test/{orderId:int}",
        async (
            int orderId,
            OrderManagementDbContext db,
            StripePaymentService stripePaymentService,
            CancellationToken cancellationToken) =>
        {
            var order = await db.Orders.FindAsync(
                new object[] { orderId },
                cancellationToken);

            if (order == null)
            {
                return Results.NotFound();
            }

            var paymentIntent =
                await stripePaymentService.CreatePaymentIntentAsync(
                    order,
                    cancellationToken);

            return Results.Ok(new
            {
                paymentIntentId = paymentIntent.Id,
                status = paymentIntent.Status,
                amount = paymentIntent.Amount,
                currency = paymentIntent.Currency
            });
        });
}
app.Run();
public partial class Program
{
}