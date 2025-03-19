using System.ComponentModel.DataAnnotations;

namespace Wabbit.Models
{
    /// <summary>
    /// Base game type enum that will be used to generate specialized enums
    /// </summary>
    [GenerateSpecializedEnums("Scrimmage", "Signup", "Team", "Tournament")]
    public enum GameType
    {
        /// <summary>
        /// 1v1 game type (1 player per team)
        /// </summary>
        [Display(Name = "1v1")]
        OneVOne,

        /// <summary>
        /// 2v2 game type (2 players per team)
        /// </summary>
        [Display(Name = "2v2")]
        TwoVTwo,

        /// <summary>
        /// 3v3 game type (3 players per team)
        /// </summary>
        [Display(Name = "3v3")]
        ThreeVThree,

        /// <summary>
        /// 4v4 game type (4 players per team)
        /// </summary>
        [Display(Name = "4v4")]
        FourVFour
    }
}