using UnityEngine;

namespace RacingSim.Track
{
    /// <summary>
    /// Хронометраж: позиция на трассе (ближайшая точка центральной линии), 3 сектора,
    /// текущий / последний / лучший круг. Круг засчитывается при пересечении старта
    /// после прохождения > 90% дистанции.
    /// </summary>
    public class LapTimer
    {
        readonly TrackData track;
        int index = -1;
        float maxProgress;
        int sector;

        public int Lap { get; private set; }
        public float CurrentLapTime { get; private set; }
        public float LastLapTime { get; private set; } = -1f;
        public float BestLapTime { get; private set; } = -1f;
        public float[] CurrentSectors { get; } = { -1f, -1f, -1f };
        public float[] BestSectors { get; } = { -1f, -1f, -1f };
        public float Distance => index >= 0 ? track.distance[index] : 0f;
        public int Index => index;
        public bool Started { get; private set; }

        public LapTimer(TrackData t) { track = t; }

        public void Reset()
        {
            index = -1;
            Started = false;
            CurrentLapTime = 0f;
            maxProgress = 0f;
            sector = 0;
            for (int i = 0; i < 3; i++) CurrentSectors[i] = -1f;
        }

        /// <summary>Сброс после «вернуть на трассу» — текущий круг не засчитывается.</summary>
        public void Invalidate()
        {
            Started = false;
            maxProgress = 0f;
        }

        public void Update(Vector3 carPos, float dt)
        {
            int prev = index;
            index = track.NearestIndex(carPos, index);
            if (Started) CurrentLapTime += dt;
            if (prev < 0) return;

            float progress = track.distance[index] / track.length;
            int n = track.Count;
            bool crossedStart = prev > n * 0.8f && index < n * 0.2f;

            if (crossedStart)
            {
                if (Started && maxProgress > 0.9f)
                {
                    CloseSector(2);
                    LastLapTime = CurrentLapTime;
                    if (BestLapTime < 0f || LastLapTime < BestLapTime) BestLapTime = LastLapTime;
                    Lap++;
                }
                Started = true;
                CurrentLapTime = 0f;
                maxProgress = 0f;
                sector = 0;
                for (int i = 0; i < 3; i++) CurrentSectors[i] = -1f;
                return;
            }

            // движение назад через старт не считается
            if (Started && progress < 0.5f) maxProgress = Mathf.Max(maxProgress, progress);
            else if (Started && maxProgress > 0.4f) maxProgress = Mathf.Max(maxProgress, progress);

            if (Started && sector < 2 && progress > (sector + 1) / 3f && progress < (sector + 1) / 3f + 0.1f)
            {
                CloseSector(sector);
                sector++;
            }
        }

        void CloseSector(int s)
        {
            float prevSum = 0f;
            for (int i = 0; i < s; i++) prevSum += Mathf.Max(0f, CurrentSectors[i]);
            CurrentSectors[s] = CurrentLapTime - prevSum;
            if (BestSectors[s] < 0f || CurrentSectors[s] < BestSectors[s]) BestSectors[s] = CurrentSectors[s];
        }

        public static string Format(float t)
        {
            if (t < 0f) return "--:--.---";
            int m = (int)(t / 60f);
            float s = t - m * 60f;
            return $"{m}:{s:00.000}";
        }
    }
}
