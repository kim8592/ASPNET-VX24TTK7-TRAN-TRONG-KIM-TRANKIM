using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MilkTeaWeb.Models.Entities;
using MilkTeaWeb.Models.Identity;
using MilkTeaWeb.Security;

namespace MilkTeaWeb.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    public DbSet<Size> Sizes => Set<Size>();

    public DbSet<ProductSize> ProductSizes => Set<ProductSize>();

    public DbSet<Topping> Toppings => Set<Topping>();

    public DbSet<ProductTopping> ProductToppings => Set<ProductTopping>();

    public DbSet<SugarLevel> SugarLevels => Set<SugarLevel>();

    public DbSet<ProductSugarLevel> ProductSugarLevels => Set<ProductSugarLevel>();

    public DbSet<IceLevel> IceLevels => Set<IceLevel>();

    public DbSet<ProductIceLevel> ProductIceLevels => Set<ProductIceLevel>();

    public DbSet<Store> Stores => Set<Store>();

    public DbSet<Cart> Carts => Set<Cart>();

    public DbSet<CartItem> CartItems => Set<CartItem>();

    public DbSet<CartItemTopping> CartItemToppings => Set<CartItemTopping>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<OrderItemTopping> OrderItemToppings => Set<OrderItemTopping>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.FullName).HasMaxLength(150).IsRequired();
            entity.Property(user => user.DefaultDeliveryAddress).HasMaxLength(500);
        });

        modelBuilder.Entity<IdentityRole>().HasData(
            new IdentityRole
            {
                Id = "4d2282a1-0f4e-4f15-8898-7de085d2e56e",
                Name = RoleNames.Customer,
                NormalizedName = "CUSTOMER",
                ConcurrencyStamp = "a566a445-99b3-4dd1-a1fd-29f399c5fbcb"
            },
            new IdentityRole
            {
                Id = "910a788f-45ad-4f06-a4cf-c2dd21b8ffc1",
                Name = RoleNames.Admin,
                NormalizedName = "ADMIN",
                ConcurrencyStamp = "b6ea283a-f062-4b75-b822-9a2a68dd32ee"
            });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasKey(category => category.CategoryId);
            entity.Property(category => category.Name).HasMaxLength(100).IsRequired();
            entity.Property(category => category.IsActive).HasDefaultValue(true);
            entity.HasIndex(category => category.Name).IsUnique();

            entity.HasMany(category => category.Products)
                .WithOne(product => product.Category)
                .HasForeignKey(product => product.CategoryId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(product => product.ProductId);
            entity.Property(product => product.Name).HasMaxLength(150).IsRequired();
            entity.Property(product => product.Description).HasMaxLength(1000);
            entity.Property(product => product.IsAvailable).HasDefaultValue(true);
            entity.HasIndex(product => product.CategoryId);
        });

        modelBuilder.Entity<ProductImage>(entity =>
        {
            entity.ToTable("ProductImages", table =>
                table.HasCheckConstraint("CK_ProductImage_DisplayOrder_NonNegative", "[DisplayOrder] >= 0"));
            entity.HasKey(image => image.ProductImageId);
            entity.Property(image => image.ImagePath).HasMaxLength(500).IsRequired();
            entity.Property(image => image.DisplayOrder).HasDefaultValue(0);
            entity.HasIndex(image => new { image.ProductId, image.DisplayOrder });

            entity.HasOne(image => image.Product)
                .WithMany(product => product.ProductImages)
                .HasForeignKey(image => image.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Size>(entity =>
        {
            entity.ToTable("Sizes", table =>
                table.HasCheckConstraint("CK_Size_DisplayOrder_NonNegative", "[DisplayOrder] >= 0"));
            entity.HasKey(size => size.SizeId);
            entity.Property(size => size.Name).HasMaxLength(20).IsRequired();
            entity.Property(size => size.DisplayOrder).HasDefaultValue(0);
            entity.HasIndex(size => size.Name).IsUnique();
        });

        modelBuilder.Entity<ProductSize>(entity =>
        {
            entity.ToTable("ProductSizes", table =>
                table.HasCheckConstraint("CK_ProductSize_Price_Positive", "[Price] > 0"));
            entity.HasKey(productSize => productSize.ProductSizeId);
            entity.Property(productSize => productSize.Price).HasPrecision(18, 2);
            entity.HasIndex(productSize => new { productSize.ProductId, productSize.SizeId }).IsUnique();
            entity.HasIndex(productSize => productSize.SizeId);

            entity.HasOne(productSize => productSize.Product)
                .WithMany(product => product.ProductSizes)
                .HasForeignKey(productSize => productSize.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(productSize => productSize.Size)
                .WithMany(size => size.ProductSizes)
                .HasForeignKey(productSize => productSize.SizeId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Topping>(entity =>
        {
            entity.ToTable("Toppings", table =>
                table.HasCheckConstraint("CK_Topping_Price_NonNegative", "[Price] >= 0"));
            entity.HasKey(topping => topping.ToppingId);
            entity.Property(topping => topping.Name).HasMaxLength(100).IsRequired();
            entity.Property(topping => topping.Price).HasPrecision(18, 2);
            entity.Property(topping => topping.IsActive).HasDefaultValue(true);
            entity.HasIndex(topping => topping.Name).IsUnique();
        });

        modelBuilder.Entity<ProductTopping>(entity =>
        {
            entity.ToTable("ProductToppings");
            entity.HasKey(productTopping => new { productTopping.ProductId, productTopping.ToppingId });
            entity.HasIndex(productTopping => productTopping.ToppingId);

            entity.HasOne(productTopping => productTopping.Product)
                .WithMany(product => product.ProductToppings)
                .HasForeignKey(productTopping => productTopping.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(productTopping => productTopping.Topping)
                .WithMany(topping => topping.ProductToppings)
                .HasForeignKey(productTopping => productTopping.ToppingId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<SugarLevel>(entity =>
        {
            entity.ToTable("SugarLevels", table =>
            {
                table.HasCheckConstraint("CK_SugarLevel_Percentage_Range", "[Percentage] BETWEEN 0 AND 100");
                table.HasCheckConstraint("CK_SugarLevel_DisplayOrder_NonNegative", "[DisplayOrder] >= 0");
            });
            entity.HasKey(sugarLevel => sugarLevel.SugarLevelId);
            entity.Property(sugarLevel => sugarLevel.DisplayOrder).HasDefaultValue(0);
            entity.HasIndex(sugarLevel => sugarLevel.Percentage).IsUnique();
            entity.HasData(
                new SugarLevel { SugarLevelId = 1, Percentage = 0, DisplayOrder = 0 },
                new SugarLevel { SugarLevelId = 2, Percentage = 30, DisplayOrder = 1 },
                new SugarLevel { SugarLevelId = 3, Percentage = 50, DisplayOrder = 2 },
                new SugarLevel { SugarLevelId = 4, Percentage = 70, DisplayOrder = 3 },
                new SugarLevel { SugarLevelId = 5, Percentage = 100, DisplayOrder = 4 });
        });

        modelBuilder.Entity<ProductSugarLevel>(entity =>
        {
            entity.ToTable("ProductSugarLevels");
            entity.HasKey(productSugarLevel => new { productSugarLevel.ProductId, productSugarLevel.SugarLevelId });
            entity.HasIndex(productSugarLevel => productSugarLevel.SugarLevelId);

            entity.HasOne(productSugarLevel => productSugarLevel.Product)
                .WithMany(product => product.ProductSugarLevels)
                .HasForeignKey(productSugarLevel => productSugarLevel.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(productSugarLevel => productSugarLevel.SugarLevel)
                .WithMany(sugarLevel => sugarLevel.ProductSugarLevels)
                .HasForeignKey(productSugarLevel => productSugarLevel.SugarLevelId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<IceLevel>(entity =>
        {
            entity.ToTable("IceLevels", table =>
            {
                table.HasCheckConstraint("CK_IceLevel_Percentage_Range", "[Percentage] BETWEEN 0 AND 100");
                table.HasCheckConstraint("CK_IceLevel_DisplayOrder_NonNegative", "[DisplayOrder] >= 0");
            });
            entity.HasKey(iceLevel => iceLevel.IceLevelId);
            entity.Property(iceLevel => iceLevel.DisplayOrder).HasDefaultValue(0);
            entity.HasIndex(iceLevel => iceLevel.Percentage).IsUnique();
            entity.HasData(
                new IceLevel { IceLevelId = 1, Percentage = 0, DisplayOrder = 0 },
                new IceLevel { IceLevelId = 2, Percentage = 30, DisplayOrder = 1 },
                new IceLevel { IceLevelId = 3, Percentage = 50, DisplayOrder = 2 },
                new IceLevel { IceLevelId = 4, Percentage = 70, DisplayOrder = 3 },
                new IceLevel { IceLevelId = 5, Percentage = 100, DisplayOrder = 4 });
        });

        modelBuilder.Entity<ProductIceLevel>(entity =>
        {
            entity.ToTable("ProductIceLevels");
            entity.HasKey(productIceLevel => new { productIceLevel.ProductId, productIceLevel.IceLevelId });
            entity.HasIndex(productIceLevel => productIceLevel.IceLevelId);

            entity.HasOne(productIceLevel => productIceLevel.Product)
                .WithMany(product => product.ProductIceLevels)
                .HasForeignKey(productIceLevel => productIceLevel.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(productIceLevel => productIceLevel.IceLevel)
                .WithMany(iceLevel => iceLevel.ProductIceLevels)
                .HasForeignKey(productIceLevel => productIceLevel.IceLevelId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Store>(entity =>
        {
            entity.ToTable("Stores");
            entity.HasKey(store => store.StoreId);
            entity.Property(store => store.Name).HasMaxLength(150).IsRequired();
            entity.Property(store => store.Address).HasMaxLength(300).IsRequired();
            entity.Property(store => store.Phone).HasMaxLength(20).IsRequired();
            entity.Property(store => store.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<Cart>(entity =>
        {
            entity.ToTable("Carts");
            entity.HasKey(cart => cart.CartId);
            entity.Property(cart => cart.UserId).HasMaxLength(450).IsRequired();
            entity.HasIndex(cart => cart.UserId).IsUnique();

            entity.HasOne(cart => cart.User)
                .WithOne(user => user.Cart)
                .HasForeignKey<Cart>(cart => cart.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.ToTable("CartItems", table =>
                table.HasCheckConstraint("CK_CartItem_Quantity_Positive", "[Quantity] > 0"));
            entity.HasKey(cartItem => cartItem.CartItemId);
            entity.HasIndex(cartItem => cartItem.CartId);
            entity.HasIndex(cartItem => cartItem.ProductSizeId);

            entity.HasOne(cartItem => cartItem.Cart)
                .WithMany(cart => cart.CartItems)
                .HasForeignKey(cartItem => cartItem.CartId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(cartItem => cartItem.ProductSize)
                .WithMany(productSize => productSize.CartItems)
                .HasForeignKey(cartItem => cartItem.ProductSizeId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(cartItem => cartItem.SugarLevel)
                .WithMany(sugarLevel => sugarLevel.CartItems)
                .HasForeignKey(cartItem => cartItem.SugarLevelId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(cartItem => cartItem.IceLevel)
                .WithMany(iceLevel => iceLevel.CartItems)
                .HasForeignKey(cartItem => cartItem.IceLevelId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<CartItemTopping>(entity =>
        {
            entity.ToTable("CartItemToppings");
            entity.HasKey(cartItemTopping => new { cartItemTopping.CartItemId, cartItemTopping.ToppingId });
            entity.HasIndex(cartItemTopping => cartItemTopping.ToppingId);

            entity.HasOne(cartItemTopping => cartItemTopping.CartItem)
                .WithMany(cartItem => cartItem.CartItemToppings)
                .HasForeignKey(cartItemTopping => cartItemTopping.CartItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(cartItemTopping => cartItemTopping.Topping)
                .WithMany(topping => topping.CartItemToppings)
                .HasForeignKey(cartItemTopping => cartItemTopping.ToppingId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders", table =>
            {
                table.HasCheckConstraint("CK_Order_Subtotal_NonNegative", "[Subtotal] >= 0");
                table.HasCheckConstraint("CK_Order_DeliveryFee_NonNegative", "[DeliveryFee] >= 0");
                table.HasCheckConstraint("CK_Order_TotalAmount_NonNegative", "[TotalAmount] >= 0");
            });
            entity.HasKey(order => order.OrderId);
            entity.Property(order => order.UserId).HasMaxLength(450).IsRequired();
            entity.Property(order => order.StoreName).HasMaxLength(150).IsRequired();
            entity.Property(order => order.StoreAddress).HasMaxLength(300).IsRequired();
            entity.Property(order => order.StorePhone).HasMaxLength(20).IsRequired();
            entity.Property(order => order.RecipientName).HasMaxLength(150).IsRequired();
            entity.Property(order => order.RecipientPhone).HasMaxLength(20).IsRequired();
            entity.Property(order => order.DeliveryAddress).HasMaxLength(500);
            entity.Property(order => order.OrderNote).HasMaxLength(500);
            entity.Property(order => order.Subtotal).HasPrecision(18, 2);
            entity.Property(order => order.DeliveryFee).HasPrecision(18, 2);
            entity.Property(order => order.TotalAmount).HasPrecision(18, 2);
            entity.Property(order => order.CreatedAt).HasColumnType("datetime2");
            entity.Property(order => order.UpdatedAt).HasColumnType("datetime2");
            entity.HasIndex(order => new { order.UserId, order.CreatedAt });
            entity.HasIndex(order => order.StoreId);
            entity.HasIndex(order => new { order.OrderStatus, order.CreatedAt });

            entity.HasOne(order => order.User)
                .WithMany(user => user.Orders)
                .HasForeignKey(order => order.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(order => order.Store)
                .WithMany(store => store.Orders)
                .HasForeignKey(order => order.StoreId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("OrderItems", table =>
            {
                table.HasCheckConstraint("CK_OrderItem_UnitPrice_NonNegative", "[UnitPrice] >= 0");
                table.HasCheckConstraint("CK_OrderItem_Quantity_Positive", "[Quantity] > 0");
                table.HasCheckConstraint("CK_OrderItem_LineTotal_NonNegative", "[LineTotal] >= 0");
            });
            entity.HasKey(orderItem => orderItem.OrderItemId);
            entity.Property(orderItem => orderItem.ProductName).HasMaxLength(150).IsRequired();
            entity.Property(orderItem => orderItem.SizeName).HasMaxLength(20).IsRequired();
            entity.Property(orderItem => orderItem.UnitPrice).HasPrecision(18, 2);
            entity.Property(orderItem => orderItem.LineTotal).HasPrecision(18, 2);
            entity.HasIndex(orderItem => orderItem.OrderId);

            entity.HasOne(orderItem => orderItem.Order)
                .WithMany(order => order.OrderItems)
                .HasForeignKey(orderItem => orderItem.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(orderItem => orderItem.Product)
                .WithMany(product => product.OrderItems)
                .HasForeignKey(orderItem => orderItem.ProductId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<OrderItemTopping>(entity =>
        {
            entity.ToTable("OrderItemToppings", table =>
                table.HasCheckConstraint("CK_OrderItemTopping_UnitPrice_NonNegative", "[UnitPrice] >= 0"));
            entity.HasKey(orderItemTopping => orderItemTopping.OrderItemToppingId);
            entity.Property(orderItemTopping => orderItemTopping.ToppingName).HasMaxLength(100).IsRequired();
            entity.Property(orderItemTopping => orderItemTopping.UnitPrice).HasPrecision(18, 2);
            entity.HasIndex(orderItemTopping => orderItemTopping.OrderItemId);

            entity.HasOne(orderItemTopping => orderItemTopping.OrderItem)
                .WithMany(orderItem => orderItem.OrderItemToppings)
                .HasForeignKey(orderItemTopping => orderItemTopping.OrderItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(orderItemTopping => orderItemTopping.Topping)
                .WithMany(topping => topping.OrderItemToppings)
                .HasForeignKey(orderItemTopping => orderItemTopping.ToppingId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
