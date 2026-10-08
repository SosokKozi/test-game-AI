using System.Collections.Generic;
using RacingSim.Core;
using RacingSim.Vehicle;
using UnityEngine;
using UnityEngine.Rendering;

namespace RacingSim.Track
{
    /// <summary>Простой накопитель геометрии с автоматическим выбором порядка обхода по нормали.</summary>
    public class MeshBuilder
    {
        public readonly List<Vector3> verts = new List<Vector3>();
        public readonly List<Vector3> normals = new List<Vector3>();
        public readonly List<Vector2> uvs = new List<Vector2>();
        public readonly List<int> tris = new List<int>();

        public int VertexCount => verts.Count;
        public bool IsEmpty => tris.Count == 0;

        /// <summary>Четырёхугольник a-b-c-d (по периметру), лицевая сторона смотрит вдоль normal.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal,
            Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);
            for (int k = 0; k < 4; k++) normals.Add(normal);
            // в Unity лицевая сторона — обход по часовой, т.е. cross(b-a, c-a) смотрит наружу
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f;
            if (!flip) { tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); tris.Add(i); tris.Add(i + 2); tris.Add(i + 3); }
            else { tris.Add(i); tris.Add(i + 2); tris.Add(i + 1); tris.Add(i); tris.Add(i + 3); tris.Add(i + 2); }
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal) =>
            Quad(a, b, c, d, normal, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            normals.Add(na); normals.Add(nb); normals.Add(nc);
            uvs.Add(Vector2.zero); uvs.Add(Vector2.right); uvs.Add(Vector2.up);
            Vector3 n = (na + nb + nc);
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) >= 0f) { tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); }
            else { tris.Add(i); tris.Add(i + 2); tris.Add(i + 1); }
        }

        public GameObject Build(string name, Transform parent, Material mat, bool collider, SurfaceType? surface = null)
        {
            var mesh = new Mesh { name = name };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            if (collider)
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
            }
            if (surface.HasValue) SurfaceInfo.Configure(go, surface.Value);
            return go;
        }
    }

    /// <summary>Общие материалы трассы.</summary>
    public static class TrackMaterials
    {
        static Material asphalt, grass, gravel, kerbRed, kerbWhite, wall, line, tree, trunk;

        public static Material Asphalt => asphalt != null ? asphalt : asphalt = MaterialFactory.Create("asphalt",
            Color.white, 0.25f, 0f,
            MaterialFactory.NoiseTexture("asphaltTex", new Color(0.16f, 0.16f, 0.17f), new Color(0.32f, 0.32f, 0.33f), 24f),
            new Vector2(1f, 1f));

        public static Material Grass => grass != null ? grass : grass = MaterialFactory.Create("grass",
            Color.white, 0.1f, 0f,
            MaterialFactory.NoiseTexture("grassTex", new Color(0.18f, 0.33f, 0.12f), new Color(0.33f, 0.5f, 0.2f), 10f, 256, true));

        public static Material Gravel => gravel != null ? gravel : gravel = MaterialFactory.Create("gravel",
            Color.white, 0.05f, 0f,
            MaterialFactory.NoiseTexture("gravelTex", new Color(0.55f, 0.5f, 0.4f), new Color(0.78f, 0.74f, 0.64f), 40f));

        public static Material KerbRed => kerbRed != null ? kerbRed : kerbRed = MaterialFactory.Create("kerbRed", new Color(0.75f, 0.08f, 0.06f), 0.4f);
        public static Material KerbWhite => kerbWhite != null ? kerbWhite : kerbWhite = MaterialFactory.Create("kerbWhite", new Color(0.92f, 0.92f, 0.92f), 0.4f);
        public static Material Wall => wall != null ? wall : wall = MaterialFactory.Create("wall", new Color(0.72f, 0.72f, 0.74f), 0.2f, 0.3f);
        public static Material Line => line != null ? line : line = MaterialFactory.Create("line", new Color(0.95f, 0.95f, 0.95f), 0.3f);
        public static Material Tree => tree != null ? tree : tree = MaterialFactory.Create("tree", new Color(0.12f, 0.26f, 0.12f), 0.05f);
        public static Material Trunk => trunk != null ? trunk : trunk = MaterialFactory.Create("trunk", new Color(0.3f, 0.22f, 0.14f), 0.05f);

        public static Texture2D GrassTexture => (Texture2D)(Grass.HasProperty("_BaseMap") ? Grass.GetTexture("_BaseMap") : Grass.GetTexture("_MainTex"));
    }
}
