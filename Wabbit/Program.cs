﻿using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Entities;
using Microsoft.Extensions.DependencyInjection;
using Wabbit.BotClient.Commands;
using Wabbit.BotClient.Config;
using Wabbit.BotClient.Events;
using Wabbit.Misc;
using Wabbit.Data;
using Wabbit.Services;
using Wabbit.Services.Interfaces;

namespace Wabbit
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                await ConfigManager.ReadConfig();

                if (ConfigManager.Config is null || ConfigManager.Config.Token is null)
                {
                    Console.WriteLine("Token is not obtained, application will be closed");
                    Environment.Exit(0);
                }

                await Maps.LoadMaps();

                DiscordClientBuilder builder = DiscordClientBuilder.CreateDefault(ConfigManager.Config.Token, DiscordIntents.All);

                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IRandomProvider, RandomProvider>();
                    services.AddSingleton<IMapBanExt, MapBanExt>();
                    services.AddSingleton<IRandomMapExt, RandomMapExt>();

                    services.AddSingleton<OngoingRounds>();


                });

                builder.ConfigureEventHandlers(events =>
                {
                    events.AddEventHandlers<Event_Button>(ServiceLifetime.Singleton);
                    events.AddEventHandlers<Event_Modal>(ServiceLifetime.Singleton);
                });

                builder.UseCommands((IServiceProvider serviceProvider, CommandsExtension extension) =>
                {
                    extension.AddCommands([typeof(BasicGroup), typeof(ConfigGroup), typeof(TournamentGroup), typeof(MapManagementGroup)]);
                }, new CommandsConfiguration()
                {
                    DebugGuildId = ConfigManager.Config.Servers?.FirstOrDefault()?.ServerId ?? 0
                });

                DiscordClient client = builder.Build();

                await client.ConnectAsync(status: DiscordUserStatus.Online);

                await Task.Delay(-1);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}
