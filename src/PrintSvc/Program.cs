using Minio;
using PrintSvc;
using PrintSvc.Settings;
using PrintSvc.Storage;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true); // Votre fichier

var configuration = builder.Configuration;

builder.Services.Configure<BrokerSettings>(configuration.GetSection("Broker"));
builder.Services.Configure<StorageSettings>(configuration.GetSection("Storage"));
builder.Services.Configure<PrintingSettings>(configuration.GetSection("Printing"));

builder.Services.AddSingleton<IPhotoDownloader, PhotoDownloader>();


var storageConfig = builder.Configuration.GetSection("Storage").Get<StorageSettings>()
    ?? throw new InvalidOperationException("Storage configuration is required");


builder.Services.AddMinio(configureClient => configureClient
    .WithEndpoint(storageConfig.Endpoint)
    .WithCredentials(storageConfig.AccessKey, storageConfig.SecretKey)
    .WithSSL(storageConfig.UseSSL)
    .Build());



IHost host = builder.Build();

await host.RunAsync();
