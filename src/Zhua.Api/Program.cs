using Microsoft.EntityFrameworkCore;
using Zhua.Infrastructure;
using Zhua.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var conn = builder.Configuration.GetConnectionString("Default")
           ?? "Host=localhost;Port=5433;Database=zhua;Username=zhua;Password=zhua";

// Query side: reads already-persisted data only. It never migrates (plan D5) and never triggers crawling.
builder.Services.AddInfrastructure(conn);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "zhua.api" }));

app.MapGet("/health/db", async (ZhuaDbContext db) =>
    await db.Database.CanConnectAsync()
        ? Results.Ok(new { db = "up" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.Run();
