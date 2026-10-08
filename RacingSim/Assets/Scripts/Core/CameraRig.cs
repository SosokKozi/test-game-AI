using RacingSim.Vehicle;
using UnityEngine;

namespace RacingSim.Core
{
    /// <summary>Камеры: C — переключение (преследование / кокпит / капот / ТВ).</summary>
    public class CameraRig : MonoBehaviour
    {
        public enum Mode { Chase, Cockpit, Bonnet, Tv }

        public VehicleController target;
        public Mode mode = Mode.Chase;
        Camera cam;
        Vector3 smoothVelocity;
        Vector3 lookDir;

        void Awake()
        {
            cam = GetComponent<Camera>();
            if (cam == null) cam = gameObject.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 6000f;
        }

        void LateUpdate()
        {
            if (target == null) return;
            if (Input.GetKeyDown(KeyCode.C)) mode = (Mode)(((int)mode + 1) % 4);

            Transform car = target.transform;
            float speed = target.SpeedMs;
            switch (mode)
            {
                case Mode.Chase:
                {
                    cam.fieldOfView = Mathf.Lerp(60f, 72f, Mathf.InverseLerp(10f, 80f, speed));
                    Vector3 fwd = car.forward;
                    fwd.y *= 0.3f;
                    lookDir = Vector3.Slerp(lookDir == Vector3.zero ? fwd : lookDir, fwd, 6f * Time.deltaTime);
                    Vector3 desired = car.position - lookDir.normalized * 6.5f + Vector3.up * 2.1f;
                    transform.position = Vector3.SmoothDamp(transform.position, desired, ref smoothVelocity, 0.06f);
                    transform.rotation = Quaternion.LookRotation(car.position + Vector3.up * 1.0f + lookDir * 4f - transform.position);
                    break;
                }
                case Mode.Cockpit:
                {
                    cam.fieldOfView = 70f;
                    var anchor = car.Find("CockpitCam");
                    Vector3 g = target.LocalAcceleration * 0.0012f; // лёгкое смещение головы от перегрузок
                    transform.position = (anchor != null ? anchor.position : car.position + Vector3.up) - car.right * g.x - car.forward * g.z;
                    transform.rotation = car.rotation;
                    break;
                }
                case Mode.Bonnet:
                    cam.fieldOfView = 65f;
                    transform.position = car.TransformPoint(new Vector3(0f, target.Spec.dimensions.height * 0.72f, target.Spec.dimensions.wheelbase * 0.25f));
                    transform.rotation = car.rotation;
                    break;
                case Mode.Tv:
                {
                    // неподвижная камера у трассы, перескакивает, когда машина уехала далеко
                    if ((transform.position - car.position).magnitude > 120f || transform.position.y < -1000f)
                        transform.position = car.position + car.forward * 70f + car.right * 18f + Vector3.up * 6f;
                    float d = (transform.position - car.position).magnitude;
                    cam.fieldOfView = Mathf.Clamp(2500f / Mathf.Max(d, 1f), 8f, 60f);
                    transform.rotation = Quaternion.LookRotation(car.position + Vector3.up * 0.6f - transform.position);
                    break;
                }
            }
        }

        public void SnapBehind()
        {
            if (target == null) return;
            lookDir = target.transform.forward;
            transform.position = target.transform.position - lookDir * 6.5f + Vector3.up * 2.1f;
            transform.rotation = Quaternion.LookRotation(target.transform.position + Vector3.up - transform.position);
        }
    }
}
