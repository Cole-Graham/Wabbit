using DSharpPlus;
using DSharpPlus.Commands;
using DSharpPlus.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Wabbit.BotClient.Commands;
using Wabbit.BotClient.Config;
using Wabbit.BotClient.Events;
using Wabbit.BotClient.Events.Components.Base;
using Wabbit.BotClient.Events.Components.Factory;
using Wabbit.BotClient.Events.Components.Tournament;
using Wabbit.BotClient.Events.MainHandlers;
using Wabbit.BotClient.Events.Modals.Base;
using Wabbit.BotClient.Events.Modals.Factory;
using Wabbit.BotClient.Events.Modals.Tournament;
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
                // Configure Serilog
                string logsPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
                Directory.CreateDirectory(logsPath);

                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .MinimumLevel.Override("System", LogEventLevel.Warning)
                    .Enrich.FromLogContext()
                    .WriteTo.Console()
                    .WriteTo.File(
                        Path.Combine(logsPath, "wabbit-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 7,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                    .CreateLogger();

                Log.Information("Starting Wabbit Discord Bot");

                await ConfigManager.ReadConfig();

                if (ConfigManager.Config is null || ConfigManager.Config.Token is null)
                {
                    Log.Error("Discord bot token is not configured.");
                    Console.WriteLine("Discord bot token is not configured. Please either:");
                    Console.WriteLine("1. Create a .env file with WABBIT_BOT_TOKEN=\"your-token-here\"");
                    Console.WriteLine("2. Run the application again and enter the token when prompted (interactive mode only)");
                    Console.WriteLine();
                    Console.WriteLine("For headless/service usage (nohup, systemd, etc.):");
                    Console.WriteLine("  You MUST create the .env file manually before starting the service");
                    Console.WriteLine("  touch .env");
                    Console.WriteLine("  chmod 644 .env");
                    Console.WriteLine("  echo 'WABBIT_BOT_TOKEN=\"your-token-here\"' > .env");
                    Console.WriteLine("  chown <service-user>:<service-group> .env  # If running as a different user");
                    Environment.Exit(0);
                }

                await Maps.LoadMaps();

                // Create a shared OngoingRounds instance
                var ongoingRounds = new OngoingRounds();

                DiscordClientBuilder builder = DiscordClientBuilder.CreateDefault(ConfigManager.Config.Token, DiscordIntents.All);

                // Configure logging with Serilog
                // Set minimum log level to match our Serilog configuration
                builder.ConfigureLogging(logging =>
                {
                    // Add Serilog provider to DSharpPlus logging
                    logging.AddSerilog(Log.Logger);
                });

                builder.ConfigureServices(services =>
                {
                    services.AddSingleton(ongoingRounds);

                    // Register tournament services
                    services.AddSingleton<ITournamentRepositoryService, TournamentRepositoryService>();
                    services.AddSingleton<ITournamentSignupService, TournamentSignupService>();
                    services.AddSingleton<ITournamentGroupService, TournamentGroupService>();
                    services.AddSingleton<ITournamentPlayoffService, TournamentPlayoffService>();
                    services.AddSingleton<ITournamentStateService, TournamentStateService>();
                    services.AddSingleton<ITournamentMatchService, TournamentMatchService>();
                    services.AddSingleton<ITournamentGameService, TournamentGameService>();
                    services.AddSingleton<ITournamentMapService, TournamentMapService>();
                    services.AddSingleton<ITournamentService, TournamentService>();
                    services.AddSingleton<ITournamentManagerService, TournamentManagerService>();
                    services.AddSingleton<IMatchStatusService, MatchStatusService>();

                    // Register existing services
                    services.AddSingleton<IRandomProvider, RandomProvider>();
                    services.AddSingleton<IMapBanExt, MapBanExt>();
                    services.AddSingleton<IRandomMapExt, RandomMapExt>();
                    services.AddSingleton<TournamentManagementGroup>();

                    // Register component handler factory and handlers - New for Phase 1
                    services.AddSingleton<ComponentHandlerFactory>();
                    services.AddSingleton<DefaultComponentHandler>();

                    // Register specialized component handlers - New for Phase 1
                    services.AddSingleton<ComponentHandlerBase, DeckSubmissionHandler>();
                    services.AddSingleton<ComponentHandlerBase, GameResultHandler>();
                    services.AddSingleton<ComponentHandlerBase, MapBanHandler>();
                    services.AddSingleton<ComponentHandlerBase, TournamentSignupHandler>();
                    services.AddSingleton<ComponentHandlerBase, AdminThirdPlaceMatchHandler>();

                    // Register the new ComponentInteractionHandler (will replace Event_Button in Phase 2)
                    services.AddSingleton<ComponentInteractionHandler>();

                    // Register modal handler factory and handlers - New for Phase 3
                    services.AddSingleton<BotClient.Events.Modals.Factory.ModalHandlerFactory>();
                    services.AddSingleton<BotClient.Events.Modals.Tournament.DefaultModalHandler>();

                    // Register specialized modal handlers - New for Phase 3
                    services.AddSingleton<BotClient.Events.Modals.Base.ModalHandlerBase, BotClient.Events.Modals.Tournament.DeckSubmissionModalHandler>();
                    services.AddSingleton<BotClient.Events.Modals.Base.ModalHandlerBase, BotClient.Events.Modals.Tournament.TournamentCreationModalHandler>();
                    services.AddSingleton<BotClient.Events.Modals.Base.ModalHandlerBase, BotClient.Events.Modals.Tournament.MapBanModalHandler>();

                    // Register the new ModalInteractionHandler - New for Phase 3
                    services.AddSingleton<ModalInteractionHandler>();
                });

                builder.ConfigureEventHandlers(events =>
                {
                    // Comment out the old Event_Button registration
                    // events.AddEventHandlers<Event_Button>(ServiceLifetime.Singleton);

                    // Register the new ComponentInteractionHandler
                    events.AddEventHandlers<ComponentInteractionHandler>(ServiceLifetime.Singleton);

                    // Register the new ModalInteractionHandler and comment out Event_Modal
                    events.AddEventHandlers<ModalInteractionHandler>(ServiceLifetime.Singleton);
                    // events.AddEventHandlers<Event_Modal>(ServiceLifetime.Singleton);

                    // Keep the other event handlers
                    events.AddEventHandlers<Event_MessageCreated>(ServiceLifetime.Singleton);
                });

                builder.UseCommands((IServiceProvider serviceProvider, CommandsExtension extension) =>
                {
                    extension.AddCommands([
                        typeof(BasicGroup),
                        typeof(ConfigGroup),
                        typeof(TournamentGroup),
                        typeof(MapManagementGroup),
                        typeof(TournamentManagementGroup)
                    ]);
                }, new CommandsConfiguration()
                {
                    DebugGuildId = ConfigManager.Config.Servers?.FirstOrDefault()?.ServerId ?? 0
                });

                // Create Images directory if it doesn't exist
                Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "Images"));

                DiscordClient client = builder.Build();

                await client.ConnectAsync(status: DiscordUserStatus.Online);

                // Load tournament state using the state service
                var stateService = client.ServiceProvider.GetRequiredService<ITournamentStateService>();
                stateService.LoadTournamentState();

                // Load tournament participants
                var tournamentManagerService = client.ServiceProvider.GetRequiredService<ITournamentManagerService>();
                await tournamentManagerService.LoadAllParticipantsAsync(client);

                await Task.Delay(-1);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Host terminated unexpectedly");
            }
            finally
            {
                // Close and flush the log
                Log.CloseAndFlush();
            }
        }
    }
}
