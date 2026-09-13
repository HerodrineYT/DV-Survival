using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using DVSurvival.Core;
using DVSurvival.Mod;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class ProvisionGeometryTests
    {
        [Theory]
        [InlineData(ProvisionKind.Meal)]
        [InlineData(ProvisionKind.Water)]
        [InlineData(ProvisionKind.Coffee)]
        [InlineData(ProvisionKind.FirstAid)]
        [InlineData(ProvisionKind.HeatPack)]
        public void ModelsHaveBoundedGeometryAndOutwardPrintedLabels(ProvisionKind kind)
        {
            var g = ProvisionGeometry.Create(kind);
            Assert.Equal(g.Vertices.Count, g.TexCoords.Count);
            Assert.InRange(g.Triangles.Count / 3, 20, 1600);
            Assert.Equal(0, g.Triangles.Count % 3);
            foreach (var p in g.Vertices)
            {
                Assert.InRange(p.X, -.175f, .175f);
                Assert.InRange(p.Y, 0f, .3f);
                Assert.InRange(p.Z, -.1f, .1f);
            }
            foreach (var uv in g.TexCoords)
            {
                Assert.InRange(uv.U, 0f, 1f);
                Assert.InRange(uv.V, 0f, 1f);
            }
            foreach (int index in g.Triangles) Assert.InRange(index, 0, g.Vertices.Count - 1);
            for (int i = g.LabelStart; i < g.Triangles.Count; i += 3)
            {
                var a=g.Vertices[g.Triangles[i]]; var b=g.Vertices[g.Triangles[i+1]]; var c=g.Vertices[g.Triangles[i+2]];
                float normalZ=(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
                Assert.True(normalZ < 0, "Labels must face -Z, never draw mirrored on the back.");
                var uv=g.TexCoords[g.Triangles[i]];
                Assert.InRange(uv.U, ((int)kind-1)/8f, (int)kind/8f);
                Assert.InRange(uv.V, .5f, 1f);
                Assert.True(a.Z < 0 && b.Z < 0 && c.Z < 0);
            }
            // Optional offline preview of the exact production geometry, not an in-game capture.
            string output=Environment.GetEnvironmentVariable("DVSURVIVAL_MODEL_PREVIEW_DIR");
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output,kind+".json"),JsonSerializer.Serialize(new {
                    name=kind.ToString(), vertices=g.Vertices.Select(p=>new[]{p.X,p.Y,p.Z}),
                    uv=g.TexCoords.Select(t=>new[]{t.U,t.V}), triangles=g.Triangles, labelStart=g.LabelStart
                }));
            }
        }
    }
}
