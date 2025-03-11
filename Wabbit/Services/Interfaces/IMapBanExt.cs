namespace Wabbit.Services.Interfaces
{
    public interface IMapBanExt
    {
        List<string>? GenerateMapListBo1(bool OvO, List<string> team1Bans, List<string> team2Bans, List<string>? customMapPool = null);
        List<string>? GenerateMapListBo3(bool OvO, List<string> team1Bans, List<string> team2Bans, List<string>? customMapPool = null);
        List<string>? GenerateMapListBo5(bool OvO, List<string> team1bans, List<string> team2bans, List<string>? customMapPool = null);
    }
}