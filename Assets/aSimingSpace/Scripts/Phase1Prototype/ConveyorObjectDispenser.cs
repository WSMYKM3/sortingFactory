using System.Collections;
using UnityEngine;
using UnityEngine.Splines;

namespace SortingFactory.Phase1
{
    public sealed class ConveyorObjectDispenser : MonoBehaviour
    {
        private SplineContainer conveyorPath;
        private Material[] objectMaterials;
        private GameObject[] objectPrefabs;
        private float beltSurfaceY;
        private float releaseInterval;
        private float laneSpread;
        private int objectCount;
        private int releasedCount;
        private System.Random random;

        public int ReleasedCount => releasedCount;
        public int ObjectCount => objectCount;

        public void Configure(
            SplineContainer path,
            float surfaceY,
            int totalObjects,
            float interval,
            float horizontalSpread,
            int randomSeed,
            GameObject[] prefabs,
            Material[] materials)
        {
            conveyorPath = path;
            beltSurfaceY = surfaceY;
            objectCount = totalObjects;
            releaseInterval = interval;
            laneSpread = horizontalSpread;
            objectPrefabs = prefabs;
            objectMaterials = materials;
            random = new System.Random(randomSeed);
            StartCoroutine(ReleaseObjects());
        }

        private IEnumerator ReleaseObjects()
        {
            yield return new WaitForFixedUpdate();
            yield return new WaitForSeconds(0.4f);

            for (int i = 0; i < objectCount; i++)
            {
                SpawnObject(i);
                releasedCount++;
                yield return new WaitForSeconds(releaseInterval);
            }
        }

        private void SpawnObject(int index)
        {
            const float spawnT = 0.035f;
            Vector3 pathPosition = conveyorPath.EvaluatePosition(spawnT);
            Vector3 tangent = conveyorPath.EvaluateTangent(spawnT);
            tangent.y = 0f;
            tangent = tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, tangent).normalized;
            float sideOffset = RandomRange(-laneSpread, laneSpread);

            GameObject item = CreateObject(index);
            item.transform.SetParent(transform, true);
            item.transform.SetPositionAndRotation(
                new Vector3(pathPosition.x, beltSurfaceY + 0.08f, pathPosition.z) + side * sideOffset,
                Quaternion.LookRotation(tangent, Vector3.up));
        }

        private GameObject CreateObject(int index)
        {
            if (objectPrefabs != null && objectPrefabs.Length > 0)
            {
                GameObject prefab = objectPrefabs[random.Next(objectPrefabs.Length)];
                if (prefab != null)
                {
                    GameObject customObject = Instantiate(prefab);
                    customObject.name = $"Pickable_{index + 1:00}_{prefab.name}";
                    EnsurePhysics(customObject);
                    return customObject;
                }
            }

            PrototypeObjectKind kind = (PrototypeObjectKind)(index % 3);
            Material material = objectMaterials[random.Next(objectMaterials.Length)];
            return PrototypeVisualFactory.CreatePickableObject(kind, material, index + 1);
        }

        private static void EnsurePhysics(GameObject item)
        {
            Rigidbody rigidbody = item.GetComponent<Rigidbody>();
            if (rigidbody == null)
            {
                rigidbody = item.AddComponent<Rigidbody>();
            }

            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;

            if (item.GetComponentInChildren<Collider>() == null)
            {
                BoxCollider fallbackCollider = item.AddComponent<BoxCollider>();
                fallbackCollider.size = new Vector3(0.6f, 1f, 0.6f);
                fallbackCollider.center = new Vector3(0f, 0.5f, 0f);
                Debug.LogWarning($"{item.name} had no Collider, so Step 1 added a fallback BoxCollider.", item);
            }
        }

        private float RandomRange(float minimum, float maximum)
        {
            return Mathf.Lerp(minimum, maximum, (float)random.NextDouble());
        }
    }
}
