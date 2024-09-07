using LiteDB;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using Sphere.Client.Services;
using Sphere.Common.Interfaces;
using Sphere.Common.Interfaces.Nodes;
using Sphere.Common.Interfaces.Providers;
using Sphere.Common.Interfaces.Readers;
using Sphere.Common.Interfaces.Services;
using Sphere.Common.Interfaces.Tcp;
using Sphere.Common.Packets;
using Sphere.Godot.Configuration.Options;
using Sphere.Repository.Configuration;
using Sphere.Services.Misc;
using Sphere.Services.Providers;
using Sphere.Services.Readers;
using Sphere.Services.Services.Handlers;
using System;

namespace Sphere.Godot.Configuration
{
    public static class DependencyInjection
    {
        public static IServiceCollection RegisterCommon(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.SetMinimumLevel(LogLevel.Information);
                builder.AddNLog("nlog.config");
            });

            services.AddSingleton(configuration);
            services.ConfigureOptions(configuration);
            services.AddSingleton<ILiteDatabase>(x => new LiteDatabase(configuration.GetConnectionString("litedb")));

            return services;
        }

        public static IServiceCollection RegisterServices(this IServiceCollection services)
        {
            services.AddSingleton<IServer, Nodes.Server>();
            services.AddSingleton<ILocalIdProvider, LocalIdProvider>();
            services.AddSingleton<IPacketParser, PacketParser>();
            services.AddSingleton<IIdentifierProvider<Guid>, GuidIdentifierProvider>();

            services.AddScoped<IClient, Nodes.Client>();
            services.AddScoped<ITcpClientAccessor, TcpClientAccessor>();
            services.AddScoped<IPacketReader, SpherePacketReader>();
            services.AddScoped<IPacketHandler, PacketHandlerBase>();
            services.AddScoped<IPacketHandler<LoginPacket>, LoginPacketHandler>();
            services.AddScoped<IPacketHandler<ClientPingPacketShort>, ClientPingPacketShortHandler>();
            services.AddScoped<IPacketHandler<ClientPingPacketLong>, ClientPingPacketLongHandler>();
            services.AddScoped<IPacketHandler<CharacterCreatePacket>, CharacterCreatePacketHandler>();

            services.RegisterRepositories();

            return services;
        }

        

        private static void ConfigureOptions(this IServiceCollection services, IConfiguration configuration)
        {
            var sphereSection = configuration.GetSection("Sphere");

            services.Configure<ServerConfiguration>(sphereSection.GetSection("Server"));
        }
    }
}
