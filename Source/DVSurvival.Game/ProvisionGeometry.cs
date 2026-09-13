using System;
using System.Collections.Generic;
using DVSurvival.Core;

namespace DVSurvival.Mod
{
    // Pure mesh data: testable without a Unity player; metres, front faces towards -Z.
    internal sealed class ProvisionGeometry
    {
        internal struct Point
        {
            public float X, Y, Z;
            public Point(float x, float y, float z) { X = x; Y = y; Z = z; }
        }
        internal struct UV
        {
            public float U, V;
            public UV(float u, float v) { U = u; V = v; }
        }
        public readonly List<Point> Vertices = new List<Point>();
        public readonly List<UV> TexCoords = new List<UV>();
        public readonly List<int> Triangles = new List<int>();
        public int LabelStart;
        private const int Segments = 20;

        public static ProvisionGeometry Create(ProvisionKind kind)
        {
            var g = new ProvisionGeometry();
            switch (kind)
            {
                case ProvisionKind.Meal:
                    g.Slab(0, .105f, 0, .158f, .194f, .047f, .012f, 0);
                    g.Box(0, .014f, 0, .162f, .012f, .05f, 1);
                    g.Box(0, .201f, 0, .162f, .012f, .05f, 1);
                    // Crimped seams, integrated into the same mesh.
                    for (int i = 0; i < 12; i++)
                        g.Box(-.0715f + i * .013f, .201f, -.026f, .003f, .009f, .002f, 0);
                    break;
                case ProvisionKind.Water:
                    g.Lathe(2, 0,.035f, .009f,.043f, .025f,.044f, .158f,.044f,
                        .175f,.039f, .2f,.022f, .223f,.022f);
                    break;
                case ProvisionKind.Coffee:
                    g.Lathe(5, 0,.034f, .008f,.043f, .021f,.043f, .023f,.039f,
                        .203f,.039f, .209f,.032f, .216f,.032f);
                    g.Lathe(6, .026f,.040f, .185f,.040f);
                    g.Lathe(9, .213f,.034f, .219f,.039f, .254f,.039f, .26f,.034f);
                    g.Lathe(5, .22f,.0395f, .225f,.0395f);
                    // Carry handle has an actual opening, not a solid block.
                    g.Box(.052f, .106f, 0, .012f, .091f, .018f, 9);
                    g.Box(.044f, .062f, 0, .022f, .012f, .018f, 9);
                    g.Box(.044f, .150f, 0, .022f, .012f, .018f, 9);
                    break;
                case ProvisionKind.FirstAid:
                    g.Slab(0, .088f, 0, .19f, .16f, .062f, .014f, 7);
                    g.Box(0, .088f, 0, .192f, .145f, .004f, 9);
                    g.Box(-.036f,.176f,0,.012f,.031f,.02f,9);
                    g.Box(.036f,.176f,0,.012f,.031f,.02f,9);
                    g.Box(0,.193f,0,.083f,.012f,.02f,9);
                    g.Box(-.056f,.167f,-.01f,.024f,.01f,.044f,5);
                    g.Box(.056f,.167f,-.01f,.024f,.01f,.044f,5);
                    break;
                case ProvisionKind.HeatPack:
                    g.Slab(0,.092f,0,.137f,.166f,.026f,.015f,8);
                    g.Box(0,.012f,0,.145f,.011f,.024f,3);
                    g.Box(0,.175f,0,.145f,.011f,.024f,3);
                    for (int i = 0; i < 12; i++)
                        g.Box(-.066f+i*.012f,.175f,-.0125f,.002f,.008f,.001f,8);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
            if (kind == ProvisionKind.Water)
            {
                g.Lathe(3,.218f,.022f,.222f,.026f,.244f,.026f,.248f,.022f);
                g.Lathe(9,.214f,.0225f,.219f,.0225f);
                foreach (float y in new[] { .029f, .039f, .148f, .158f })
                    g.Lathe(2,y,.044f,y+.003f,.046f,y+.007f,.044f);
            }
            g.LabelStart = g.Triangles.Count;
            int label = (int)kind - 1;
            if (kind == ProvisionKind.Water) g.CurvedLabel(label,.045f,.052f,.14f);
            else if (kind == ProvisionKind.Coffee) g.CurvedLabel(label,.041f,.055f,.166f);
            else if (kind == ProvisionKind.Meal) g.Label(label,.128f,.126f,.105f,-.0242f);
            else if (kind == ProvisionKind.FirstAid) g.Label(label,.121f,.105f,.088f,-.0317f);
            else g.Label(label,.11f,.116f,.092f,-.0137f);
            return g;
        }

        private UV Swatch(int color) { return new UV((color * 128 + 64f) / 2048f, .25f); }
        private UV LabelUV(int label, float u, float v)
        {
            // Inset half a texel; 12px artwork border prevents mip bleed.
            return new UV((label * 256 + .5f + u * 255) / 2048f, (256.5f + v * 255) / 512f);
        }
        private void Quad(Point a, Point b, Point c, Point d, int color)
        {
            var uv = Swatch(color);
            Quad(a,b,c,d,uv,uv,uv,uv);
        }
        private void Quad(Point a, Point b, Point c, Point d, UV ua, UV ub, UV uc, UV ud)
        {
            int start = Vertices.Count;
            Vertices.AddRange(new[] { a,b,c,d });
            TexCoords.AddRange(new[] { ua,ub,uc,ud });
            Triangles.AddRange(new[] { start,start+1,start+2 });
            if (a.X != d.X || a.Y != d.Y || a.Z != d.Z)
                Triangles.AddRange(new[] { start,start+2,start+3 });
        }
        private void Box(float x,float y,float z,float w,float h,float depth,int color)
        {
            var a=new Point(x-w/2,y-h/2,z-depth/2); var b=new Point(x+w/2,y-h/2,z-depth/2);
            var c=new Point(x+w/2,y+h/2,z-depth/2); var d=new Point(x-w/2,y+h/2,z-depth/2);
            var e=new Point(a.X,a.Y,z+depth/2); var f=new Point(b.X,b.Y,e.Z);
            var g=new Point(c.X,c.Y,e.Z); var h0=new Point(d.X,d.Y,e.Z);
            Quad(a,d,c,b,color); Quad(e,f,g,h0,color);
            Quad(a,e,h0,d,color); Quad(b,c,g,f,color);
            Quad(d,h0,g,c,color); Quad(a,b,f,e,color);
        }
        private void Slab(float x,float y,float z,float w,float h,float depth,float bevel,int color)
        {
            var outline = new[] {
                new Point(-w/2+bevel,-h/2,0),new Point(w/2-bevel,-h/2,0),
                new Point(w/2,-h/2+bevel,0),new Point(w/2,h/2-bevel,0),
                new Point(w/2-bevel,h/2,0),new Point(-w/2+bevel,h/2,0),
                new Point(-w/2,h/2-bevel,0),new Point(-w/2,-h/2+bevel,0) };
            // A shallow bevel on both sides catches light along the packaging edges.
            for (int i=0;i<8;i++)
            {
                var p=outline[i]; var q=outline[(i+1)%8];
                var a=new Point(x+p.X,y+p.Y,z-depth*.32f); var b=new Point(x+q.X,y+q.Y,a.Z);
                var c=new Point(b.X,b.Y,z+depth*.32f); var d=new Point(a.X,a.Y,c.Z);
                var af=new Point(x+p.X*.93f,y+p.Y*.93f,z-depth/2);
                var bf=new Point(x+q.X*.93f,y+q.Y*.93f,af.Z);
                var ab=new Point(af.X,af.Y,z+depth/2); var bb=new Point(bf.X,bf.Y,ab.Z);
                Quad(a,b,c,d,color); Quad(af,bf,b,a,color); Quad(d,c,bb,ab,color);
                // Triangle fans (fourth vertex duplicates center).
                Quad(new Point(x,y,af.Z),bf,af,new Point(x,y,af.Z),color);
                Quad(new Point(x,y,ab.Z),ab,bb,new Point(x,y,ab.Z),color);
            }
        }
        private static Point Ring(float y,float radius,double angle)
        { return new Point((float)Math.Sin(angle)*radius,y,-(float)Math.Cos(angle)*radius); }
        private void Lathe(int color, params float[] profile)
        {
            for (int ring=0;ring<profile.Length/2-1;ring++)
                for (int j=0;j<Segments;j++)
                {
                    double a=j*Math.PI*2/Segments, b=(j+1)*Math.PI*2/Segments;
                    Quad(Ring(profile[ring*2],profile[ring*2+1],a),
                        Ring(profile[ring*2+2],profile[ring*2+3],a),
                        Ring(profile[ring*2+2],profile[ring*2+3],b),
                        Ring(profile[ring*2],profile[ring*2+1],b),color);
                }
            for (int j=0;j<Segments;j++)
            {
                double a=j*Math.PI*2/Segments,b=(j+1)*Math.PI*2/Segments;
                var bottom=new Point(0,profile[0],0);
                Quad(bottom,Ring(profile[0],profile[1],a),Ring(profile[0],profile[1],b),bottom,color);
                int last=profile.Length-2; var top=new Point(0,profile[last],0);
                Quad(top,Ring(profile[last],profile[last+1],b),Ring(profile[last],profile[last+1],a),top,color);
            }
        }
        private void Label(int label,float w,float h,float y,float z)
        {
            Quad(new Point(-w/2,y-h/2,z),new Point(-w/2,y+h/2,z),
                new Point(w/2,y+h/2,z),new Point(w/2,y-h/2,z),
                LabelUV(label,0,0),LabelUV(label,0,1),LabelUV(label,1,1),LabelUV(label,1,0));
        }
        private void CurvedLabel(int label,float radius,float bottom,float top)
        {
            // Match the body's 18-degree facets; labels sit 1mm above the opaque shell.
            for (int i=0;i<6;i++)
            {
                double a=(i-3)*Math.PI/10, b=(i-2)*Math.PI/10;
                float u=i/6f,v=(i+1)/6f;
                Quad(Ring(bottom,radius,a),Ring(top,radius,a),Ring(top,radius,b),Ring(bottom,radius,b),
                    LabelUV(label,u,0),LabelUV(label,u,1),LabelUV(label,v,1),LabelUV(label,v,0));
            }
        }
    }
}
