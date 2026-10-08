using UnityEngine;

namespace RacingSim.Core
{
    /// <summary>
    /// Автозапуск: при нажатии Play в любой сцене (даже пустой) создаётся RaceSession с меню.
    /// Поэтому проекту не нужны заранее собранные .unity-сцены.
    /// </summary>
    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindAnyObjectByType<RaceSession>() != null) return;
            var go = new GameObject("RacingSim");
            go.AddComponent<RaceSession>();
        }
    }
}
