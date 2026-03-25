using PrintSvc;
using PrintSvc.Settings;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((hostingContext, config) =>
    {
        // On vide parfois les sources par défaut ou on ajoute simplement la nôtre
        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
              .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true) // Votre fichier
              .AddEnvironmentVariables();
    })
    .ConfigureServices((hostContext, services) =>
    {
        var configuration = hostContext.Configuration;

        // Liaison des sections aux classes POCO
        services.Configure<BrokerSettings>(configuration.GetSection("Broker"));
        services.Configure<StorageSettings>(configuration.GetSection("Storage"));
        services.Configure<PrintingSettings>(configuration.GetSection("Printing"));

        services.AddHostedService<Worker>();
    })
    .Build();

await host.RunAsync();
