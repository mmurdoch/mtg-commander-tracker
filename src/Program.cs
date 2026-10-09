using Microsoft.EntityFrameworkCore;
using mtg_game_tracker.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();

var defaultDbFolder = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(defaultDbFolder);
var defaultConnectionString = $"Data Source={Path.Combine(defaultDbFolder, "mtg-games.db")}";
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? defaultConnectionString;
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    context.Database.EnsureCreated();

    if (!context.Colors.Any())
    {
        context.Colors.AddRange(ColorIdentitySeeder.All);
        context.SaveChanges();
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.Run();

public partial class Program { }
