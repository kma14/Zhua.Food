using Zhua.Infrastructure;
using Zhua.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Admin-only write actions are marked [Authorize("Admin")]. Real role enforcement is the deferred auth task
// (plan-cc Phase 4 "Auth on /admin/*"): no authentication scheme is wired yet, so this policy currently allows
// all callers — the attributes + policy are the seam. Flip to `policy.RequireRole("admin")` (+ AddAuthentication)
// when auth lands; nothing else changes.
builder.Services.AddAuthorization(options =>
    options.AddPolicy("Admin", policy => policy.RequireAssertion(_ => true)));

var conn = builder.Configuration.GetConnectionString("Default") ?? DbDefaults.DevConnectionString;

// Query side: persistence only — no ingestion/matching services (D19). Never migrates (D5), never crawls.
builder.Services.AddPersistence(conn);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthorization();
app.MapControllers();

app.Run();

// Exposes the implicit Program class to the integration-test project (WebApplicationFactory<Program>).
public partial class Program;
