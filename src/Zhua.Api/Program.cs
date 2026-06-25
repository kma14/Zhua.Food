using Microsoft.EntityFrameworkCore;
using Zhua.Api.Endpoints;
using Zhua.Infrastructure;
using Zhua.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var conn = builder.Configuration.GetConnectionString("Default") ?? DbDefaults.DevConnectionString;

// Query side: persistence only — no ingestion/matching services (D19). Never migrates (D5), never crawls.
builder.Services.AddPersistence(conn);

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

app.MapStoreEndpoints();     // /stores (the physical stores we track)
app.MapCategoryEndpoints();  // /categories (canonical category tree, D22)
app.MapProductEndpoints();   // /products/search, /products/{id} (compare)
app.MapDealEndpoints();      // /deals
app.MapMatchReviewEndpoints(); // /admin/match-candidates (+ approve/reject)

app.Run();

// Exposes the implicit Program class to the integration-test project (WebApplicationFactory<Program>).
public partial class Program;
