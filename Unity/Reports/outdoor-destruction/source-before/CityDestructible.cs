using UnityEngine;

namespace OpenTerritory
{
    // Authored props remain static until damaged. No per-prop Update or Rigidbody.
    public sealed class CityDestructible : MonoBehaviour
    {
        public string category = "furniture";
        public float durability = 55;
        public int nodeId, place = -1;
        public Renderer[] visuals = new Renderer[0];
        public Collider[] shapes = new Collider[0];
        public bool Broken { get; private set; }
        public float Health { get; private set; }
        public CityPropDamageKind LastDamage { get; private set; }
        internal CityDestruction owner;
        internal float lastVehicleHit = -100;
        internal Rigidbody breakingVehicle;
        internal float vehicleBreakStep = float.NegativeInfinity;

        internal void Bind(CityDestruction service)
        {
            owner = service; Health = durability; Broken = false;
            lastVehicleHit = -100; breakingVehicle = null; vehicleBreakStep = float.NegativeInfinity;
            if (visuals.Length == 0) visuals = GetComponentsInChildren<Renderer>(true);
            if (shapes.Length == 0) shapes = GetComponentsInChildren<Collider>(true);
        }
        public bool Apply(float damage, Vector3 point, Vector3 impulse, CityPropDamageKind kind, bool playerCaused = true)
        {
            if (!owner || Broken || !gameObject.activeInHierarchy || !owner.CanDamage || damage <= 0) return false;
            Health = Mathf.Max(0, Health - damage); LastDamage = kind;
            if (Health <= 0) { Broken = true; owner.Shatter(this, point, impulse, playerCaused); }
            else owner.MarkDamage(this, point, playerCaused);
            return true;
        }
    }
    public enum CityPropDamageKind { Bullet, Melee, Vehicle }
}
