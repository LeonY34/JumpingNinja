using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace JumpingNinja
{
    public sealed class GameController : MonoBehaviour
    {
        private sealed class RecordTarget
        {
            public string id;
            public string name;
            public int score;
        }

        private readonly List<RecordTarget> recordTargets = new List<RecordTarget>();
        private readonly HashSet<string> announcedTargets = new HashSet<string>();
        private readonly Queue<string> notificationQueue = new Queue<string>();

        private GameApp app;
        private JumpingNinjaConfig config;
        private UserRepository users;
        private WorldGenerator world;
        private NinjaController ninja;
        private Canvas gameCanvas;
        private Transform gameContent;
        private Button pauseButton;
        private Text scoreText;
        private Text nextTargetText;
        private GameObject pauseOverlay;
        private Coroutine notificationRoutine;
        private int highestLevel;
        private int initialPersonalBest;
        private bool personalBestAnnounced;
        private bool paused;
        private bool countingDown;
        private bool dead;

        public bool AcceptsInput => !paused && !countingDown && !dead;

        public void Initialize(GameApp owner, JumpingNinjaConfig gameConfig, UserRepository userRepository)
        {
            app = owner;
            config = gameConfig;
            users = userRepository;
            Time.timeScale = 1f;

            CaptureRecordTargets();
            CreateWorld();
            CreateNinja();
            Physics2D.SyncTransforms();
            CreateCamera();
            CreateHud();
            UpdateScoreDisplay();
            StartCoroutine(ShowOpeningHint());
        }

        private void Update()
        {
            if (dead || ninja == null)
            {
                return;
            }

            HandleKeyboardInput();

            if (ninja.Position.y < -2f)
            {
                KillPlayer();
                return;
            }

            int level = Mathf.Max(0, Mathf.FloorToInt(ninja.Position.y / config.SafeLayerHeight));
            if (level > highestLevel)
            {
                int previous = highestLevel;
                highestLevel = level;
                world.EnsureGeneratedThrough(highestLevel + config.generateAheadLayers);
                CheckRecords(previous, highestLevel);
                UpdateScoreDisplay();
            }
        }

        private void HandleKeyboardInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (!AcceptsInput || keyboard == null)
            {
                return;
            }

            bool leftPressed = keyboard.aKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame;
            bool rightPressed = keyboard.dKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame;
            if (leftPressed == rightPressed)
            {
                return;
            }

            ninja.Steer(rightPressed);
        }

        public void KillPlayer()
        {
            if (dead)
            {
                return;
            }

            dead = true;
            paused = false;
            countingDown = false;
            Time.timeScale = 1f;
            float animationDuration = ninja != null ? ninja.StopForDeath() : 0f;
            StartCoroutine(FinishRunAfterDeathAnimation(animationDuration));
        }

        private IEnumerator FinishRunAfterDeathAnimation(float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            app.FinishRun(highestLevel);
        }

        public void PresentGameOver(int score, bool isPersonalBest)
        {
            Image overlay = RuntimeUi.CreateImage("Game Over", gameContent, new Color(RuntimeUi.Ink.r, RuntimeUi.Ink.g, RuntimeUi.Ink.b, 0.96f));
            RuntimeUi.Stretch(overlay.rectTransform);

            Text heading = RuntimeUi.CreateText("Heading", overlay.transform, "RUN OVER", 92, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            RuntimeUi.Place(heading.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 180f), new Vector2(0f, 360f));

            Text result = RuntimeUi.CreateText("Result", overlay.transform, $"LEVEL {score}", 116, TextAnchor.MiddleCenter, RuntimeUi.Red, FontStyle.Bold);
            RuntimeUi.Place(result.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 180f), new Vector2(0f, 150f));

            string subline = isPersonalBest ? "NEW PERSONAL BEST" : $"BEST  {users.ActiveUser.bestScore}";
            Text best = RuntimeUi.CreateText("Best", overlay.transform, subline, 40, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            RuntimeUi.Place(best.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 100f), new Vector2(0f, 5f));

            Button retry = RuntimeUi.CreateButton("Retry", overlay.transform, "RETRY", app.RetryRun, RuntimeUi.Red);
            RuntimeUi.Place(retry.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(760f, 140f), new Vector2(0f, -220f));

            Button menu = RuntimeUi.CreateButton("Menu", overlay.transform, "MAIN MENU", app.ReturnToMenu, RuntimeUi.Muted);
            RuntimeUi.Place(menu.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(760f, 125f), new Vector2(0f, -390f));
            RuntimeUi.Select(retry);
        }

        private void CreateWorld()
        {
            GameObject worldObject = new GameObject("Generated World");
            worldObject.transform.SetParent(transform, false);
            world = worldObject.AddComponent<WorldGenerator>();
            world.Initialize(config);
            world.EnsureGeneratedThrough(config.generateAheadLayers);
        }

        private void CreateNinja()
        {
            GameObject ninjaObject = new GameObject("Ninja");
            ninjaObject.transform.SetParent(transform, false);
            ninjaObject.transform.position = new Vector3(config.SafeMapWidth * 0.5f, config.playerStartY, 0f);
            ninja = ninjaObject.AddComponent<NinjaController>();
            ninja.Initialize(this, config, world.SolidSprite, world.FrictionlessMaterial);
        }

        private void CreateCamera()
        {
            GameObject cameraObject = new GameObject("Game Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.transform.SetParent(transform, false);
            cameraObject.tag = "MainCamera";
            Camera cameraComponent = cameraObject.GetComponent<Camera>();
            cameraComponent.orthographic = true;
            cameraComponent.clearFlags = CameraClearFlags.SolidColor;
            cameraComponent.backgroundColor = config.worldBackground;
            cameraComponent.nearClipPlane = 0.1f;
            cameraComponent.farClipPlane = 100f;
            cameraComponent.allowHDR = false;
            cameraComponent.allowMSAA = false;
            cameraObject.AddComponent<PortraitViewport>();

            CameraFollower follower = cameraObject.AddComponent<CameraFollower>();
            follower.Initialize(cameraComponent, ninja.transform, config);
        }

        private void CreateHud()
        {
            gameCanvas = RuntimeUi.CreateCanvas("Game HUD", 10);
            gameCanvas.transform.SetParent(transform, false);
            gameContent = RuntimeUi.Content(gameCanvas);

            CreateInputZone("Left Input", new Vector2(0f, 0f), new Vector2(0.5f, 1f), () => ninja.Steer(false));
            CreateInputZone("Right Input", new Vector2(0.5f, 0f), new Vector2(1f, 1f), () => ninja.Steer(true));

            Image scorePanel = RuntimeUi.CreateImage("Score Panel", gameContent, new Color(0.04f, 0.05f, 0.07f, 0.82f));
            scorePanel.raycastTarget = false;
            RuntimeUi.Place(scorePanel.rectTransform, new Vector2(0f, 1f), new Vector2(570f, 190f), new Vector2(315f, -135f));

            scoreText = RuntimeUi.CreateText("Score", scorePanel.transform, "LEVEL 0", 54, TextAnchor.MiddleLeft, Color.white, FontStyle.Bold);
            scoreText.rectTransform.anchorMin = new Vector2(0f, 0.46f);
            scoreText.rectTransform.anchorMax = Vector2.one;
            scoreText.rectTransform.offsetMin = new Vector2(34f, 0f);
            scoreText.rectTransform.offsetMax = new Vector2(-20f, -8f);

            nextTargetText = RuntimeUi.CreateText("Next Target", scorePanel.transform, string.Empty, 28, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.72f), FontStyle.Bold);
            nextTargetText.rectTransform.anchorMin = Vector2.zero;
            nextTargetText.rectTransform.anchorMax = new Vector2(1f, 0.46f);
            nextTargetText.rectTransform.offsetMin = new Vector2(34f, 5f);
            nextTargetText.rectTransform.offsetMax = new Vector2(-20f, 0f);

            pauseButton = RuntimeUi.CreateButton("Pause", gameContent, "II", Pause, RuntimeUi.Red);
            RuntimeUi.Place(pauseButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(130f, 130f), new Vector2(-95f, -105f));

            Text leftHint = RuntimeUi.CreateText("Left Hint", gameContent, "<  TAP", 32, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.6f), FontStyle.Bold);
            RuntimeUi.Place(leftHint.rectTransform, new Vector2(0f, 0f), new Vector2(350f, 80f), new Vector2(205f, 90f));

            Text rightHint = RuntimeUi.CreateText("Right Hint", gameContent, "TAP  >", 32, TextAnchor.MiddleRight, new Color(1f, 1f, 1f, 0.6f), FontStyle.Bold);
            RuntimeUi.Place(rightHint.rectTransform, new Vector2(1f, 0f), new Vector2(350f, 80f), new Vector2(-205f, 90f));
            RuntimeUi.Select(pauseButton);
        }

        private void CreateInputZone(string name, Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction onClick)
        {
            Image image = RuntimeUi.CreateImage(name, gameContent, Color.clear);
            image.raycastTarget = true;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            EventTrigger trigger = image.gameObject.AddComponent<EventTrigger>();
            EventTrigger.Entry click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            click.callback.AddListener(_ => onClick?.Invoke());
            trigger.triggers.Add(click);
        }

        private void CaptureRecordTargets()
        {
            UserProfile active = users.ActiveUser;
            initialPersonalBest = active?.bestScore ?? 0;
            foreach (UserProfile profile in users.Users)
            {
                if (profile.id == active?.id)
                {
                    continue;
                }

                recordTargets.Add(new RecordTarget
                {
                    id = profile.id,
                    name = profile.name,
                    score = profile.bestScore
                });
            }
        }

        private void CheckRecords(int previousScore, int newScore)
        {
            if (!personalBestAnnounced && previousScore <= initialPersonalBest && newScore > initialPersonalBest)
            {
                personalBestAnnounced = true;
                QueueNotification("NEW PERSONAL BEST!");
            }

            foreach (RecordTarget target in recordTargets)
            {
                if (!announcedTargets.Contains(target.id) && previousScore <= target.score && newScore > target.score)
                {
                    announcedTargets.Add(target.id);
                    QueueNotification($"PASSED {target.name.ToUpperInvariant()}!");
                }
            }
        }

        private void UpdateScoreDisplay()
        {
            scoreText.text = $"LEVEL {highestLevel}";
            UserProfile nextTarget = users.GetNextTarget(highestLevel);
            nextTargetText.text = nextTarget == null
                ? "TOP OF THE BOARD"
                : $"NEXT  {nextTarget.name.ToUpperInvariant()}  {nextTarget.bestScore}";
        }

        private void Pause()
        {
            if (!AcceptsInput)
            {
                return;
            }

            paused = true;
            Time.timeScale = 0f;
            Image overlay = RuntimeUi.CreateImage("Pause Overlay", gameContent, new Color(RuntimeUi.Ink.r, RuntimeUi.Ink.g, RuntimeUi.Ink.b, 0.94f));
            RuntimeUi.Stretch(overlay.rectTransform);
            pauseOverlay = overlay.gameObject;

            Text heading = RuntimeUi.CreateText("Heading", overlay.transform, "PAUSED", 96, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            RuntimeUi.Place(heading.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 180f), new Vector2(0f, 230f));

            Button resume = RuntimeUi.CreateButton("Resume", overlay.transform, "RESUME", BeginResumeCountdown, RuntimeUi.Red);
            RuntimeUi.Place(resume.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(760f, 140f), new Vector2(0f, 0f));

            Button menu = RuntimeUi.CreateButton("Menu", overlay.transform, "MAIN MENU", app.ReturnToMenu, RuntimeUi.Muted);
            RuntimeUi.Place(menu.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(760f, 125f), new Vector2(0f, -180f));
            RuntimeUi.Select(resume);
        }

        private void BeginResumeCountdown()
        {
            if (!paused || countingDown)
            {
                return;
            }

            countingDown = true;
            if (pauseOverlay != null)
            {
                Destroy(pauseOverlay);
                pauseOverlay = null;
            }

            StartCoroutine(ResumeCountdown());
        }

        private IEnumerator ResumeCountdown()
        {
            Image veil = RuntimeUi.CreateImage("Resume Countdown", gameContent, new Color(0f, 0f, 0f, 0.5f));
            RuntimeUi.Stretch(veil.rectTransform);
            Text countdown = RuntimeUi.CreateText("Countdown", veil.transform, "3", 190, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            RuntimeUi.Stretch(countdown.rectTransform);

            for (int number = 3; number >= 1; number--)
            {
                countdown.text = number.ToString();
                yield return new WaitForSecondsRealtime(1f);
            }

            countdown.text = "GO!";
            yield return new WaitForSecondsRealtime(0.45f);
            Destroy(veil.gameObject);
            paused = false;
            countingDown = false;
            Time.timeScale = 1f;
            RuntimeUi.Select(pauseButton);
        }

        private IEnumerator ShowOpeningHint()
        {
            Image card = RuntimeUi.CreateImage("Opening Hint", gameContent, new Color(0.04f, 0.05f, 0.07f, 0.78f));
            card.raycastTarget = false;
            RuntimeUi.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(820f, 210f), new Vector2(0f, -390f));
            Text message = RuntimeUi.CreateText("Message", card.transform, "TAP LEFT OR RIGHT\nTO JUMP", 44, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            RuntimeUi.Stretch(message.rectTransform);
            yield return new WaitForSeconds(2.2f);
            Destroy(card.gameObject);
        }

        private void QueueNotification(string message)
        {
            notificationQueue.Enqueue(message);
            if (notificationRoutine == null)
            {
                notificationRoutine = StartCoroutine(PlayNotifications());
            }
        }

        private IEnumerator PlayNotifications()
        {
            while (notificationQueue.Count > 0)
            {
                string message = notificationQueue.Dequeue();
                Image card = RuntimeUi.CreateImage("Record Notification", gameContent, RuntimeUi.Red);
                card.raycastTarget = false;
                RuntimeUi.Place(card.rectTransform, new Vector2(0.5f, 1f), new Vector2(760f, 110f), new Vector2(0f, -270f));
                Text label = RuntimeUi.CreateText("Label", card.transform, message, 36, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
                RuntimeUi.Stretch(label.rectTransform);
                yield return new WaitForSecondsRealtime(config.notificationDuration);
                Destroy(card.gameObject);
            }

            notificationRoutine = null;
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f;
        }
    }
}
