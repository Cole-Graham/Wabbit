using Wabbit.Data;
using Wabbit.Services.Interfaces;

namespace Wabbit.Services
{
    public class MapBanExt(IRandomProvider random) : IMapBanExt
    {
        private readonly Random _random = random.Instance;

        public List<string>? GenerateMapListBo1(bool OvO, List<string> team1Bans, List<string> team2Bans, List<string>? customMapPool = null)
        {
            // Get the map pool
            List<string>? mapList = GetMapPool(OvO, customMapPool);
            if (mapList == null || mapList.Count == 0)
            {
                Console.WriteLine("Map collection is empty");
                return null;
            }

            // For Bo1, we just need to remove all banned maps and pick one
            var allBans = team1Bans.Concat(team2Bans).Distinct().ToList();
            foreach (var map in allBans)
            {
                mapList.Remove(map);
            }

            // If we have no maps left, return null
            if (mapList.Count == 0)
            {
                Console.WriteLine("No maps left after bans");
                return null;
            }

            // Pick one random map
            var shuffled = mapList.OrderBy(_ => _random.Next()).Take(1).ToList();
            return shuffled;
        }

        public List<string>? GenerateMapListBo3(bool OvO, List<string> team1Bans, List<string> team2Bans, List<string>? customMapPool = null)
        {
            // Get the map pool
            List<string>? mapList = GetMapPool(OvO, customMapPool);
            if (mapList == null || mapList.Count == 0)
            {
                Console.WriteLine("Map collection is empty");
                return null;
            }

            string[] mtrArray; // Maps to remove

            var same = team1Bans.Intersect(team2Bans);
            switch (same.Count())
            {
                default:
                    mtrArray = [team1Bans[0], team1Bans[1], team2Bans[0], team2Bans[1]];
                    foreach (var map in mtrArray)
                    {
                        mapList.Remove(map);
                    }
                    break;
                case 1:
                    var sameMap = same.First();

                    var sameT1Index = team1Bans.IndexOf(sameMap);
                    var sameT2Index = team2Bans.IndexOf(sameMap);

                    if (sameT1Index < 2 && sameT2Index < 2)
                    {
                        string[] thirds = [team1Bans[2], team2Bans[2]];
                        string rndm3rdMap = thirds[_random.Next(thirds.Length)];

                        List<string> bans = team1Bans.Concat(team2Bans).ToList();
                        bans.Remove(sameMap);
                        bans.Remove(rndm3rdMap);

                        mapList = mapList.Except(bans).ToList();
                    }
                    else
                    {
                        mtrArray = [team1Bans[0], team1Bans[1], team2Bans[0], team2Bans[1]];
                        foreach (var map in mtrArray)
                            mapList.Remove(map);
                    }
                    break;
                case 2:
                    mtrArray = [same.First(), same.Last()];

                    foreach (var map in mtrArray)
                    {
                        team1Bans.Remove(map);
                        team2Bans.Remove(map);
                        mapList.Remove(map);
                    }

                    mapList.Remove(team1Bans.First()); // 1 left in each
                    mapList.Remove(team2Bans.First());
                    break;
                case 3:
                    foreach (var map in team1Bans)
                        mapList.Remove(map);
                    break;
            }
            var shuffled = mapList.OrderBy(_ => _random.Next()).Take(3).ToList();
            return shuffled;
        }

        public List<string>? GenerateMapListBo5(bool OvO, List<string> team1bans, List<string> team2bans, List<string>? customMapPool = null)
        {
            // Get the map pool
            List<string>? mapList = GetMapPool(OvO, customMapPool);
            if (mapList == null || mapList.Count == 0)
            {
                Console.WriteLine("Map collection is empty");
                return null;
            }

            List<string> randomMaps = [];
            int rMIndex;

            List<string> mtrList = [];

            var same = team1bans.Intersect(team2bans);
            switch (same.Count())
            {
                case 0:
                    mtrList.Add(team1bans[0]);
                    mtrList.Add(team2bans[0]);

                    foreach (var map in mtrList)
                        mapList.Remove(map);

                    break;
                case 1:
                    var sameMap = same.First();
                    team1bans.Remove(sameMap);
                    team2bans.Remove(sameMap);
                    mtrList.Add(sameMap);

                    randomMaps.Add(team1bans.First());
                    randomMaps.Add(team2bans.First());

                    rMIndex = _random.Next(randomMaps.Count - 1);
                    mtrList.Add(randomMaps[rMIndex]);

                    foreach (var map in mtrList)
                        mapList.Remove(map);

                    break;
                case 2:
                    foreach (var map in same)
                        mapList.Remove(map);

                    break;
            }

            var shuffled = mapList.OrderBy(_ => _random.Next()).Take(5).ToList();
            return shuffled;
        }

        private List<string>? GetMapPool(bool OvO, List<string>? customMapPool = null)
        {
            // If a custom map pool is provided, use it
            if (customMapPool != null && customMapPool.Count > 0)
            {
                return new List<string>(customMapPool);
            }

            // Otherwise, use the default map pool from Maps.MapCollection
            if (Maps.MapCollection is null || Maps.MapCollection.Count == 0)
            {
                Console.WriteLine("Map collection is empty");
                return null;
            }

            List<string>? mapList;
            if (OvO)
                mapList = Maps.MapCollection.Where(m => m.Size == "1v1" && m.IsInTournamentPool).Select(m => m.Name).ToList();
            else
                mapList = Maps.MapCollection.Where(m => m.Size == "2v2" && m.IsInTournamentPool).Select(m => m.Name).ToList();

            return mapList;
        }
    }
}
