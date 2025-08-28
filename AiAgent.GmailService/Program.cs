using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Reflection.PortableExecutable;

Host.CreateDefaultBuilder(args)
    .ConfigureWebHostDefaults(webBuilder =>
    {
        webBuilder.ConfigureServices((hostContext, services) =>
        {
            services.AddHttpClient();
            services.AddControllers();
            services.AddSingleton<GmailServiceAgent>(sp =>
            {
                var config = sp.GetRequiredService<IConfiguration>();
                return new GmailServiceAgent(
                    config["Gmail:ClientId"],
                    config["Gmail:ClientSecret"]
                );
            });
            services.AddHostedService<GmailPollingService>();
        });
        webBuilder.Configure((context, app) =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        });
    })
    .Build()
    .Run();
