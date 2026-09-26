using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public class CustomWebApplicationFactory
    : WebApplicationFactory<Program>
{
    private const string TestConnectionString =
        "Server=localhost;Database=OrderManagementEfCoreDb_Test;Trusted_Connection=True;TrustServerCertificate=True;";

    public const string TestAdminEmail =
        "integration.admin@test.local";

    public const string TestAdminPassword =
        "IntegrationAdmin123!";

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:Key"] =
                        "IntegrationTestingKey12345678901234567890",

                    ["Jwt:Issuer"] =
                        "OrderManagementApi",

                    ["Jwt:Audience"] =
                        "OrderManagementApi",

                    ["Jwt:ExpirationMinutes"] =
                        "30",

                    ["Admin:Email"] =
                        TestAdminEmail,

                    ["Admin:Password"] =
                        TestAdminPassword
                });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IStripePaymentService>();
            services.AddScoped<IStripePaymentService, FakeStripePaymentService>();

            services.RemoveAll<
                DbContextOptions<OrderManagementDbContext>>();

            services.RemoveAll<
                IDbContextOptionsConfiguration<
                    OrderManagementDbContext>>();

            services.RemoveAll<OrderManagementDbContext>();

            services.AddDbContext<OrderManagementDbContext>(
                options =>
                    options.UseSqlServer(
                        TestConnectionString));

            using var serviceProvider =
                services.BuildServiceProvider();

            using var scope =
                serviceProvider.CreateScope();

            var context =
                scope.ServiceProvider
                    .GetRequiredService<
                        OrderManagementDbContext>();

            context.Database.Migrate();
        });
    }
}
