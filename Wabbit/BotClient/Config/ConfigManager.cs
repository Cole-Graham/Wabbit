using Newtonsoft.Json;
using System.IO;

namespace Wabbit.BotClient.Config
{
    public static class ConfigManager
    {
        // Local environment file for storing secrets
        // Note: Place this file in the same directory as ConfigFile.json (Wabbit/Wabbit/)
        private const string ENV_FILE_PATH = ".env";
        private const string BOT_TOKEN_KEY = "WABBIT_BOT_TOKEN";

        private static Dictionary<string, string> _envVariables = new();

        public static BotConfig Config { get; set; } = new();

        public static async Task ReadConfig()
        {
            try
            {
                // Try to load from .env file if it exists
                try
                {
                    LoadEnvironmentFile();

                    // Check for token in environment file
                    if (_envVariables.TryGetValue(BOT_TOKEN_KEY, out string? envToken) && !string.IsNullOrEmpty(envToken))
                    {
                        Console.WriteLine($"Using Discord bot token from {ENV_FILE_PATH} file");
                        Config.Token = envToken;

                        // Still load the rest of the config from regular config file
                        await LoadConfigWithoutToken();
                        return;
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    Console.WriteLine($"ERROR: Cannot access {ENV_FILE_PATH} file due to permissions: {ex.Message}");
                    Console.WriteLine("If you're running as a service or with nohup, ensure the correct permissions:");
                    Console.WriteLine($"  chmod 644 {ENV_FILE_PATH}");
                    Console.WriteLine($"  chown <service-user>:<service-group> {ENV_FILE_PATH}");

                    // If running in headless mode, cannot continue without the .env file
                    if (Environment.UserInteractive == false || Console.IsInputRedirected)
                    {
                        Console.WriteLine("Running in non-interactive mode, cannot prompt for token.");
                        Console.WriteLine("Please fix the .env file permissions and restart the application.");
                        Environment.Exit(1);
                    }
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"ERROR: IO error accessing {ENV_FILE_PATH} file: {ex.Message}");

                    // If running in headless mode, cannot continue without the .env file
                    if (Environment.UserInteractive == false || Console.IsInputRedirected)
                    {
                        Console.WriteLine("Running in non-interactive mode, cannot prompt for token.");
                        Console.WriteLine("Please fix the .env file issues and restart the application.");
                        Environment.Exit(1);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: Problem with {ENV_FILE_PATH} file: {ex.Message}");

                    // If running in headless mode, cannot continue without the .env file
                    if (Environment.UserInteractive == false || Console.IsInputRedirected)
                    {
                        Console.WriteLine("Running in non-interactive mode, cannot prompt for token.");
                        Console.WriteLine("Please create a valid .env file and restart the application.");
                        Environment.Exit(1);
                    }
                }

                // Fall back to asking for input and saving to .env file
                // This will only run in interactive mode
                await LoadFullConfig();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal error in ReadConfig: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Environment.Exit(1);
            }
        }

        private static void LoadEnvironmentFile()
        {
            _envVariables.Clear();
            if (!File.Exists(ENV_FILE_PATH))
            {
                return;
            }

            foreach (var line in File.ReadAllLines(ENV_FILE_PATH))
            {
                // Skip comments and empty lines
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    // Remove quotes if they exist
                    if (value.StartsWith("\"") && value.EndsWith("\""))
                        value = value.Substring(1, value.Length - 2);

                    _envVariables[key] = value;
                }
            }
        }

        private static void SaveEnvironmentVariable(string key, string value)
        {
            _envVariables[key] = value;

            try
            {
                // Build the file content
                var lines = new List<string>();
                foreach (var entry in _envVariables)
                {
                    lines.Add($"{entry.Key}=\"{entry.Value}\"");
                }

                // Write the file
                File.WriteAllLines(ENV_FILE_PATH, lines);

                // Ensure .env is in .gitignore
                EnsureGitIgnore();
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"Error: Cannot write to {ENV_FILE_PATH} due to permissions: {ex.Message}");
                Console.WriteLine($"You may need to run the following commands to set permissions:");
                Console.WriteLine($"  touch {ENV_FILE_PATH}");
                Console.WriteLine($"  chmod 600 {ENV_FILE_PATH}");
                Console.WriteLine($"  echo '{BOT_TOKEN_KEY}=\"your-token-here\"' > {ENV_FILE_PATH}");

                // On error, still store in memory for this session
                Config.Token = value;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing {ENV_FILE_PATH} file: {ex.Message}");

                // On error, still store in memory for this session
                Config.Token = value;
            }
        }

