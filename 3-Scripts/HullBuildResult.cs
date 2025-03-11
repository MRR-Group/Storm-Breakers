using UnityEngine;

namespace StormBreakers
{
    public sealed class HullBuildResult
    {
        public Mesh Mesh { get; }
        public float[] BuoyancyMultipliers { get; }
        public int ElementCount { get; }
        public int TriangleCount => BuoyancyMultipliers.Length;
        public float TotalMass { get; }
        public float DisplacementVolume { get; }
        public Vector3 CenterOfMass { get; }

        public float MaximumSupportedMass => DisplacementVolume * 1000f;

        internal HullBuildResult(Mesh mesh, float[] buoyancyMultipliers, int elementCount, float totalMass, float displacementVolume, Vector3 centerOfMass)
        {
            Mesh = mesh;
            BuoyancyMultipliers = buoyancyMultipliers;
            ElementCount = elementCount;
            TotalMass = totalMass;
            DisplacementVolume = displacementVolume;
            CenterOfMass = centerOfMass;
        }
    }
}
