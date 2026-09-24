using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MilkTeaWeb.Data;
using MilkTeaWeb.Models.Identity;
using MilkTeaWeb.Options;
using MilkTeaWeb.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddAuthorization();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(24);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddScoped<DemoDataSeeder>();
builder.Services.AddScoped<DemoProductImageSeeder>();

builder.Services.Configure<SmtpOptions>(
    builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.AddOptions<CheckoutOptions>()
    .Bind(builder.Configuration.GetSection(CheckoutOptions.SectionName))
    .Validate(options => options.FixedDeliveryFee > 0m,
        "Checkout:FixedDeliveryFee must be configured as a positive value.")
    .ValidateOnStart();
builder.Services.AddScoped<EmailService>();
builder.Services.Configure<AdminBootstrapOptions>(
    builder.Configuration.GetSection(AdminBootstrapOptions.SectionName));
builder.Services.AddScoped<AdminBootstrapService>();
builder.Services.AddScoped<ProductImageService>();
builder.Services.AddScoped<GuestCartSessionStore>();
builder.Services.AddScoped<CartService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<OrderNotificationService>();

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireDigit = true;
        options.Password.RequireNonAlphanumeric = true;

        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = true;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var adminBootstrapService = scope.ServiceProvider.GetRequiredService<AdminBootstrapService>();
    await adminBootstrapService.EnsureAdminAsync();

    var demoDataSeeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
    await demoDataSeeder.SeedIfEligibleAsync();

    var demoProductImageSeeder = scope.ServiceProvider.GetRequiredService<DemoProductImageSeeder>();
    await demoProductImageSeeder.SeedIfEligibleAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseStatusCodePagesWithReExecute("/Home/Status", "?code={0}");
app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
