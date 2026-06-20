using Microsoft.EntityFrameworkCore;
using Zhua.Infrastructure.Persistence;

// One-shot migrator (plan D5). Compose runs this to completion before Api/Worker start,
// so neither of those auto-migrates and there is no migration race.

var conn = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
           ?? "Host=localhost;Port=5432;Database=zhua;Username=zhua;Password=zhua";

Console.WriteLine("[migrator] applying migrations...");

var options = new DbContextOptionsBuilder<ZhuaDbContext>()
    .UseNpgsql(conn)
    .Options;

await using var db = new ZhuaDbContext(options);
await db.Database.MigrateAsync();

Console.WriteLine("[migrator] migrations applied. Done.");
