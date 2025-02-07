using Newtonsoft.Json;

namespace RDGRBotV2.BotClient.Config
{
    public static class ConfigManager
    {
        public static BotConfig Config { get; set; } = new();

        public static async Task ReadConfig()
        {
            static async Task SetToken(bool create)
            {
                string message;
                if (create)
                    message = "Configuration file was not found. Please enter Bot Token or press enter to close the application";
                else
                    message = "Token was not found inside configuration file.  Please enter Bot Token or press enter to close the application";
                Console.WriteLine(message);
                Console.WriteLine("Bot token:");
                string? token = Console.ReadLine();

                if (String.IsNullOrEmpty(token))
                {
                    Console.WriteLine("Entered an invalid value. Please relaunch the application and try again");
                    Environment.Exit(0);
                }

                Console.WriteLine("Enter Discord ID of the server where the bot will be deployed or press enter to continue");
                string? serverId = Console.ReadLine();
                ulong guildId = 0;
                if (!String.IsNullOrEmpty(serverId))
                    _ = (ulong.TryParse(serverId, out guildId)) ? guildId : 0;

                Config ??= new(); // Might set to null during deserialization

                Config.Token = token;
                if (guildId > 0)
                {
                    Config.Servers.Add(new BotConfig.ServerConfig
                    {
                        ServerId = guildId
                    });
                }

                await SaveConfig();
            }

            string configFile = "ConfigFile.json";
            if (File.Exists(configFile))
            {
                string? json = await File.ReadAllTextAsync(configFile);
                if (String.IsNullOrEmpty(json))
                {
                    await SetToken(false);
                    return;
                }

                Config = JsonConvert.DeserializeObject<BotConfig>(json);
                if (Config is null || String.IsNullOrEmpty(Config.Token))
                {
                    await SetToken(false);
                    return;
                }
            }
            else
                await SetToken(true);
        }

        public static async Task SaveConfig()
        {
            string json = JsonConvert.SerializeObject(Config, Formatting.Indented);
            await File.WriteAllTextAsync("ConfigFile.json", json);
        }

    }
}
