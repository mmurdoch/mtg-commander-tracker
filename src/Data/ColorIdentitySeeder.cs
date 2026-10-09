using mtg_game_tracker.Models;

namespace mtg_game_tracker.Data
{
    // Fixed reference data for the 31 possible Commander color identities.
    public static class ColorIdentitySeeder
    {
        public static IReadOnlyList<ColorData> All { get; } = new List<ColorData>
        {
            new() { Id = 1, Name = "Mono White", Abbreviation = "W" },
            new() { Id = 2, Name = "Mono Blue", Abbreviation = "U" },
            new() { Id = 3, Name = "Mono Black", Abbreviation = "B" },
            new() { Id = 4, Name = "Mono Red", Abbreviation = "R" },
            new() { Id = 5, Name = "Mono Green", Abbreviation = "G" },
            new() { Id = 6, Name = "Azorius", Abbreviation = "WU" },
            new() { Id = 7, Name = "Dimir", Abbreviation = "UB" },
            new() { Id = 8, Name = "Rakdos", Abbreviation = "BR" },
            new() { Id = 9, Name = "Gruul", Abbreviation = "RG" },
            new() { Id = 10, Name = "Selesnya", Abbreviation = "GW" },
            new() { Id = 11, Name = "Orzhov", Abbreviation = "WB" },
            new() { Id = 12, Name = "Izzet", Abbreviation = "UR" },
            new() { Id = 13, Name = "Golgari", Abbreviation = "BG" },
            new() { Id = 14, Name = "Boros", Abbreviation = "RW" },
            new() { Id = 15, Name = "Simic", Abbreviation = "GU" },
            new() { Id = 16, Name = "Esper", Abbreviation = "WUB" },
            new() { Id = 17, Name = "Grixis", Abbreviation = "UBR" },
            new() { Id = 18, Name = "Jund", Abbreviation = "BRG" },
            new() { Id = 19, Name = "Naya", Abbreviation = "RGW" },
            new() { Id = 20, Name = "Bant", Abbreviation = "GWU" },
            new() { Id = 21, Name = "Abzan", Abbreviation = "WBG" },
            new() { Id = 22, Name = "Jeskai", Abbreviation = "URW" },
            new() { Id = 23, Name = "Sultai", Abbreviation = "BGU" },
            new() { Id = 24, Name = "Mardu", Abbreviation = "RWB" },
            new() { Id = 25, Name = "Temur", Abbreviation = "GUR" },
            new() { Id = 26, Name = "Yore-Tiller", Abbreviation = "WUBR" },
            new() { Id = 27, Name = "Glint-Eye", Abbreviation = "UBRG" },
            new() { Id = 28, Name = "Dune-Brood", Abbreviation = "BRGW" },
            new() { Id = 29, Name = "Ink-Treader", Abbreviation = "RGWU" },
            new() { Id = 30, Name = "Witch-Maw", Abbreviation = "GWUB" },
            new() { Id = 31, Name = "Five Colour", Abbreviation = "WUBRG" },
        };
    }
}
