using UnityEngine;

namespace RacingSim.Vehicle
{
    /// <summary>
    /// Процедурный звук двигателя без сэмплов: сумма гармоник частоты вспышек
    /// (обороты/60 × цилиндры/2) + шум впуска; громкость от нагрузки. Плюс визг шин.
    /// Синтез в OnAudioFilterRead (аудиопоток).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class EngineAudio : MonoBehaviour
    {
        public VehicleController car;
        public int cylinders = 8;

        volatile float rpm = 1000f;
        volatile float load;
        volatile float skid;
        double phase, phase2, skidPhase;
        float sampleRate;
        uint noiseState = 22222;
        float lpf;

        void Start()
        {
            sampleRate = AudioSettings.outputSampleRate;
            var src = GetComponent<AudioSource>();
            src.playOnAwake = true;
            src.loop = true;
            src.spatialBlend = 0f;
            src.volume = 0.5f;
            if (!src.isPlaying) src.Play();
        }

        void Update()
        {
            if (car == null || car.Drivetrain == null) return;
            rpm = car.Drivetrain.EngineRpm;
            load = car.Drivetrain.ThrottleActual;
            float s = 0f;
            foreach (var w in car.Wheels)
                if (w.grounded) s = Mathf.Max(s, Mathf.InverseLerp(1.1f, 2.5f, w.combinedSlip) * Mathf.Clamp01(car.SpeedMs / 10f));
            skid = s;
        }

        float Noise()
        {
            noiseState ^= noiseState << 13;
            noiseState ^= noiseState >> 17;
            noiseState ^= noiseState << 5;
            return (noiseState & 0xFFFF) / 32768f - 1f;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (sampleRate <= 0f) return;
            float firing = Mathf.Max(rpm, 300f) / 60f * cylinders * 0.5f;
            float amp = 0.18f + 0.25f * load;
            float dt = 1f / sampleRate;
            for (int i = 0; i < data.Length; i += channels)
            {
                phase += firing * dt;
                phase2 += firing * 0.5f * dt;
                if (phase > 1.0) phase -= 1.0;
                if (phase2 > 1.0) phase2 -= 1.0;
                float p = (float)phase * Mathf.PI * 2f;
                float p2 = (float)phase2 * Mathf.PI * 2f;
                float engine = Mathf.Sin(p) * 0.6f + Mathf.Sin(2f * p) * 0.25f + Mathf.Sin(3f * p) * 0.12f + Mathf.Sin(p2) * 0.3f;
                // «рычание» — пилообразная составляющая с шумом под нагрузкой
                engine += ((float)phase * 2f - 1f) * 0.15f * load;
                lpf += (Noise() - lpf) * 0.2f;
                engine += lpf * 0.25f * load;

                skidPhase += 1100.0 * dt * (1.0 + 0.05 * Noise());
                float tyre = Mathf.Sin((float)(skidPhase * Mathf.PI * 2.0)) * 0.5f + Noise() * 0.15f;

                float sample = engine * amp + tyre * skid * 0.2f;
                for (int c = 0; c < channels; c++) data[i + c] = sample;
            }
        }
    }
}
