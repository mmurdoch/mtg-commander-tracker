using Microsoft.EntityFrameworkCore;
using mtg_game_tracker.Data;
using mtg_game_tracker.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();

var defaultDbFolder = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(defaultDbFolder);
var defaultConnectionString = $"Data Source={Path.Combine(defaultDbFolder, "mtg-games.db")}";
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? defaultConnectionString;
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));

builder.Services.AddHttpClient(ScryfallCommanderCatalog.HttpClientName);
builder.Services.AddSingleton<ICommanderCatalog>(sp => new ScryfallCommanderCatalog(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient(ScryfallCommanderCatalog.HttpClientName),
    Path.Combine(defaultDbFolder, "commanders.json"),
    TimeProvider.System,
    sp.GetRequiredService<ILogger<ScryfallCommanderCatalog>>()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    context.Database.EnsureCreated();

    // EnsureCreated doesn't alter existing SQLite databases, so add columns introduced later.
    if (context.Database.IsSqlite() &&
        !context.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Decks')").AsEnumerable().Contains("SecondCommander"))
    {
        context.Database.ExecuteSqlRaw("ALTER TABLE Decks ADD COLUMN SecondCommander TEXT NULL");
    }

    // Also adds identities introduced after the database was first seeded (e.g. Colorless).
    var existingColorIds = context.Colors.Select(c => c.Id).ToHashSet();
    var missingColors = ColorIdentitySeeder.All.Where(c => !existingColorIds.Contains(c.Id)).ToList();
    if (missingColors.Count > 0)
    {
        context.Colors.AddRange(missingColors);
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
