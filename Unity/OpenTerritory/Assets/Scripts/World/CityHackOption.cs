namespace OpenTerritory
{
    public readonly struct CityHackOption
    {
        public readonly string Id, Label, Effect;
        public readonly bool Available;
        public readonly float Cooldown;
        public CityHackOption(string id, string label, string effect, float cooldown = 1, bool available = true)
        { Id = id; Label = label; Effect = effect; Cooldown = cooldown; Available = available; }
    }
}
