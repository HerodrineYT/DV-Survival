using System;

namespace DVSurvival.Core
{
    // ShelfItem's pivot is the front edge; shelf depth runs along negative local Z.
    // The display model faces the aisle after a 180-degree Y rotation.
    public sealed class ShelfDisplayLayout
    {
        public float Width { get; private set; }
        public float Depth { get; private set; }
        public float Height { get; private set; }
        public float OffsetX { get; private set; }
        public float OffsetY { get; private set; }
        public float OffsetZ { get; private set; }
        public ShelfDisplayLayout(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
        {
            Width = Math.Max(0.35f, maxX - minX + 0.06f);
            Depth = Math.Max(0.30f, maxZ - minZ + 0.08f);
            Height = maxY - minY + 0.01f;
            OffsetX = (minX + maxX) * 0.5f;
            OffsetY = 0.002f - minY;
            OffsetZ = (minZ + maxZ) * 0.5f - Depth * 0.5f;
        }
    }
}
