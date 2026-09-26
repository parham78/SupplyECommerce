using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

public class OrderManagementDbContext
    : IdentityDbContext<ApplicationUser>
{
    public DbSet<Product> Products { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<Customer> Customers { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<Address> Addresses { get; set; }
    public DbSet<Basket> Baskets { get; set; }
    public DbSet<BasketItem> BasketItems { get; set; }
    public DbSet<Category> Categories { get; set; }

    public OrderManagementDbContext(
        DbContextOptions<OrderManagementDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Product price
        modelBuilder.Entity<Product>()
            .Property(p => p.Price)
            .HasPrecision(18, 2);
        modelBuilder.Entity<Product>()
    .Property(p => p.RowVersion)
    .IsRowVersion();
        modelBuilder.Entity<Product>()
        .Property(p => p.Sku)
        .HasMaxLength(50)
        .IsRequired();

        modelBuilder.Entity<Product>()
            .HasIndex(p => p.Sku)
            .IsUnique();
        modelBuilder.Entity<Customer>()
        .HasIndex(c => c.Email)
        .IsUnique();
        modelBuilder.Entity<Customer>()
    .HasOne(c => c.User)
    .WithOne()
    .HasForeignKey<Customer>(c => c.UserId)
    .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Category>()
        .Property(c => c.Name)
        .HasMaxLength(100)
        .IsRequired();

        modelBuilder.Entity<Category>()
            .Property(c => c.Slug)
            .HasMaxLength(100)
            .IsRequired();

        modelBuilder.Entity<Category>()
            .HasIndex(c => c.Name)
            .IsUnique();

        modelBuilder.Entity<Category>()
            .HasIndex(c => c.Slug)
            .IsUnique();

        modelBuilder.Entity<Product>()
            .HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Address>()
        .HasOne(a => a.Customer)
        .WithMany(c => c.Addresses)
        .HasForeignKey(a => a.CustomerId)
        .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Address>()
    .Property(a => a.Label)
    .HasMaxLength(50)
    .IsRequired();

        modelBuilder.Entity<Address>()
            .Property(a => a.RecipientName)
            .HasMaxLength(100)
            .IsRequired();

        modelBuilder.Entity<Address>()
            .Property(a => a.AddressLine1)
            .HasMaxLength(200)
            .IsRequired();

        modelBuilder.Entity<Address>()
            .Property(a => a.AddressLine2)
            .HasMaxLength(200);

        modelBuilder.Entity<Address>()
            .Property(a => a.City)
            .HasMaxLength(100)
            .IsRequired();

        modelBuilder.Entity<Address>()
            .Property(a => a.Province)
            .HasMaxLength(100)
            .IsRequired();

        modelBuilder.Entity<Address>()
            .Property(a => a.PostalCode)
            .HasMaxLength(20)
            .IsRequired();

        modelBuilder.Entity<Address>()
            .Property(a => a.Country)
            .HasMaxLength(100)
            .IsRequired();

        modelBuilder.Entity<Address>()
            .Property(a => a.PhoneNumber)
            .HasMaxLength(30);

        modelBuilder.Entity<Order>()
            .HasIndex(o => o.CustomerId);

        // Order total price
        modelBuilder.Entity<Order>()
            .Property(o => o.TotalPrice)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Order>()
    .Property(o => o.Status)
    .HasConversion<string>();

        modelBuilder.Entity<Order>()
        .Property(o => o.ShippingRecipientName)
        .HasMaxLength(100);

        modelBuilder.Entity<Order>()
            .Property(o => o.ShippingAddressLine1)
            .HasMaxLength(200);

        modelBuilder.Entity<Order>()
            .Property(o => o.ShippingAddressLine2)
            .HasMaxLength(200);

        modelBuilder.Entity<Order>()
            .Property(o => o.ShippingCity)
            .HasMaxLength(100);

        modelBuilder.Entity<Order>()
            .Property(o => o.ShippingProvince)
            .HasMaxLength(100);

        modelBuilder.Entity<Order>()
            .Property(o => o.ShippingPostalCode)
            .HasMaxLength(20);

        modelBuilder.Entity<Order>()
            .Property(o => o.ShippingCountry)
            .HasMaxLength(100);

        modelBuilder.Entity<Order>()
            .Property(o => o.ShippingPhoneNumber)
            .HasMaxLength(30);

        modelBuilder.Entity<Order>()
    .Property(o => o.PaymentStatus)
    .HasConversion<string>()
    .HasMaxLength(20)
    .HasDefaultValue(PaymentStatus.NotTracked);
        modelBuilder.Entity<Order>()
        .Property(o => o.StripePaymentIntentId)
        .HasMaxLength(255);
        modelBuilder.Entity<Order>()
        .HasIndex(o => o.StripePaymentIntentId)
        .IsUnique()
        .HasFilter("[StripePaymentIntentId] IS NOT NULL");

        // OrderItem historical price
        modelBuilder.Entity<OrderItem>()
            .Property(oi => oi.UnitPrice)
            .HasPrecision(18, 2);

        // Customer 1 ---- many Orders
        modelBuilder.Entity<Order>()
            .HasOne(o => o.Customer)
            .WithMany(c => c.Orders)
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Order 1 ---- many OrderItems
        modelBuilder.Entity<OrderItem>()
            .HasOne(oi => oi.Order)
            .WithMany(o => o.OrderItems)
            .HasForeignKey(oi => oi.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Product 1 ---- many OrderItems
        modelBuilder.Entity<OrderItem>()
            .HasOne(oi => oi.Product)
            .WithMany(p => p.OrderItems)
            .HasForeignKey(oi => oi.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // Customer 1 ---- 1 Basket
        modelBuilder.Entity<Basket>()
            .HasOne(b => b.Customer)
            .WithOne()
            .HasForeignKey<Basket>(b => b.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Basket>()
            .HasIndex(b => b.CustomerId)
            .IsUnique();

        // Basket 1 ---- many BasketItems
        modelBuilder.Entity<BasketItem>()
            .HasOne(bi => bi.Basket)
            .WithMany(b => b.Items)
            .HasForeignKey(bi => bi.BasketId)
            .OnDelete(DeleteBehavior.Cascade);

        // Product 1 ---- many BasketItems
        modelBuilder.Entity<BasketItem>()
            .HasOne(bi => bi.Product)
            .WithMany()
            .HasForeignKey(bi => bi.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // Same product can appear only once in a basket
        modelBuilder.Entity<BasketItem>()
            .HasIndex(bi => new
            {
                bi.BasketId,
                bi.ProductId
            })
            .IsUnique();
    }
}