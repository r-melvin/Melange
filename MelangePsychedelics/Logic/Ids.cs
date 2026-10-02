namespace Melange.Psychedelics
{
    /// <summary>
    /// Every stable ID the spoke puts into a save. They never change once released: the game and S1API find items, products and
    /// recipes by these strings when a save loads. Products and kinds are namespaced because S1API requires it; plain items
    /// follow the game's lower-case style with a prefix so they can't collide with another mod's.
    /// </summary>
    public static class Ids
    {
        public const string Owner = "melange.psy";

        // products (S1API generic products, compatibility type Shrooms)
        public const string ToadKind = "melange.psy:toad";
        public const string LsdKind = "melange.psy:lsd";
        public const string ToadProduct = "melange.psy:products/toad";
        public const string LsdProduct = "melange.psy:products/lsd";
        public const string ProductSaveProvider = "melange.psy:products";

        // toad line
        public const string LiveToad = "melange_psy_live_toad";
        public const string Crickets = "melange_psy_crickets";
        public const string ToadNet = "melange_psy_toad_net";
        public const string Terrarium = "melange_psy_terrarium";

        // LSD line
        public const string ErgotSpores = "melange_psy_ergot_spores";
        public const string ErgotSpawn = "melange_psy_ergot_spawn";
        public const string Ergot = "melange_psy_ergot";
        public const string Reagent = "melange_psy_reagent";
        public const string BlankSheet = "melange_psy_blank_sheet";
        public const string LsdSolution = "melange_psy_lsd_solution";
        public const string BlotterFrame = "melange_psy_blotter_frame";
        public const string SolutionRecipe = "melange.psy:lsd-solution";

        // the LSD chemist (S1API supplier NPC): her ID is save data, never rename it
        public const string AnaNpc = "melange_psy_ana_slughin";
        // the wildlife officer at the pond (S1API NPC): save data too, never rename it
        public const string WardenNpc = "melange_psy_wildlife_warden";

        /// <summary>Tabs on one sheet: the game's brick holds 20 units (BrickPress consumes 20 of a product).</summary>
        public const int TabsPerSheet = 20;
    }
}
