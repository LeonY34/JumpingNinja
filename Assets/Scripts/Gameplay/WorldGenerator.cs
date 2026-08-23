using System.Collections.Generic;
using UnityEngine;

namespace JumpingNinja
{
    internal sealed class HazardBlock : MonoBehaviour
    {
    }

    internal sealed class SideWall : MonoBehaviour
    {
    }

    public sealed class WorldGenerator : MonoBehaviour
    {
        private const int GapWidth = 3;

        private readonly Dictionary<int, GameObject> segments = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, List<int>> boundaryGaps = new Dictionary<int, List<int>>();
        private JumpingNinjaConfig config;
        private Texture2D solidTexture;
        private Sprite solidSprite;
        private PhysicsMaterial2D frictionlessMaterial;
        private int resolvedSeed;

        public Sprite SolidSprite => solidSprite;
        public PhysicsMaterial2D FrictionlessMaterial => frictionlessMaterial;

        public void Initialize(JumpingNinjaConfig gameConfig)
        {
            config = gameConfig;
            resolvedSeed = config.randomSeed == 0 ? System.Environment.TickCount : config.randomSeed;
            CreateRuntimeAssets();
            CreateBottom();
        }

        public void EnsureGeneratedThrough(int highestSegment)
        {
            for (int segment = 0; segment <= highestSegment; segment++)
            {
                if (!segments.ContainsKey(segment))
                {
                    GenerateSegment(segment);
                }
            }
        }

        private void CreateRuntimeAssets()
        {
            solidTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "Runtime Solid Pixel",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            solidTexture.SetPixel(0, 0, Color.white);
            solidTexture.Apply();
            solidSprite = Sprite.Create(solidTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            solidSprite.name = "Runtime Square";

            frictionlessMaterial = new PhysicsMaterial2D("Frictionless")
            {
                friction = 0f,
                bounciness = 0f
            };
        }

        private void CreateBottom()
        {
            GameObject bottom = new GameObject("Bottom Boundary");
            bottom.transform.SetParent(transform, false);

            CreateBlock(
                bottom.transform,
                "Death Floor",
                new Vector2(config.SafeMapWidth * 0.5f, -0.5f),
                new Vector2(config.SafeMapWidth - 2f, 1f),
                config.hazardColor,
                true,
                false);

            CreateBlock(bottom.transform, "Left Corner", new Vector2(0.5f, -0.5f), Vector2.one, config.wallColor, false, true);
            CreateBlock(bottom.transform, "Right Corner", new Vector2(config.SafeMapWidth - 0.5f, -0.5f), Vector2.one, config.wallColor, false, true);
        }

        private void GenerateSegment(int segmentIndex)
        {
            int mapWidth = config.SafeMapWidth;
            int layerHeight = config.SafeLayerHeight;
            int startY = segmentIndex * layerHeight;
            int boundaryY = (segmentIndex + 1) * layerHeight - 1;

            GameObject segmentRoot = new GameObject($"Layer {segmentIndex}");
            segmentRoot.transform.SetParent(transform, false);
            segments.Add(segmentIndex, segmentRoot);

            for (int y = startY; y < startY + layerHeight; y++)
            {
                CreateBlock(segmentRoot.transform, $"Left Wall {y}", new Vector2(0.5f, y + 0.5f), Vector2.one, config.wallColor, false, true);
                CreateBlock(segmentRoot.transform, $"Right Wall {y}", new Vector2(mapWidth - 0.5f, y + 0.5f), Vector2.one, config.wallColor, false, true);
            }

            List<int> upperGaps = GetOrCreateBoundaryGaps(segmentIndex + 1);
            for (int x = 1; x < mapWidth - 1; x++)
            {
                if (!IsGapCell(x, upperGaps))
                {
                    CreateBlock(
                        segmentRoot.transform,
                        $"Boundary {x}",
                        new Vector2(x + 0.5f, boundaryY + 0.5f),
                        Vector2.one,
                        config.hazardColor,
                        true,
                        false);
                }
            }

            int oneBasedLayer = segmentIndex + 1;
            int obstacleCount = oneBasedLayer <= 5 ? 0 : ((oneBasedLayer - 1) / 10) + 1;
            System.Random random = CreateRandom(1000 + segmentIndex);
            List<int> lowerGaps = segmentIndex == 0 ? null : GetOrCreateBoundaryGaps(segmentIndex);

            int placed = 0;
            int attempts = 0;
            HashSet<Vector2Int> occupiedCells = new HashSet<Vector2Int>();
            while (placed < obstacleCount && attempts < 100)
            {
                attempts++;
                int x = random.Next(1, mapWidth - 1);
                int y = random.Next(startY + 2, boundaryY - 1);
                Vector2Int cell = new Vector2Int(x, y);
                if (occupiedCells.Contains(cell))
                {
                    continue;
                }

                if (IsProtectedNearBoundary(x, y, startY - 1, lowerGaps) ||
                    IsProtectedNearBoundary(x, y, boundaryY, upperGaps))
                {
                    continue;
                }

                CreateBlock(
                    segmentRoot.transform,
                    $"Obstacle {placed + 1}",
                    new Vector2(x + 0.5f, y + 0.5f),
                    Vector2.one,
                    config.hazardColor,
                    true,
                    false);
                occupiedCells.Add(cell);
                placed++;
            }
        }

        private List<int> GetOrCreateBoundaryGaps(int boundaryIndex)
        {
            if (boundaryGaps.TryGetValue(boundaryIndex, out List<int> existing))
            {
                return existing;
            }

            System.Random random = CreateRandom(boundaryIndex);
            int gapCount = random.Next(1, 3);
            int minimumStart = 2;
            int maximumStart = Mathf.Max(minimumStart, config.SafeMapWidth - GapWidth - 2);
            List<int> starts = new List<int>();

            int first = random.Next(minimumStart, maximumStart + 1);
            starts.Add(first);

            if (gapCount == 2)
            {
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    int candidate = random.Next(minimumStart, maximumStart + 1);
                    if (Mathf.Abs(candidate - first) >= GapWidth + 2)
                    {
                        starts.Add(candidate);
                        break;
                    }
                }
            }

            boundaryGaps.Add(boundaryIndex, starts);
            return starts;
        }

