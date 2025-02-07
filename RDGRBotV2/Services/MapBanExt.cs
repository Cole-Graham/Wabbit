using RDGRBotV2.Data;
using RDGRBotV2.Services.Interfaces;

namespace RDGRBotV2.Services
{
    public class MapBanExt(IRandomProvider random) : IMapBanExt
    {
        private readonly Random _random = random.Instance;

        public List<string?>? GenerateMapListBo3(bool OvO, List<string> team1Bans, List<string> team2Bans)
        {
            if (Maps.MapCollection is null || Maps.MapCollection.Count == 0)
            {
                Console.WriteLine("Map collection is empty");
                return null;
            }

            List<string?> mapList;
            if (OvO)
                mapList = Maps.MapCollection.Where(m => m.Size == "1v1").Select(m => m.Name).ToList();
            else
                mapList = Maps.MapCollection.Where(m => m.Size == "2v2").Select(m => m.Name).ToList();
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
            var shuffled = mapList.OrderBy(_ => _random.Next()).ToList();
            return shuffled;
        }

        public List<string?>? GenerateMapListBo5(bool OvO, List<string> team1bans, List<string> team2bans)
        {
            if (Maps.MapCollection is null ||  Maps.MapCollection.Count == 0)
            {
                Console.WriteLine("Map collection is empty");
                return null;
            }

            List<string> randomMaps = [];
            int rMIndex;

            List<string?> mapList;
            if (OvO)
                mapList = Maps.MapCollection.Where(m => m.Size == "1v1").Select(m => m.Name).ToList();
            else
                mapList = Maps.MapCollection.Where(m => m.Size == "2v2").Select(m => m.Name).ToList();

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
            
            var shuffled = mapList.OrderBy(_ => _random.Next()).ToList();
            return shuffled;
        }
    }
}
