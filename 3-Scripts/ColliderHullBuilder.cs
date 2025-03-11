using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StormBreakers
{
    public static class ColliderHullBuilder
    {
        public static bool TryBuild(Rigidbody body, IReadOnlyList<HullElement> elements, out HullBuildResult result)
        {
            result = null;

            if (body == null || elements == null || elements.Count == 0)
            {
                Debug.LogError("A Rigidbody and at least one hull element are required.", body);
                return false;
            }

            var bodyTransform = body.transform;
            var determinant = bodyTransform.localToWorldMatrix.determinant;

            if (!float.IsFinite(determinant) || Mathf.Abs(determinant) < 0.000001f)
            {
                Debug.LogError("The hull Rigidbody must have a finite, non-zero scale.", body);
                return false;
            }

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var buoyancyMultipliers = new List<float>();
            var usedColliders = new HashSet<BoxCollider>();
            var worldToBody = bodyTransform.worldToLocalMatrix;
            var mass = 0f;
            var volume = 0f;
            var weightedCenter = Vector3.zero;

            foreach (var element in elements)
            {
                var collider = element.Collider;
                var divisions = element.MeshDivisions;

                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger || collider.attachedRigidbody != body)
                {
                    Debug.LogError("Each hull element requires an active, non-trigger BoxCollider owned by the supplied Rigidbody.", body);
                    return false;
                }

                if (!float.IsFinite(element.Mass) || element.Mass <= 0f || !float.IsFinite(element.Buoyancy) || element.Buoyancy < 0f)
                {
                    Debug.LogError($"Hull element '{collider.name}' requires positive finite mass and non-negative finite buoyancy.", collider);
                    return false;
                }

                if (divisions.x < 1 || divisions.x > 8 || divisions.y < 1 || divisions.y > 8 || divisions.z < 1 || divisions.z > 8)
                {
                    Debug.LogError($"Hull element '{collider.name}' requires mesh divisions between 1 and 8 per axis.", collider);
                    return false;
                }

                if (!usedColliders.Add(collider))
                {
                    Debug.LogError($"BoxCollider '{collider.name}' is assigned to more than one hull element.", collider);
                    return false;
                }

                var partToWorld = collider.transform.localToWorldMatrix;
                var size = collider.size;
                var partVolume = Mathf.Abs(size.x * size.y * size.z * partToWorld.determinant);
                var worldCenter = partToWorld.MultiplyPoint3x4(collider.center);

                if (size.x <= 0f || size.y <= 0f || size.z <= 0f || !float.IsFinite(partVolume) || partVolume <= 0.000001f || !IsFinite(worldCenter))
                {
                    Debug.LogError($"Hull element '{collider.name}' has no usable volume or position.", collider);
                    return false;
                }

                mass += element.Mass;
                volume += partVolume * element.Buoyancy;
                weightedCenter += worldCenter * element.Mass;

                AppendBox(vertices, triangles, buoyancyMultipliers, worldToBody * partToWorld, collider.center, size, divisions, element.Buoyancy);
            }

            if (!float.IsFinite(mass) || !float.IsFinite(volume) || volume <= 0f || !IsFinite(weightedCenter))
            {
                Debug.LogError("The hull requires finite mass and positive finite displacement.", body);
                return false;
            }

            var mesh = new Mesh
            {
                name = $"{body.name} Collider Hull",
                indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            // Rigidbody.centerOfMass uses body rotation without applying Transform scale.
            var centerOfMass = bodyTransform.InverseTransformDirection(weightedCenter / mass - bodyTransform.position);
            result = new HullBuildResult(mesh, buoyancyMultipliers.ToArray(), elements.Count, mass, volume, centerOfMass);
            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        internal static void AppendBox(List<Vector3> vertices, List<int> triangles, List<float> buoyancyMultipliers, Matrix4x4 boxToBody, Vector3 center, Vector3 size, Vector3Int divisions, float buoyancy)
        {
            var minimum = center - size * 0.5f;
            var x = Vector3.right * size.x;
            var y = Vector3.up * size.y;
            var z = Vector3.forward * size.z;
            var mirrored = Vector3.Dot(boxToBody.MultiplyVector(Vector3.right), Vector3.Cross(boxToBody.MultiplyVector(Vector3.up), boxToBody.MultiplyVector(Vector3.forward))) < 0f;
            
            AppendFace(vertices, triangles, buoyancyMultipliers, boxToBody, minimum + x, y, z, divisions.y, divisions.z, buoyancy, mirrored);
            AppendFace(vertices, triangles, buoyancyMultipliers, boxToBody, minimum, z, y, divisions.z, divisions.y, buoyancy, mirrored);
            AppendFace(vertices, triangles, buoyancyMultipliers, boxToBody, minimum + y, z, x, divisions.z, divisions.x, buoyancy, mirrored);
            AppendFace(vertices, triangles, buoyancyMultipliers, boxToBody, minimum, x, z, divisions.x, divisions.z, buoyancy, mirrored);
            AppendFace(vertices, triangles, buoyancyMultipliers, boxToBody, minimum + z, x, y, divisions.x, divisions.y, buoyancy, mirrored);
            AppendFace(vertices, triangles, buoyancyMultipliers, boxToBody, minimum, y, x, divisions.y, divisions.x, buoyancy, mirrored);
        }
        
        private static void AppendFace(List<Vector3> vertices, List<int> triangles, List<float> buoyancyMultipliers, Matrix4x4 boxToBody, Vector3 origin, Vector3 horizontal, Vector3 vertical, int horizontalDivisions, int verticalDivisions, float buoyancy, bool mirrored)
        {
            var firstVertex = vertices.Count;
            var rowSize = horizontalDivisions + 1;

            for (var row = 0; row <= verticalDivisions; row++)
            {
                for (var column = 0; column <= horizontalDivisions; column++)
                {
                    var position = origin + horizontal * ((float)column / horizontalDivisions) + vertical * ((float)row / verticalDivisions);
                    vertices.Add(boxToBody.MultiplyPoint3x4(position));
                }
            }

            for (var row = 0; row < verticalDivisions; row++)
            {
                for (var column = 0; column < horizontalDivisions; column++)
                {
                    var a = firstVertex + row * rowSize + column;
                    var b = a + 1;
                    var c = a + rowSize;
                    var d = c + 1;

                    triangles.Add(a);
                    triangles.Add(mirrored ? c : b);
                    triangles.Add(mirrored ? b : c);
                    triangles.Add(b);
                    triangles.Add(mirrored ? c : d);
                    triangles.Add(mirrored ? d : c);
                    
                    buoyancyMultipliers.Add(buoyancy);
                    buoyancyMultipliers.Add(buoyancy);
                }
            }
        }
    }
}
