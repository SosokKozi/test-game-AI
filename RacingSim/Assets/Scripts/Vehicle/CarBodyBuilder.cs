using System.Collections.Generic;
using RacingSim.Core;
using UnityEngine;

namespace RacingSim.Vehicle
{
    /// <summary>Ссылки на визуальные части машины.</summary>
    public class CarVisual
    {
        public Transform root;
        public Transform[] wheelPivots = new Transform[4];
        public Transform[] wheelSpin = new Transform[4];
        public Transform cockpitAnchor;
        public Transform chaseTarget;

        public void Sync(VehicleController car)
        {
            for (int i = 0; i < 4; i++)
            {
                var w = car.Wheels[i];
                wheelPivots[i].position = w.WheelCenter(car.transform);
                wheelPivots[i].localRotation = Quaternion.Euler(0f, w.steerAngleDeg, 0f);
                wheelSpin[i].localRotation = Quaternion.Euler(w.spinAngle, 0f, 0f);
            }
        }
    }

    /// <summary>
    /// Процедурная low-poly модель GT3-автомобиля по реальным габаритам (длина, ширина, высота,
    /// база, колея, свесы, компоновка). Это заглушка до импорта настоящей 3D-модели:
    /// см. README → «Замена моделей автомобилей».
    /// </summary>
    public static class CarBodyBuilder
    {
        struct Section
        {
            public float t, halfWidth, bottom, top;
            public Section(float t, float hw, float bottom, float top) { this.t = t; halfWidth = hw; this.bottom = bottom; this.top = top; }
        }

