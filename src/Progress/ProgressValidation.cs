namespace VoidCrewProgressEditor
{
    public static class ProgressValidation
    {
        public static string ValidateFavor(int rank, int favorRank)
        {
            if (favorRank < 0) return "Favor rank must be non-negative; nothing changed.";
            if (rank != 30 && favorRank > 0)
                return "Favor rank must be 0 unless rank is 30; nothing changed.";
            return null;
        }

        public static string Validate(long xp, int rank, long maximumXp)
        {
            if (rank < 0 || rank > 30) return "Rank must be between 0 and 30; nothing changed.";
            if (xp < 0) return "XP must be non-negative; nothing changed.";
            if (maximumXp <= 0) return "The game's XP threshold is unavailable; nothing changed.";
            if (xp >= maximumXp)
                return "XP must be less than " + maximumXp + " for rank " + rank + "; nothing changed.";
            return null;
        }
    }
}
