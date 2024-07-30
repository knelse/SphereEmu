using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using Sphere.Common.Interfaces;
using Sphere.Godot.Nodes;
using Sphere.Server.Configuration.Options;
using System.IO;

namespace Sphere.Godot.Configuration.Services
{
    public static class DependencyInjection
    {
        public static IServiceCollection RegisterCommon(this IServiceCollection services)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
                builder.AddNLog("nlog.config");
            });

            services.ConfigureOptions(configuration);

            return services;
        }

        public static IServiceCollection RegisterServices(this IServiceCollection services)
        {
            services.AddSingleton<IServer, Nodes.Server>();

            services.AddScoped<IClient, ClientNode>();

            return services;
        }

        private static void ConfigureOptions(this IServiceCollection services, IConfiguration configuration)
        {
            var sphereSection = configuration.GetSection("Sphere");

            services.Configure<ServerConfiguration>(sphereSection.GetSection("Server"));
        }
    }
}
