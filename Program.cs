using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using QualMaggie.Scanner.Models;
using QualMaggie.Scanner.Services;

var builder = Host.CreateApplicationBuilder(args);

// Load configuration (appsettings.json is automatically loaded)
var config = builder.Configuration;

// Bind scanner settings
builder.Services.Configure<QualMaggieSettings>(
    config.GetSection("QualMaggieSettings"));

// Bind result directory settings
builder.Services.Configure<ResultDirectorySettings>(
    config.GetSection("ResultDirectory"));

// Connection string
string connectionString = config.GetConnectionString("DefaultConnection")
    ?? throw new Exception("Connection string is missing in appsettings.json");

// Register rules (DI will inject IOptions<QualMaggieSettings>)
builder.Services.AddSingleton<QualMaggieRules>();

// Register scanner service with DI
builder.Services.AddSingleton<ScannerService>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<QualMaggieSettings>>();
    var rules = sp.GetRequiredService<QualMaggieRules>();
    var resultDirSettings = sp.GetRequiredService<IOptions<ResultDirectorySettings>>();
    return new ScannerService(settings, rules, connectionString, resultDirSettings);
});

Console.WriteLine("Starting QualMaggie Scanner...");

// Build + run
var app = builder.Build();

var scanner = app.Services.GetRequiredService<ScannerService>();
await scanner.RunAsync();