        public static CarVisual Build(CarSpec spec, Transform car)
        {
            var d = spec.dimensions;
            var vis = new CarVisual();
            var root = new GameObject("Body").transform;
            root.SetParent(car, false);
            vis.root = root;

            float W = d.width, H = d.height, L = d.length;
            float frontZ = d.wheelbase * 0.5f, rearZ = -d.wheelbase * 0.5f;
            float noseZ = frontZ + d.frontOverhang;
            float tailZ = rearZ - (L - d.wheelbase - d.frontOverhang);
            bool mid = spec.bodyStyle == "midEngine";

            var bodyMat = MaterialFactory.Create("car_" + spec.id, spec.BodyColor, 0.85f, 0.4f);
            var accentMat = MaterialFactory.Create("accent_" + spec.id, spec.AccentColor, 0.7f, 0.2f);
            var glassMat = MaterialFactory.Create("glass", new Color(0.05f, 0.07f, 0.09f), 0.95f, 0.1f);
            var carbonMat = MaterialFactory.Create("carbon", new Color(0.06f, 0.06f, 0.07f), 0.5f, 0.2f);
            var tyreMat = MaterialFactory.Create("tyre", new Color(0.04f, 0.04f, 0.04f), 0.15f);
            var rimMat = MaterialFactory.Create("rim", new Color(0.55f, 0.55f, 0.58f), 0.8f, 0.9f);
            var lightMat = MaterialFactory.Create("headlight", Color.white, 0.9f, 0f, null, null, new Color(1f, 1f, 0.9f) * 1.5f);
            var tailMat = MaterialFactory.Create("taillight", new Color(0.6f, 0f, 0f), 0.9f, 0f, null, null, new Color(1f, 0f, 0f) * 1.2f);

            // настоящая модель, если есть: Resources/CarModels/<id>.prefab
            // (начало координат — на земле посередине между осями, нос по +Z, без колёс и коллайдеров)
            var custom = Resources.Load<GameObject>("CarModels/" + spec.id);

            // профиль нижней части кузова (t: 0 = корма, 1 = нос)
            var lower = mid
                ? new List<Section>
                {
                    new Section(0.00f, 0.46f * W, 0.12f, 0.58f * H),
                    new Section(0.06f, 0.49f * W, 0.10f, 0.66f * H),
                    new Section(0.25f, 0.50f * W, 0.10f, 0.68f * H),
                    new Section(0.45f, 0.47f * W, 0.10f, 0.60f * H),
                    new Section(0.72f, 0.50f * W, 0.10f, 0.50f * H),
                    new Section(0.90f, 0.48f * W, 0.10f, 0.40f * H),
                    new Section(1.00f, 0.42f * W, 0.08f, 0.24f * H),
                }
                : new List<Section>
                {
                    new Section(0.00f, 0.46f * W, 0.12f, 0.55f * H),
                    new Section(0.07f, 0.49f * W, 0.10f, 0.62f * H),
                    new Section(0.27f, 0.50f * W, 0.10f, 0.63f * H),
                    new Section(0.50f, 0.48f * W, 0.10f, 0.58f * H),
                    new Section(0.72f, 0.50f * W, 0.10f, 0.54f * H),
                    new Section(0.90f, 0.48f * W, 0.10f, 0.44f * H),
                    new Section(1.00f, 0.42f * W, 0.08f, 0.28f * H),
                };
            float c0 = mid ? 0.36f : 0.24f, c1 = mid ? 0.80f : 0.66f;
            if (custom != null)
            {
                var model = Object.Instantiate(custom, root, false);
                model.name = "Model";
                foreach (var col in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
            }
            else
            {
                AddLoft(root, "LowerBody", lower, tailZ, noseZ, bodyMat, 0.12f);

                // кабина
                float shoulder = mid ? 0.60f * H : 0.58f * H;
                var cabin = new List<Section>
                {
                    new Section(c0, 0.36f * W, shoulder - 0.05f, shoulder + 0.02f),
                    new Section(c0 + (c1 - c0) * 0.25f, 0.37f * W, shoulder - 0.05f, H),
                    new Section(c0 + (c1 - c0) * 0.55f, 0.37f * W, shoulder - 0.05f, H * 0.99f),
                    new Section(c1, 0.40f * W, shoulder - 0.08f, shoulder + 0.02f),
                };
                AddLoft(root, "Cabin", cabin, tailZ, noseZ, glassMat, 0.08f);
                float roofZ0 = Mathf.Lerp(tailZ, noseZ, c0 + (c1 - c0) * 0.25f);
                float roofZ1 = Mathf.Lerp(tailZ, noseZ, c0 + (c1 - c0) * 0.55f);
                AddBox(root, "Roof", new Vector3(0f, H + 0.005f, (roofZ0 + roofZ1) * 0.5f), new Vector3(0.66f * W, 0.02f, roofZ1 - roofZ0 + 0.05f), bodyMat);

                // полоса ливреи, сплиттер, диффузор, антикрыло
                AddBox(root, "Stripe", new Vector3(0f, 0.5f * H + 0.02f, Mathf.Lerp(tailZ, noseZ, 0.8f)), new Vector3(0.25f * W, 0.02f, 0.3f * L), accentMat).localRotation = Quaternion.Euler(-4f, 0f, 0f);
                AddBox(root, "Splitter", new Vector3(0f, 0.07f, noseZ - 0.05f), new Vector3(0.92f * W, 0.02f, 0.25f), carbonMat);
                AddBox(root, "Diffuser", new Vector3(0f, 0.18f, tailZ + 0.12f), new Vector3(0.75f * W, 0.16f, 0.25f), carbonMat);
                float wingY = mid ? H + 0.02f : H + 0.06f;
                float wingZ = tailZ + 0.22f;
                AddBox(root, "Wing", new Vector3(0f, wingY, wingZ), new Vector3(0.9f * W, 0.03f, 0.32f), carbonMat).localRotation = Quaternion.Euler(-8f, 0f, 0f);
                AddBox(root, "EndplateL", new Vector3(-0.45f * W, wingY - 0.05f, wingZ), new Vector3(0.015f, 0.22f, 0.40f), carbonMat);
                AddBox(root, "EndplateR", new Vector3(0.45f * W, wingY - 0.05f, wingZ), new Vector3(0.015f, 0.22f, 0.40f), carbonMat);
                float mountTop = wingY - 0.02f, mountBottom = lower[0].top - 0.02f;
                AddBox(root, "WingMountL", new Vector3(-0.2f * W, (mountTop + mountBottom) * 0.5f, wingZ + 0.05f), new Vector3(0.02f, mountTop - mountBottom, 0.18f), carbonMat);
                AddBox(root, "WingMountR", new Vector3(0.2f * W, (mountTop + mountBottom) * 0.5f, wingZ + 0.05f), new Vector3(0.02f, mountTop - mountBottom, 0.18f), carbonMat);

                // фары и фонари
                AddBox(root, "HeadL", new Vector3(-0.33f * W, 0.36f * H, noseZ - 0.12f), new Vector3(0.25f, 0.06f, 0.1f), lightMat);
                AddBox(root, "HeadR", new Vector3(0.33f * W, 0.36f * H, noseZ - 0.12f), new Vector3(0.25f, 0.06f, 0.1f), lightMat);
                AddBox(root, "TailL", new Vector3(-0.36f * W, 0.5f * H, tailZ + 0.02f), new Vector3(0.3f, 0.05f, 0.04f), tailMat);
                AddBox(root, "TailR", new Vector3(0.36f * W, 0.5f * H, tailZ + 0.02f), new Vector3(0.3f, 0.05f, 0.04f), tailMat);
            }

            // колёса
            string[] names = { "FL", "FR", "RL", "RR" };
            for (int i = 0; i < 4; i++)
            {
                bool front = i < 2, left = i % 2 == 0;
                var ty = front ? spec.tyreFront : spec.tyreRear;
                float x = (front ? d.trackFront : d.trackRear) * 0.5f * (left ? -1f : 1f);
                var pivot = new GameObject("Wheel" + names[i]).transform;
                pivot.SetParent(car, false);
                pivot.localPosition = new Vector3(x, ty.radius, front ? frontZ : rearZ);
                var spin = new GameObject("Spin").transform;
                spin.SetParent(pivot, false);
                var tyre = Primitive(PrimitiveType.Cylinder, spin, "Tyre", tyreMat);
                tyre.localRotation = Quaternion.Euler(0f, 0f, 90f);
                tyre.localScale = new Vector3(ty.radius * 2f, ty.width * 0.5f, ty.radius * 2f);
                var rim = Primitive(PrimitiveType.Cylinder, spin, "Rim", rimMat);
                rim.localRotation = Quaternion.Euler(0f, 0f, 90f);
                rim.localPosition = new Vector3((left ? -1f : 1f) * 0.01f, 0f, 0f);
                rim.localScale = new Vector3(ty.radius * 1.35f, ty.width * 0.505f, ty.radius * 1.35f);
                // спица — чтобы было видно вращение
                var spoke = Primitive(PrimitiveType.Cube, spin, "Spoke", carbonMat);
                spoke.localPosition = new Vector3((left ? -1f : 1f) * ty.width * 0.5f, 0f, 0f);
                spoke.localScale = new Vector3(0.02f, ty.radius * 1.2f, 0.06f);
                vis.wheelPivots[i] = pivot;
                vis.wheelSpin[i] = spin;
            }

            // камера из кокпита (левый руль) и точка для камеры преследования
            vis.cockpitAnchor = new GameObject("CockpitCam").transform;
            vis.cockpitAnchor.SetParent(car, false);
            vis.cockpitAnchor.localPosition = new Vector3(-0.36f, H - 0.22f, Mathf.Lerp(tailZ, noseZ, c0 + (c1 - c0) * 0.62f));
            vis.chaseTarget = new GameObject("ChaseTarget").transform;
            vis.chaseTarget.SetParent(car, false);
            vis.chaseTarget.localPosition = new Vector3(0f, H * 0.8f, 0f);

            SetLayerRecursive(car.gameObject, 2);
            return vis;
        }

        static Transform Primitive(PrimitiveType type, Transform parent, string name, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static Transform AddBox(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var t = Primitive(PrimitiveType.Cube, parent, name, mat);
            t.localPosition = pos;
            t.localScale = size;
            return t;
        }

        /// <summary>«Лофт» по поперечным сечениям — восьмиугольник со скруглёнными углами, плоское затенение.</summary>
        static void AddLoft(Transform parent, string name, List<Section> sections, float tailZ, float noseZ, Material mat, float chamfer)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            Vector3[] Ring(Section s)
            {
                float z = Mathf.Lerp(tailZ, noseZ, s.t);
                float hw = s.halfWidth, b = s.bottom, t = s.top;
                float c = Mathf.Min(chamfer, (t - b) * 0.3f, hw * 0.3f);
                return new[]
                {
                    new Vector3(-hw + c, b, z), new Vector3(hw - c, b, z),
                    new Vector3(hw, b + c, z), new Vector3(hw * 0.97f, t - c * 2f, z),
                    new Vector3(hw * 0.85f - c, t, z), new Vector3(-hw * 0.85f + c, t, z),
                    new Vector3(-hw * 0.97f, t - c * 2f, z), new Vector3(-hw, b + c, z),
                };
            }

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }

            void Cap(Vector3[] r, bool front)
            {
                int start = verts.Count;
                verts.AddRange(r);
                for (int k = 1; k < r.Length - 1; k++)
                {
                    if (front) { tris.Add(start); tris.Add(start + k + 1); tris.Add(start + k); }
                    else { tris.Add(start); tris.Add(start + k); tris.Add(start + k + 1); }
                }
            }

            Vector3[] prev = Ring(sections[0]);
            Cap(prev, false);
            for (int s = 1; s < sections.Count; s++)
            {
                var cur = Ring(sections[s]);
                for (int k = 0; k < 8; k++)
                {
                    int n = (k + 1) % 8;
                    Quad(prev[k], cur[k], cur[n], prev[n]);
                }
                prev = cur;
            }
            Cap(prev, true);

            // Unity: лицевая сторона — обход по часовой стрелке; переворачиваем порядок
            for (int i = 0; i < tris.Count; i += 3)
            {
                int tmp = tris[i + 1];
                tris[i + 1] = tris[i + 2];
                tris[i + 2] = tmp;
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
        }
    }
}
