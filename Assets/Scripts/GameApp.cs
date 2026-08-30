using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace JumpingNinja
{
    public sealed class GameApp : MonoBehaviour
    {
        private JumpingNinjaConfig config;
        private UserRepository users;
        private GameObject currentScreen;
        private GameController currentGame;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Time.timeScale = 1f;

            config = Resources.Load<JumpingNinjaConfig>("JumpingNinjaConfig");
            if (config == null)
            {
                Debug.LogWarning("JumpingNinjaConfig was not found. Runtime defaults will be used.");
                config = ScriptableObject.CreateInstance<JumpingNinjaConfig>();
            }

            users = new UserRepository();
            DisableTemplateCameras();
            CreateLetterboxBackgroundCamera();
            EnsureEventSystem();
        }

        private void Start()
        {
            StartCoroutine(LoadingSequence());
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f;
        }

        public void StartRun()
        {
            if (users.ActiveUser == null)
            {
                ShowCreateUser(false);
                return;
            }

            DestroyCurrentScreen();
            DestroyCurrentGame();
            GameObject gameObject = new GameObject("Infinite Run");
            currentGame = gameObject.AddComponent<GameController>();
            currentGame.Initialize(this, config, users);
        }

        public void FinishRun(int score)
        {
            bool isPersonalBest = users.RecordScore(score);
            currentGame?.PresentGameOver(score, isPersonalBest);
        }

        public void RetryRun()
        {
            StartRun();
        }

        public void ReturnToMenu()
        {
            Time.timeScale = 1f;
            DestroyCurrentGame();
            ShowMainMenu();
        }

        private IEnumerator LoadingSequence()
        {
            Transform screen = BeginScreen("Loading Screen", RuntimeUi.Ink);
            AddLogo(screen, new Vector2(520f, 520f), new Vector2(0f, 150f));

            Text loading = RuntimeUi.CreateText("Loading", screen, "LOADING", 38, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            RuntimeUi.Place(loading.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(700f, 90f), new Vector2(0f, -270f));

            float duration = Mathf.Max(0f, config.loadingDuration);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                int dots = 1 + Mathf.FloorToInt(elapsed * 3f) % 3;
                loading.text = "LOADING" + new string('.', dots);
                yield return null;
            }

            if (users.ActiveUser == null)
            {
                ShowCreateUser(false);
            }
            else
            {
                ShowMainMenu();
            }
        }

        private void ShowMainMenu()
        {
            Transform screen = BeginScreen("Main Menu", RuntimeUi.Paper);
            AddLogo(screen, new Vector2(380f, 380f), new Vector2(0f, 520f));

            Text title = RuntimeUi.CreateText("Title", screen, "JUMPING\nNINJA", 92, TextAnchor.MiddleCenter, RuntimeUi.Ink, FontStyle.Bold);
            RuntimeUi.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 260f), new Vector2(0f, 155f));

            UserProfile active = users.ActiveUser;
            string userLine = active == null ? "NO NINJA" : $"{active.name.ToUpperInvariant()}  •  BEST {active.bestScore}";
            Text activeUser = RuntimeUi.CreateText("Active User", screen, userLine, 38, TextAnchor.MiddleCenter, RuntimeUi.Muted, FontStyle.Bold);
            RuntimeUi.Place(activeUser.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 80f), new Vector2(0f, -30f));

            Button start = RuntimeUi.CreateButton("Start", screen, "START RUN", StartRun, RuntimeUi.Red);
            RuntimeUi.Place(start.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(760f, 150f), new Vector2(0f, -210f));

            Button leaderboard = RuntimeUi.CreateButton("Leaderboard", screen, "LEADERBOARD", ShowLeaderboard, RuntimeUi.Ink);
            RuntimeUi.Place(leaderboard.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(760f, 130f), new Vector2(0f, -390f));

            Button switchUser = RuntimeUi.CreateButton("Switch User", screen, "SWITCH NINJA", ShowSwitchUser, RuntimeUi.Ink);
            RuntimeUi.Place(switchUser.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(760f, 130f), new Vector2(0f, -550f));

            Text footer = RuntimeUi.CreateText("Version", screen, "POTATOED MICE  •  V1", 28, TextAnchor.MiddleCenter, RuntimeUi.Muted, FontStyle.Bold);
            RuntimeUi.Place(footer.rectTransform, new Vector2(0.5f, 0f), new Vector2(900f, 80f), new Vector2(0f, 70f));
            RuntimeUi.Select(start);
        }

        private void ShowCreateUser(bool canCancel)
        {
            Transform screen = BeginScreen("Create User", RuntimeUi.Ink);
            Text heading = RuntimeUi.CreateText("Heading", screen, "CREATE YOUR\nNINJA", 86, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            RuntimeUi.Place(heading.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 260f), new Vector2(0f, 430f));

            Text hint = RuntimeUi.CreateText("Hint", screen, "Your score is saved on this device.", 36, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f));
            RuntimeUi.Place(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 80f), new Vector2(0f, 230f));

            InputField input = RuntimeUi.CreateInputField("Name Input", screen, "Ninja name");
            RuntimeUi.Place(input.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(820f, 130f), new Vector2(0f, 40f));

            Text error = RuntimeUi.CreateText("Error", screen, string.Empty, 32, TextAnchor.MiddleCenter, new Color(1f, 0.55f, 0.45f, 1f), FontStyle.Bold);
            RuntimeUi.Place(error.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 80f), new Vector2(0f, -70f));

            void CreateUser()
            {
                if (users.TryCreateUser(input.text, out string message))
                {
                    ShowMainMenu();
                }
                else
                {
                    error.text = message;
                }
            }

            Button create = RuntimeUi.CreateButton("Create", screen, "CREATE", CreateUser, RuntimeUi.Red);
            RuntimeUi.Place(create.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(820f, 140f), new Vector2(0f, -220f));
            if (canCancel)
            {
                Button back = RuntimeUi.CreateButton("Back", screen, "BACK", ShowSwitchUser, RuntimeUi.Muted);
                RuntimeUi.Place(back.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(820f, 120f), new Vector2(0f, -390f));
            }
            RuntimeUi.Select(create);
        }

        private void ShowLeaderboard()
        {
            Transform screen = BeginScreen("Leaderboard", RuntimeUi.Paper);
            Text heading = RuntimeUi.CreateText("Heading", screen, "LEADERBOARD", 78, TextAnchor.MiddleCenter, RuntimeUi.Ink, FontStyle.Bold);
            RuntimeUi.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(960f, 160f), new Vector2(0f, -170f));

            List<UserProfile> leaderboard = users.GetLeaderboard();
            StringBuilder builder = new StringBuilder();
            int count = Mathf.Min(leaderboard.Count, 12);
            for (int index = 0; index < count; index++)
            {
                UserProfile user = leaderboard[index];
                string marker = user.id == users.ActiveUser?.id ? "  ◀" : string.Empty;
                builder.Append(index + 1).Append(".  ")
                    .Append(user.name)
                    .Append("     ")
                    .Append(user.bestScore)
                    .Append(marker)
                    .AppendLine();
            }

            if (count == 0)
            {
                builder.Append("No scores yet.");
            }

            Text scores = RuntimeUi.CreateText("Scores", screen, builder.ToString(), 48, TextAnchor.UpperLeft, RuntimeUi.Ink, FontStyle.Bold);
            scores.lineSpacing = 1.25f;
            RuntimeUi.Place(scores.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(850f, 1180f), new Vector2(0f, -30f));

            Button back = RuntimeUi.CreateButton("Back", screen, "BACK", ShowMainMenu, RuntimeUi.Red);
            RuntimeUi.Place(back.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(760f, 130f), new Vector2(0f, 130f));
            RuntimeUi.Select(back);
        }

        private void ShowSwitchUser()
        {
            Transform screen = BeginScreen("Switch User", RuntimeUi.Paper);
            Text heading = RuntimeUi.CreateText("Heading", screen, "CHOOSE NINJA", 76, TextAnchor.MiddleCenter, RuntimeUi.Ink, FontStyle.Bold);
            RuntimeUi.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(960f, 160f), new Vector2(0f, -160f));

            List<UserProfile> leaderboard = users.GetLeaderboard();
            int visibleCount = Mathf.Min(leaderboard.Count, 8);
            Button initialSelection = null;
            for (int index = 0; index < visibleCount; index++)
            {
                UserProfile profile = leaderboard[index];
                string label = $"{profile.name}   •   {profile.bestScore}";
                Color color = profile.id == users.ActiveUser?.id ? RuntimeUi.Red : RuntimeUi.Ink;
                Button userButton = RuntimeUi.CreateButton($"User {index}", screen, label, () =>
                {
                    users.SetActiveUser(profile.id);
                    ShowMainMenu();
                }, color);
                RuntimeUi.Place(userButton.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(820f, 115f), new Vector2(0f, -300f - index * 135f));
                if (initialSelection == null || profile.id == users.ActiveUser?.id)
                {
                    initialSelection = userButton;
                }
            }

            Button create = RuntimeUi.CreateButton("New User", screen, "NEW NINJA", () => ShowCreateUser(true), RuntimeUi.Red);
            RuntimeUi.Place(create.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(820f, 120f), new Vector2(0f, 260f));

            Button back = RuntimeUi.CreateButton("Back", screen, "BACK", ShowMainMenu, RuntimeUi.Muted);
            RuntimeUi.Place(back.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(820f, 120f), new Vector2(0f, 110f));
            RuntimeUi.Select(initialSelection != null ? initialSelection : create);
        }

        private Transform BeginScreen(string name, Color backgroundColor)
        {
            DestroyCurrentScreen();
            Canvas canvas = RuntimeUi.CreateCanvas(name);
            currentScreen = canvas.gameObject;
            Transform content = RuntimeUi.Content(canvas);
            Image background = RuntimeUi.CreateImage("Background", content, backgroundColor);
            RuntimeUi.Stretch(background.rectTransform);
            background.transform.SetAsFirstSibling();
            return content;
        }

        private void AddLogo(Transform parent, Vector2 size, Vector2 position)
        {
            if (config.logo == null)
            {
                return;
            }

            Image logo = RuntimeUi.CreateImage("Potatoed Mice Logo", parent, Color.white);
            logo.sprite = config.logo;
            logo.preserveAspect = true;
            logo.raycastTarget = false;
            RuntimeUi.Place(logo.rectTransform, new Vector2(0.5f, 0.5f), size, position);
        }

        private void DestroyCurrentScreen()
        {
            if (currentScreen != null)
            {
                Destroy(currentScreen);
                currentScreen = null;
            }
        }

        private void DestroyCurrentGame()
        {
            if (currentGame != null)
            {
                Destroy(currentGame.gameObject);
                currentGame = null;
            }
        }

        private static void DisableTemplateCameras()
        {
            Camera[] cameras = FindObjectsByType<Camera>();
            foreach (Camera sceneCamera in cameras)
            {
                sceneCamera.gameObject.SetActive(false);
            }
        }

        private void CreateLetterboxBackgroundCamera()
        {
            GameObject cameraObject = new GameObject("Letterbox Background Camera", typeof(Camera));
            cameraObject.transform.SetParent(transform, false);
            Camera backgroundCamera = cameraObject.GetComponent<Camera>();
            backgroundCamera.clearFlags = CameraClearFlags.SolidColor;
            backgroundCamera.backgroundColor = Color.black;
            backgroundCamera.cullingMask = 0;
            backgroundCamera.depth = -100f;
            backgroundCamera.allowHDR = false;
            backgroundCamera.allowMSAA = false;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            GameObject eventSystemObject = new GameObject("Event System", typeof(EventSystem));
            InputSystemUIInputModule inputModule = eventSystemObject.AddComponent<InputSystemUIInputModule>();
            inputModule.AssignDefaultActions();
        }
    }
}
