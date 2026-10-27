using Gthx.Data;
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

builder.Services.AddDbContext<GthxDataContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddScoped<IGthxWebData, GthxWebData>();
builder.Services.AddMemoryCache();

builder.Build().Run();
