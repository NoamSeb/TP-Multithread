namespace MiniHospital.Simulation
{
    public class HospitalStats
    {
        public int Arrived;
        public int Treated;
        public int Untreated;
        public int Active;

        public int Score;
        public int Combo;
        public int BestCombo;

        public void UpdateScore(bool treated)
        {
            if (treated)
            {
                // Combo++;
                Interlocked.Increment(ref Combo);
                Score += 100 * Combo;
                if (Combo > BestCombo)
                    BestCombo = Combo;
            }
            else
            {
                Combo = 0;
            }
        }
    }
}
