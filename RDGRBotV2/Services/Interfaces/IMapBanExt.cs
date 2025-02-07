namespace RDGRBotV2.Services.Interfaces
{
    public interface IMapBanExt
    {
        List<string?>? GenerateMapListBo3(bool OvO, List<string> team1Bans, List<string> team2Bans);
        List<string?>? GenerateMapListBo5(bool OvO, List<string> team1bans, List<string> team2bans);
    }
}