        private static void EnsureGitIgnore()
        {
            const string gitIgnorePath = ".gitignore";

            try
            {
                var gitIgnoreLines = File.Exists(gitIgnorePath)
                    ? File.ReadAllLines(gitIgnorePath).ToList()
                    : new List<string>();

                if (!gitIgnoreLines.Any(line => line.Trim() == ENV_FILE_PATH))
                {
                    gitIgnoreLines.Add(ENV_FILE_PATH);
                    File.WriteAllLines(gitIgnorePath, gitIgnoreLines);
                    Console.WriteLine($"Added {ENV_FILE_PATH} to .gitignore");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not update .gitignore: {ex.Message}");
            }
        }

        private static async Task LoadConfigWithoutToken()
        {
            string configFile = "ConfigFile.json";
            if (File.Exists(configFile))
            {
                string? json = await File.ReadAllTextAsync(configFile);
                if (!String.IsNullOrEmpty(json))
                {
                    var fileConfig = JsonConvert.DeserializeObject<BotConfig>(json) ?? new();
                    // Copy everything except the token (which we already have from env file)
                    Config.Servers = fileConfig.Servers;
                }
            }
        }

        private static async Task LoadFullConfig()
        {
            static async Task SetToken(bool create)
            {
                string message;
                if (create)
                    message = "Configuration file was not found. Please enter Bot Token or press enter to close the application";
                else
                    message = "Token was not found. Please enter Bot Token or press enter to close the application";
                Console.WriteLine(message);
                Console.WriteLine($"The token will be stored in {ENV_FILE_PATH}");

                // Check if we're running in a headless/non-interactive environment
                try
                {
                    // Try to detect if we have a console available for input
                    if (!Console.IsInputRedirected && !Console.IsOutputRedirected)
                    {
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

                        // Store token in .env file (will handle errors internally)
                        SaveEnvironmentVariable(BOT_TOKEN_KEY, token);
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
                    else
                    {
                        // Running in headless mode, cannot prompt for input
                        Console.WriteLine("ERROR: Bot is running in non-interactive mode (probably with nohup or as a service)");
                        Console.WriteLine("Cannot prompt for token input. Please create a .env file manually:");
                        Console.WriteLine($"1. Create a file named {ENV_FILE_PATH} in the application directory");
                        Console.WriteLine($"2. Add this line to the file: {BOT_TOKEN_KEY}=\"your-token-here\"");
                        Console.WriteLine($"3. Ensure the application has permissions to read the file");
                        Console.WriteLine("Exiting application.");
                        Environment.Exit(1);
                    }
                }
                catch (InvalidOperationException)
                {
                    // This exception happens when Console.ReadLine is called in a process without a console
                    Console.WriteLine("ERROR: No console available for input (running as service or with redirected input)");
                    Console.WriteLine("Cannot prompt for token input. Please create a .env file manually:");
                    Console.WriteLine($"1. Create a file named {ENV_FILE_PATH} in the application directory");
                    Console.WriteLine($"2. Add this line to the file: {BOT_TOKEN_KEY}=\"your-token-here\"");
                    Console.WriteLine($"3. Ensure the application has permissions to read the file");
                    Console.WriteLine("Exiting application.");
                    Environment.Exit(1);
                }
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

                Config = JsonConvert.DeserializeObject<BotConfig>(json) ?? new();
                if (String.IsNullOrEmpty(Config.Token))
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
            // Create a copy of the config without the token for saving to JSON
            var configToSave = new BotConfig
            {
                // Never save token to config file
                Token = null,
                Servers = Config.Servers
            };

            string json = JsonConvert.SerializeObject(configToSave, Formatting.Indented);
            await File.WriteAllTextAsync("ConfigFile.json", json);
        }
    }
}
