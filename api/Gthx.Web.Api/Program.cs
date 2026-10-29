using Gthx.Data;
using Gthx.Web.Api;
using GthxData;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// In Azure this signs in with the app's managed identity, whose database user only has db_datareader.
var connectionString = builder.Configuration.GetConnectionString("GthxDb")
    ?? throw new InvalidOperationException("The connection string ConnectionStrings:GthxDb is not set.");

// The admin panel's changes sign in as a second managed identity, which can change factoids. Only
// IGthxAdminData gets this context, so the public functions can't write even by mistake.
var adminConnectionString = builder.Configuration.GetConnectionString("GthxAdminDb")
    ?? throw new InvalidOperationException("The connection string ConnectionStrings:GthxAdminDb is not set.");
var adminDbOptions = new DbContextOptionsBuilder<GthxDataContext>().UseSqlServer(adminConnectionString).Options;

builder.Services.AddDbContext<GthxDataContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddScoped<IGthxWebData, GthxWebData>();
builder.Services.AddKeyedScoped("admin", (_, _) => new GthxDataContext(adminDbOptions));
builder.Services.AddScoped<IGthxAdminData>(services => new GthxAdminData(services.GetRequiredKeyedService<GthxDataContext>("admin")));
builder.Services.AddMemoryCache();

// App Service sets WEBSITE_AUTH_ENABLED when its authentication is turned on (infra/main.bicep does that).
// Admin:TrustPrincipalHeaders is only for running locally, where there's no Easy Auth; never set it in Azure.
var allowedAdmins = (builder.Configuration["Admin:AllowedObjectIds"] ?? "")
    .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddSingleton(new AdminOptions(
    new HashSet<string>(allowedAdmins, StringComparer.OrdinalIgnoreCase),
    TrustPrincipalHeaders: string.Equals(builder.Configuration["WEBSITE_AUTH_ENABLED"], "true", StringComparison.OrdinalIgnoreCase)
        || builder.Configuration.GetValue<bool>("Admin:TrustPrincipalHeaders")));

builder.Build().Run();
