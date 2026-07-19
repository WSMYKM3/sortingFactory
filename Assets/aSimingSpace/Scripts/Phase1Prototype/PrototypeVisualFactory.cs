using UnityEngine;

namespace SortingFactory.Phase1
{
    public enum PrototypeObjectKind
    {
        Bottle,
        Can,
        Box
    }

    internal static class PrototypeVisualFactory
    {
        public static void CreateWireBox(Transform parent, Vector3 size, Material material, float thickness)
        {
            Vector3 half = size * 0.5f;

            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    CreateBar(parent, new Vector3(0f, y * half.y, z * half.z), new Vector3(size.x, thickness, thickness), material);
                }
            }

            for (int x = -1; x <= 1; x += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    CreateBar(parent, new Vector3(x * half.x, 0f, z * half.z), new Vector3(thickness, size.y, thickness), material);
                }
            }

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    CreateBar(parent, new Vector3(x * half.x, y * half.y, 0f), new Vector3(thickness, thickness, size.z), material);
                }
            }
        }

        public static void CreatePlaceholderRobot(Transform robotMount, Material material)
        {
            Transform placeholder = new GameObject("RobotArmPlaceholder_REPLACE_ME").transform;
            placeholder.SetParent(robotMount, false);

            CreatePrimitive("Base", PrimitiveType.Cylinder, placeholder, new Vector3(0f, 0.18f, 0f), new Vector3(0.72f, 0.18f, 0.72f), material, false);
            CreatePrimitive("Pedestal", PrimitiveType.Cylinder, placeholder, new Vector3(0f, 0.72f, 0f), new Vector3(0.42f, 0.55f, 0.42f), material, false);

            Vector3 shoulder = new Vector3(0f, 1.25f, 0f);
            Vector3 elbow = new Vector3(-0.55f, 2.05f, 0f);
            Vector3 wrist = new Vector3(-1.35f, 2.15f, 0f);
            CreateJoint("Shoulder", placeholder, shoulder, 0.32f, material);
            CreateLink("UpperArm", placeholder, shoulder, elbow, 0.22f, material);
            CreateJoint("Elbow", placeholder, elbow, 0.26f, material);
            CreateLink("Forearm", placeholder, elbow, wrist, 0.18f, material);
            CreateJoint("Wrist", placeholder, wrist, 0.2f, material);

            CreatePrimitive("GripperPalm", PrimitiveType.Cube, placeholder, wrist + new Vector3(-0.22f, 0f, 0f), new Vector3(0.36f, 0.16f, 0.34f), material, false);
            CreatePrimitive("GripperLeft", PrimitiveType.Cube, placeholder, wrist + new Vector3(-0.42f, -0.12f, -0.13f), new Vector3(0.32f, 0.12f, 0.08f), material, false);
            CreatePrimitive("GripperRight", PrimitiveType.Cube, placeholder, wrist + new Vector3(-0.42f, -0.12f, 0.13f), new Vector3(0.32f, 0.12f, 0.08f), material, false);
        }

        public static void CreateDropZone(Transform parent, Material material)
        {
            CreatePrimitive("DropSurface", PrimitiveType.Cube, parent, Vector3.zero, new Vector3(1.65f, 0.12f, 1.65f), material, true);
            CreateWireBox(parent, new Vector3(1.7f, 0.55f, 1.7f), material, 0.04f);
        }

        public static void CreateGuideRail(Transform parent, float side, Material material)
        {
            Transform rail = CreatePrimitive(
                side < 0f ? "LeftGuide" : "RightGuide",
                PrimitiveType.Cube,
                parent,
                new Vector3(side, 0f, 0.75f),
                new Vector3(0.09f, 0.35f, 2.4f),
                material,
                false);
            rail.localRotation = Quaternion.Euler(0f, side < 0f ? -8f : 8f, 0f);
        }

        public static void CreateSortingPlatform(
            Transform parent,
            Vector2 size,
            Material beltMaterial,
            Material edgeMaterial)
        {
            CreatePrimitive(
                "SortingDeck",
                PrimitiveType.Cube,
                parent,
                Vector3.zero,
                new Vector3(size.x, 0.18f, size.y),
                beltMaterial,
                true);
            CreateWireBox(
                parent,
                new Vector3(size.x, 0.65f, size.y),
                edgeMaterial,
                0.055f);
        }

        public static void CreateFeederBeltSegment(
            Transform parent,
            Vector3 start,
            Vector3 end,
            float surfaceY,
            float width,
            Material beltMaterial,
            Material railMaterial)
        {
            Vector3 direction = end - start;
            direction.y = 0f;
            float length = direction.magnitude;
            if (length <= 0.001f)
            {
                return;
            }

            Transform segment = new GameObject("FeederBeltSegment").transform;
            segment.SetParent(parent, true);
            segment.SetPositionAndRotation(
                new Vector3((start.x + end.x) * 0.5f, surfaceY - 0.09f, (start.z + end.z) * 0.5f),
                Quaternion.LookRotation(direction.normalized, Vector3.up));

            CreatePrimitive(
                "BeltSurface",
                PrimitiveType.Cube,
                segment,
                Vector3.zero,
                new Vector3(width, 0.18f, length + 0.12f),
                beltMaterial,
                true);
            CreatePrimitive(
                "LeftRail",
                PrimitiveType.Cube,
                segment,
                new Vector3(-width * 0.5f, 0.24f, 0f),
                new Vector3(0.08f, 0.48f, length + 0.12f),
                railMaterial,
                true);
            CreatePrimitive(
                "RightRail",
                PrimitiveType.Cube,
                segment,
                new Vector3(width * 0.5f, 0.24f, 0f),
                new Vector3(0.08f, 0.48f, length + 0.12f),
                railMaterial,
                true);
        }

        public static GameObject CreatePickableObject(PrototypeObjectKind kind, Material material, int index)
        {
            GameObject root = new GameObject($"Pickable_{index:00}_{kind}");
            Rigidbody rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.mass = kind == PrototypeObjectKind.Box ? 0.65f : 0.4f;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rigidbody.linearDamping = 0.1f;
            rigidbody.angularDamping = 0.35f;

            switch (kind)
            {
                case PrototypeObjectKind.Bottle:
                    CreateBottle(root.transform, material);
                    break;
                case PrototypeObjectKind.Can:
                    CreateCan(root.transform, material);
                    break;
                default:
                    CreateBox(root.transform, material);
                    break;
            }

            return root;
        }

        private static void CreateBottle(Transform parent, Material material)
        {
            CreatePrimitive("Body", PrimitiveType.Cylinder, parent, new Vector3(0f, 0.46f, 0f), new Vector3(0.27f, 0.46f, 0.27f), material, true);
            CreatePrimitive("Shoulder", PrimitiveType.Sphere, parent, new Vector3(0f, 0.89f, 0f), new Vector3(0.5f, 0.22f, 0.5f), material, true);
            CreatePrimitive("Neck", PrimitiveType.Cylinder, parent, new Vector3(0f, 1.08f, 0f), new Vector3(0.13f, 0.2f, 0.13f), material, true);
            CreatePrimitive("Cap", PrimitiveType.Cylinder, parent, new Vector3(0f, 1.3f, 0f), new Vector3(0.16f, 0.06f, 0.16f), material, true);
        }

        private static void CreateCan(Transform parent, Material material)
        {
            CreatePrimitive("CanBody", PrimitiveType.Cylinder, parent, new Vector3(0f, 0.42f, 0f), new Vector3(0.31f, 0.42f, 0.31f), material, true);
        }

        private static void CreateBox(Transform parent, Material material)
        {
            CreatePrimitive("BoxBody", PrimitiveType.Cube, parent, new Vector3(0f, 0.34f, 0f), new Vector3(0.62f, 0.68f, 0.55f), material, true);
        }

        private static void CreateJoint(string name, Transform parent, Vector3 position, float radius, Material material)
        {
            CreatePrimitive(name, PrimitiveType.Sphere, parent, position, Vector3.one * radius * 2f, material, false);
        }

        private static void CreateLink(string name, Transform parent, Vector3 from, Vector3 to, float radius, Material material)
        {
            Vector3 delta = to - from;
            Transform link = CreatePrimitive(
                name,
                PrimitiveType.Cylinder,
                parent,
                (from + to) * 0.5f,
                new Vector3(radius, delta.magnitude * 0.5f, radius),
                material,
                false);
            link.localRotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
        }

        private static void CreateBar(Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            CreatePrimitive("DebugEdge", PrimitiveType.Cube, parent, position, scale, material, false);
        }

        private static Transform CreatePrimitive(
            string name,
            PrimitiveType primitiveType,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            bool keepCollider)
        {
            GameObject primitive = GameObject.CreatePrimitive(primitiveType);
            primitive.name = name;
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = localPosition;
            primitive.transform.localScale = localScale;
            primitive.GetComponent<Renderer>().sharedMaterial = material;

            if (!keepCollider)
            {
                Collider collider = primitive.GetComponent<Collider>();
                if (collider != null)
                {
                    if (Application.isPlaying)
                    {
                        Object.Destroy(collider);
                    }
                    else
                    {
                        Object.DestroyImmediate(collider);
                    }
                }
            }

            return primitive.transform;
        }
    }
}