        private System.Random CreateRandom(int salt)
        {
            return new System.Random(unchecked(resolvedSeed * 397 ^ salt * 7919));
        }

        private static bool IsGapCell(int x, List<int> gapStarts)
        {
            foreach (int start in gapStarts)
            {
                if (x >= start && x < start + GapWidth)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsProtectedNearBoundary(int x, int y, int boundaryY, List<int> gapStarts)
        {
            if (gapStarts == null || Mathf.Abs(y - boundaryY) > 3)
            {
                return false;
            }

            foreach (int start in gapStarts)
            {
                if (x >= start - 3 && x <= start + GapWidth + 2)
                {
                    return true;
                }
            }

            return false;
        }

        private GameObject CreateBlock(
            Transform parent,
            string objectName,
            Vector2 position,
            Vector2 size,
            Color color,
            bool isHazard,
            bool isWall)
        {
            GameObject block = new GameObject(objectName, typeof(SpriteRenderer), typeof(BoxCollider2D));
            block.transform.SetParent(parent, false);
            block.transform.position = new Vector3(position.x, position.y, 0f);
            block.transform.localScale = new Vector3(size.x, size.y, 1f);

            SpriteRenderer renderer = block.GetComponent<SpriteRenderer>();
            renderer.sprite = solidSprite;
            renderer.color = color;
            renderer.sortingOrder = 0;

            BoxCollider2D collider = block.GetComponent<BoxCollider2D>();
            collider.sharedMaterial = frictionlessMaterial;

            if (isHazard)
            {
                block.AddComponent<HazardBlock>();
            }

            if (isWall)
            {
                block.AddComponent<SideWall>();
            }

            return block;
        }

        private void OnDestroy()
        {
            if (solidSprite != null)
            {
                Destroy(solidSprite);
            }

            if (solidTexture != null)
            {
                Destroy(solidTexture);
            }

            if (frictionlessMaterial != null)
            {
                Destroy(frictionlessMaterial);
            }
        }
    }
}
