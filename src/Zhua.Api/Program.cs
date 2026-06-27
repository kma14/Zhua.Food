using Zhua.Infrastructure;
using Zhua.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var conn = builder.Configuration.GetConnectionString("Default") ?? DbDefaults.DevConnectionString;

// Query side: persistence only — no ingestion/matching services (D19). Never migrates (D5), never crawls.
builder.Services.AddPersistence(conn);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();

app.Run();

// Exposes the implicit Program class to the integration-test project (WebApplicationFactory<Program>).
public partial class Program;
