using System.Collections.Generic;
using UnityEngine;

namespace OpenTerritory
{
    public sealed partial class CityLighting
    {
        sealed class BlackoutCircuit { public Vector3 center; public int place; public float until; }
        readonly List<BlackoutCircuit> blackouts = new List<BlackoutCircuit>();
        float lastHackDarkness;
        const float CircuitRadius = 24;
        void ExpireBlackouts()
        { for (int i = blackouts.Count - 1; i >= 0; i--) if (ctx.Clock >= blackouts[i].until) blackouts.RemoveAt(i); }
        bool RoomCircuitOff(int place)
        { foreach (var circuit in blackouts) if (circuit.place == place && ctx.Clock < circuit.until) return true; return false; }
        bool FixtureCircuitOff(Fixture fixture)
        {
            foreach (var circuit in blackouts)
            {
                if (ctx.Clock >= circuit.until) continue;
                if (fixture.place >= 0) { if (circuit.place == fixture.place) return true; }
                else if (circuit.place < 0 && fixture.source && CityPhysics.Flat(fixture.source.transform.position - circuit.center).sqrMagnitude <= CircuitRadius * CircuitRadius) return true;
            }
            return false;
        }
        public bool CircuitOff(int fixture) => fixture >= 0 && fixture < fixtures.Length && FixtureCircuitOff(fixtures[fixture]);
        public bool Blackout(int fixture, float seconds)
        {
            if (!ctx || ctx.Paused || fixture < 0 || fixture >= fixtures.Length || !fixtures[fixture].source) return false;
            ExpireBlackouts(); var target = fixtures[fixture];
            blackouts.Add(new BlackoutCircuit { center = target.source.transform.position, place = target.place, until = ctx.Clock + Mathf.Clamp(seconds, 1, 30) });
            Tick(lastHackDarkness); return true;
        }
        public bool RestoreCircuit(int fixture)
        {
            if (!ctx || ctx.Paused || fixture < 0 || fixture >= fixtures.Length || !fixtures[fixture].source) return false;
            var target = fixtures[fixture]; bool removed = false;
            for (int i = blackouts.Count - 1; i >= 0; i--)
            {
                var circuit = blackouts[i];
                if (target.place >= 0 ? circuit.place == target.place : circuit.place < 0 && CityPhysics.Flat(target.source.transform.position - circuit.center).sqrMagnitude <= CircuitRadius * CircuitRadius)
                { blackouts.RemoveAt(i); removed = true; }
            }
            Tick(lastHackDarkness); return removed;
        }
        public bool BlackoutAt(Vector3 point)
        {
            foreach (var circuit in blackouts)
            {
                if (ctx.Clock >= circuit.until) continue;
                if (circuit.place >= 0)
                { if (Inside(ctx.Data.places[circuit.place], point)) return true; }
                else if (CityPhysics.Flat(point - circuit.center).sqrMagnitude <= CircuitRadius * CircuitRadius && Mathf.Abs(point.y - circuit.center.y) < 15)
                {
                    bool indoors = false;
                    foreach (var place in ctx.Data.places) if (Inside(place, point)) { indoors = true; break; }
                    if (!indoors) return true;
                }
            }
            return false;
        }
    }
}
