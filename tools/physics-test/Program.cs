using System;
using System.IO;
using RacingSim.Vehicle;
using UnityEngine;

// Быстрый тест физики без Unity: разгон 0-100/0-200, макс. скорость, торможение.
// Запуск: dotnet run   (из папки tools/physics-test)
// Продольная модель: 2 оси, перенос нагрузки, аэро, реальные Drivetrain + TireModel,
// логика колеса скопирована из Wheel.UpdateTire / IntegrateSpin.
class W { public TyreData ty; public TireModel tm; public float omega, kappa, drive, brake, extraI, load; }
static class Sim {
  static void Main(string[] a) {
    string dir = a.Length > 0 ? a[0] : Path.Combine("..", "..", "RacingSim", "Assets", "Resources", "Cars");
    foreach (var f in Directory.GetFiles(dir, "*.json")) Run(Path.GetFileNameWithoutExtension(f), dir);
  }
  static void Run(string id, string dir) {
    var spec = System.Text.Json.JsonSerializer.Deserialize<CarSpec>(File.ReadAllText(Path.Combine(dir, id + ".json")), new System.Text.Json.JsonSerializerOptions{IncludeFields=true});
    var dtn = new Drivetrain(spec.engine, spec.gearbox, spec.differential);
    float m = spec.mass.mass, g = 9.81f, dt = 0.002f, v = 0f, x = 0f;
    var F = new W{ty=spec.tyreFront, tm=new TireModel(spec.tyreFront)};
    var R1 = new W{ty=spec.tyreRear, tm=new TireModel(spec.tyreRear)};
    var R2 = new W{ty=spec.tyreRear, tm=new TireModel(spec.tyreRear)};
    var wl = new Wheel("RL", false, true, Vector3.zero, spec.suspensionRear, spec.tyreRear);
    var wr = new Wheel("RR", false, false, Vector3.zero, spec.suspensionRear, spec.tyreRear);
    float t = 0, t100 = -1, t200 = -1, maxV = 0; float ax = 0; int shifts = 0, lastGear = 1;
    float h = spec.mass.cgHeight, wb = spec.dimensions.wheelbase, wdf = spec.mass.weightDistributionFront;
    string phase = "accel"; float brakeStartV = 0, brakeDist = 0, brakeT0 = 0;
    float maxKappaOsc = 0; float cool = 0;
    for (int step = 0; step < 500 * 120; step++) {
      t += dt;
      float throttle = phase == "accel" ? 1f : 0f; float brake = phase == "brake" ? 1f : 0f;
      if (phase == "accel" && t > 60f) { phase = "brake"; brakeStartV = v; brakeT0 = t; brakeDist = x; }
      if (phase == "brake" && v < 0.3f) { Console.WriteLine($"  braking {brakeStartV*3.6f:0} km/h -> 0: {x-brakeDist:0} m, {t-brakeT0:0.00} s"); phase = "hold"; }
      if (phase == "hold" && t > brakeT0 + 15f) break;
      // auto shift (как в VehicleController)
      cool -= dt;
      if (!dtn.IsShifting && dtn.Gear > 0 && cool <= 0f) {
        float driven = R1.omega; bool spin = R1.kappa > 0.25f; float toRpm = 60f / (2f*Mathf.PI);
        float rpmNow = driven * dtn.GearRatio(dtn.Gear) * toRpm;
        if (dtn.Gear < dtn.GearCount && rpmNow > dtn.LimiterRpm * 0.965f && !spin) { if (dtn.RequestGear(dtn.Gear + 1)) cool = 0.3f; }
        else if (dtn.Gear > 1) { float lower = driven * dtn.GearRatio(dtn.Gear - 1) * toRpm; float downAt = brake > 0.2f ? dtn.LimiterRpm*0.8f : dtn.LimiterRpm*0.62f; if (lower < downAt && dtn.RequestGear(dtn.Gear - 1)) cool = 0.25f; }
      }
      if (dtn.Gear != lastGear) { shifts++; lastGear = dtn.Gear; }
      // нагрузки
      float q = 0.5f * spec.aero.airDensity * v * v; float down = q * spec.aero.downforceArea; float drag = q * spec.aero.dragArea;
      float transfer = m * ax * h / wb;
      float front = (m * g * wdf + down * spec.aero.balanceFront - transfer) / 2f;
      float rear = (m * g * (1 - wdf) + down * (1 - spec.aero.balanceFront) + transfer) / 2f;
      F.load = Mathf.Max(0, front); R1.load = R2.load = Mathf.Max(0, rear);
      // тормоза + ABS lvl2
      float bt = brake * spec.brakes.maxTorqueTotal;
      F.brake = bt * spec.brakes.biasFront * 0.5f; R1.brake = R2.brake = bt * (1 - spec.brakes.biasFront) * 0.5f;
      foreach (var w in new[]{F,R1}) if (brake > 0.05f && w.kappa < -0.13f && v > 3f) w.brake *= 0.25f;
      R2.brake = R1.brake;
      // TC lvl2
      float thr = throttle; if (R1.kappa > 0.10f && v > 2f) thr *= 1f - 0.9f * Mathf.Clamp01((R1.kappa - 0.10f) / 0.08f);
      // trans: wheels as separate objects, R2 mirrors R1
      wl.angularVelocity = R1.omega; wr.angularVelocity = R2.omega;
      dtn.Step(thr, wl, wr, dt);
      R1.drive = wl.driveTorque; R2.drive = wr.driveTorque; R1.extraI = R2.extraI = wl.extraInertia; F.drive = 0; F.extraI = 0;
      float fx = 0;
      foreach (var w in new[]{F, R1, R2}) {
        float k = dt / w.ty.relaxationLength; float sp = Mathf.Max(Mathf.Abs(v), 0.5f);
        w.kappa = Mathf.Clamp((w.kappa + k * (w.omega * w.ty.radius - v)) / (1f + k * sp), -3f, 3f);
        w.tm.ComputeForces(w.kappa, 0f, w.load, 1f, out float f, out _, out _);
        float rr = w.ty.rollingResistance * w.load * Mathf.Clamp(v, -1f, 1f);
        float mult = (w == F) ? 2f : 1f;
        fx += (f - rr) * mult;
        float I = w.ty.inertia + w.extraI;
        w.omega += (w.drive - f * w.ty.radius) / I * dt;
        float bd = w.brake / I * dt; if (Mathf.Abs(w.omega) <= bd) w.omega = 0; else w.omega -= Mathf.Sign(w.omega) * bd;
        if (phase == "hold") maxKappaOsc = Mathf.Max(maxKappaOsc, Mathf.Abs(w.kappa));
      }
      fx -= drag * Mathf.Sign(v);
      ax = fx / m; v += ax * dt; x += v * dt;
      if (float.IsNaN(v)) { Console.WriteLine("  NaN!"); return; }
      maxV = Mathf.Max(maxV, v);
      if (t100 < 0 && v >= 100/3.6f) t100 = t; if (t200 < 0 && v >= 200/3.6f) t200 = t;
      if (step % 2500 == 0 && phase=="accel" && t < 12) Console.Write($"[{t:0.0}s {v*3.6f:0}kmh g{dtn.Gear} {dtn.EngineRpm:0}rpm k{R1.kappa:0.00}] ");
    }
    Console.WriteLine($"\n{id}: 0-100 {t100:0.00}s, 0-200 {t200:0.00}s, vmax {maxV*3.6f:0} km/h, shifts {shifts}, final v {v:0.000} m/s, hold kappa {maxKappaOsc:0.000}");
  }
}
