using UnityEngine;

namespace StormBreakers
{
    public readonly struct HullElement
    {
        public BoxCollider Collider { get; }
        public float Mass { get; }
        public float Buoyancy { get; }
        public Vector3Int MeshDivisions { get; }

        public HullElement(BoxCollider collider, float mass, float buoyancy = 1f, Vector3Int? meshDivisions = null)
        {
            Collider = collider;
            Mass = mass;
            Buoyancy = buoyancy;
            MeshDivisions = meshDivisions ?? Vector3Int.one;
        }
    }
}
