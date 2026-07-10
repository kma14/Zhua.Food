using Zhua.Infrastructure;
using Zhua.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// CORS is only needed for the local-dev path where the browser calls this API cross-origin
// (front-end on the Vite dev server -> this API on another host/port). In production the front-end
// is served same-origin by nginx (which reverse-proxies the API), so no CORS is involved there.
// Origins are config-driven (`Cors:AllowedOrigins`), defaulting to the Vite dev server.
const string WebCorsPolicy = "web";
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173", "http://127.0.0.1:5173"];
builder.Services.AddCors(options =>
    options.AddPolicy(WebCorsPolicy, policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));

// Admin-only write actions are marked [Authorize("Admin")]. Real role enforcement is the deferred auth task
// (plan-cc Phase 4 "Auth on /admin/*"): no authentication scheme is wired yet, so this policy currently allows
// all callers — the attributes + policy are the seam. Flip to `policy.RequireRole("admin")` (+ AddAuthentication)
// when auth lands; nothing else changes.
builder.Services.AddAuthorization(options =>
    options.AddPolicy("Admin", policy => policy.RequireAssertion(_ => true)));

var conn = builder.Configuration.GetConnectionString("Default") ?? DbDefaults.DevConnectionString;

// Query side: persistence only — no crawling/matching services (D19). Never migrates (D5), never crawls.
builder.Services.AddPersistence(conn);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(WebCorsPolicy);
app.UseAuthorization();
app.MapControllers();

app.Run();

// Exposes the implicit Program class to the integration-test project (WebApplicationFactory<Program>).
public partial class Program;
