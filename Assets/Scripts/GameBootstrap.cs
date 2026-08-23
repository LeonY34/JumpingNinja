using UnityEngine;

namespace JumpingNinja
{
    internal static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartGame()
        {
            if (Object.FindAnyObjectByType<GameApp>() != null)
            {
                return;
            }

            GameObject appObject = new GameObject("Jumping Ninja V1");
            appObject.AddComponent<GameApp>();
        }
    }
}
