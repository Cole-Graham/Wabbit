using Newtonsoft.Json;
using RDGRBotV2.Models;

namespace RDGRBotV2.Data
{
    public static class Maps
    {
        public static List<Map>? MapCollection { get; set; }

        public static async Task LoadMaps()
        {
            string mapsFile = "Maps.json";

            async static Task CreateMapFile()
            {
                MapCollection =
                [
                    new Map
                    {
                        Name = string.Empty,
                        Id = null,
                        Thumbnail = null,
                        Size = "1v1",
                        IsInRandomPool = false
                    }
                ];

                await SaveMaps();
            }

            if (File.Exists(mapsFile))
            {
                string json = File.ReadAllText(mapsFile);
                if (!String.IsNullOrEmpty(json))
                {
                    MapCollection = JsonConvert.DeserializeObject<List<Map>>(json);
                    
                    if (MapCollection is null || MapCollection.Count == 0)
                    {
                        Console.WriteLine("Could not deserialize maps or collection was empty. Make sure you filled all the values");
                        Environment.Exit(1);
                    }
                }
                else
                {
                    Console.WriteLine("Could not read Maps.json. Recreating the file");
                    await CreateMapFile();
                }
            }
            else
            {
                Console.WriteLine("File Maps.json was not found and has been created. Fill the file and try again");
                await CreateMapFile();
                Environment.Exit(1);
            }
        }

        public static async Task<(bool, string?)> SaveMaps()
        {
            try
            {
                string json = JsonConvert.SerializeObject(MapCollection, Formatting.Indented);
                await File.WriteAllTextAsync("Maps.json", json);
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
