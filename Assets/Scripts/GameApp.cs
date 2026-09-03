using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace JumpingNinja
{
    public sealed class GameApp : MonoBehaviour
    {
        private JumpingNinjaConfig config;
        private UserRepository legacyUsers;
        private OnlineNinjaRepository ninjas;
        private GameObject currentScreen;
        private GameController currentGame;
        private AuthApiClient authApi;
        private OnlineAuthSession authSession;
        private bool authRequestInFlight;
        private bool onlineDataRequestInFlight;
        private int onlineSessionGeneration;
        private LeaderboardPayload leaderboardCache;
        private LeaderboardTargetsPayload targetsCache;
        private Coroutine scoreSyncRoutine;
        private Coroutine onlineRefreshRoutine;
        private Coroutine onlineTargetsRefreshRoutine;

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

            legacyUsers = new UserRepository();
            ninjas = new OnlineNinjaRepository(legacyUsers);
            authApi = new AuthApiClient(config.authApiBaseUrl, config.authRequestTimeoutSeconds);
            authSession = new OnlineAuthSession();
            DisableTemplateCameras();
            CreateLetterboxBackgroundCamera();
            EnsureEventSystem();
        }

        private void Start()
        {
            StartCoroutine(LoadingSequence());
        }

        private void Update()
        {
            if (authSession != null &&
                authSession.HasSession &&
                !authSession.IsAuthenticated)
            {
                Time.timeScale = 1f;
                authSession.Clear();
                ninjas.ClearActiveAccount();
                DestroyCurrentGame();
                ShowLogin();
            }
        }

        private void OnDestroy()
        {
            authSession?.Clear();
            Time.timeScale = 1f;
        }

        public void StartRun()
        {
            if (!authSession.IsAuthenticated)
            {
                authSession.Clear();
                ShowLogin();
                return;
            }

            if (ninjas.ActiveNinja == null)
            {
                ShowCreateNinja(false);
                return;
            }

            DestroyCurrentScreen();
            DestroyCurrentGame();
            GameObject gameObject = new GameObject("Infinite Run");
            currentGame = gameObject.AddComponent<GameController>();
            currentGame.Initialize(this, config, ninjas, targetsCache);
        }

        public void FinishRun(int score)
        {
            OnlineScoreRecord record = ninjas.RecordLocalScore(score);
            GameController game = currentGame;
            game?.PresentGameOver(score, record.ninjaImproved, record.accountImproved);
            if (record.ninjaImproved && !string.IsNullOrEmpty(record.ninjaId))
            {
                if (scoreSyncRoutine != null)
                {
                    StopCoroutine(scoreSyncRoutine);
                }

                scoreSyncRoutine = StartCoroutine(
                    SubmitScoreAfterRun(record.ninjaId, record.score, game));
            }
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

            ShowLogin();
        }

        private void ContinueAfterAuthentication()
        {
            authRequestInFlight = false;
            StartCoroutine(PrepareOnlineAccount());
        }

        private void ShowLogin(string initialError = null)
        {
            Transform screen = BeginScreen("Online Login", RuntimeUi.Ink);
            Text heading = RuntimeUi.CreateText("Heading", screen, "ONLINE\nLOGIN", 86, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            RuntimeUi.Place(heading.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 260f), new Vector2(0f, 430f));

            Text hint = RuntimeUi.CreateText("Hint", screen, "Sign in to continue to Jumping Ninja.", 34, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.72f));
            RuntimeUi.Place(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 80f), new Vector2(0f, 255f));

            InputField username = RuntimeUi.CreateInputField("Username Input", screen, "Username");
            RuntimeUi.Place(username.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(820f, 130f), new Vector2(0f, 110f));

            InputField password = RuntimeUi.CreatePasswordField("Password Input", screen, "Password");
            RuntimeUi.Place(password.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(820f, 130f), new Vector2(0f, -50f));

            Text error = RuntimeUi.CreateText("Error", screen, string.Empty, 32, TextAnchor.MiddleCenter, new Color(1f, 0.55f, 0.45f, 1f), FontStyle.Bold);
            RuntimeUi.Place(error.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 80f), new Vector2(0f, -190f));
            error.text = initialError ?? string.Empty;

            Text status = RuntimeUi.CreateText("Status", screen, string.Empty, 30, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.72f), FontStyle.Bold);
            RuntimeUi.Place(status.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 70f), new Vector2(0f, -275f));

            Button login = null;
            Button register = null;
            login = RuntimeUi.CreateButton(
                "Login",
                screen,
                "LOG IN",
                () => BeginLogin(username, password, error, status, login, register),
                RuntimeUi.Red);
            RuntimeUi.Place(login.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(820f, 140f), new Vector2(0f, -390f));

            register = RuntimeUi.CreateButton(
                "Register",
                screen,
                "CREATE ACCOUNT",
                () => ShowRegister(),
                RuntimeUi.Muted);
            RuntimeUi.Place(register.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(820f, 120f), new Vector2(0f, -555f));
            RuntimeUi.Select(login);
        }

        private void ShowRegister()
        {
            Transform screen = BeginScreen("Online Register", RuntimeUi.Ink);
            Text heading = RuntimeUi.CreateText("Heading", screen, "CREATE AN\nACCOUNT", 78, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            RuntimeUi.Place(heading.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 260f), new Vector2(0f, 475f));

            Text hint = RuntimeUi.CreateText("Hint", screen, "3-16 letters, numbers, or underscores.", 32, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.72f));
            RuntimeUi.Place(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 80f), new Vector2(0f, 300f));

            InputField username = RuntimeUi.CreateInputField("Username Input", screen, "Username");
            RuntimeUi.Place(username.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(820f, 125f), new Vector2(0f, 175f));

            InputField password = RuntimeUi.CreatePasswordField("Password Input", screen, "Password");
            RuntimeUi.Place(password.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(820f, 125f), new Vector2(0f, 25f));

            InputField confirmPassword = RuntimeUi.CreatePasswordField("Confirm Password Input", screen, "Confirm password");
            RuntimeUi.Place(confirmPassword.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(820f, 125f), new Vector2(0f, -125f));

            Text error = RuntimeUi.CreateText("Error", screen, string.Empty, 30, TextAnchor.MiddleCenter, new Color(1f, 0.55f, 0.45f, 1f), FontStyle.Bold);
            RuntimeUi.Place(error.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 80f), new Vector2(0f, -265f));

            Text status = RuntimeUi.CreateText("Status", screen, string.Empty, 28, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.72f), FontStyle.Bold);
            RuntimeUi.Place(status.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 70f), new Vector2(0f, -345f));

            Button create = null;
            Button back = null;
            create = RuntimeUi.CreateButton(
                "Register",
                screen,
                "REGISTER",
                () => BeginRegistration(username, password, confirmPassword, error, status, create, back),
                RuntimeUi.Red);
            RuntimeUi.Place(create.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(820f, 130f), new Vector2(0f, -475f));

            back = RuntimeUi.CreateButton("Back", screen, "BACK TO LOGIN", () => ShowLogin(), RuntimeUi.Muted);
            RuntimeUi.Place(back.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(820f, 115f), new Vector2(0f, -635f));
            RuntimeUi.Select(create);
        }

        private void BeginLogin(
            InputField usernameInput,
            InputField passwordInput,
            Text error,
            Text status,
            Button login,
            Button register)
        {
            if (authRequestInFlight)
            {
                return;
            }

            string username = (usernameInput.text ?? string.Empty).Trim();
            string password = passwordInput.text ?? string.Empty;
            if (!ValidateClientCredentials(username, password, error))
            {
                return;
            }

            authRequestInFlight = true;
            error.text = string.Empty;
            status.text = "CONNECTING...";
            SetAuthControls(false, login, register);
            StartCoroutine(authApi.Login(
                username,
                password,
                OnAuthenticationSucceeded,
                apiError => OnAuthenticationFailed(apiError, error, status, login, register)));
        }

        private void BeginRegistration(
            InputField usernameInput,
            InputField passwordInput,
            InputField confirmPasswordInput,
            Text error,
            Text status,
            Button register,
            Button back)
        {
            if (authRequestInFlight)
            {
                return;
            }

            string username = (usernameInput.text ?? string.Empty).Trim();
            string password = passwordInput.text ?? string.Empty;
            string confirmPassword = confirmPasswordInput.text ?? string.Empty;
            if (!ValidateClientCredentials(username, password, error))
            {
                return;
            }

            if (password != confirmPassword)
            {
                error.text = "PASSWORDS DO NOT MATCH";
                return;
            }

            authRequestInFlight = true;
            error.text = string.Empty;
            status.text = "CREATING ACCOUNT...";
            SetAuthControls(false, register, back);
            StartCoroutine(authApi.Register(
                username,
                password,
                OnAuthenticationSucceeded,
                apiError => OnAuthenticationFailed(apiError, error, status, register, back)));
        }

        private void OnAuthenticationSucceeded(AuthResponsePayload response)
        {
            authSession.Apply(response);
            StartCoroutine(authApi.GetMe(
                authSession.AccessToken,
                OnSessionValidated,
                OnSessionValidationFailed));
        }

        private void OnSessionValidated(AuthUserPayload user)
        {
            StopOnlineWork();
            onlineSessionGeneration++;
            leaderboardCache = null;
            targetsCache = null;
            authSession.ApplyValidatedUser(user);
            ninjas.SetAccount(user.id);
            ContinueAfterAuthentication();
        }

        private void OnSessionValidationFailed(AuthApiError apiError)
        {
            StopOnlineWork();
            onlineSessionGeneration++;
            leaderboardCache = null;
            targetsCache = null;
            authSession.Clear();
            ninjas.ClearActiveAccount();
            authRequestInFlight = false;
            ShowLogin(apiError == null
                ? "SESSION VALIDATION FAILED"
                : apiError.Message);
        }

        private void OnAuthenticationFailed(
            AuthApiError apiError,
            Text error,
            Text status,
            Button primary,
            Button secondary)
        {
            authRequestInFlight = false;
            if (apiError != null && apiError.IsUnauthorized)
            {
                authSession.Clear();
            }

            error.text = apiError == null
                ? "AUTHENTICATION FAILED"
                : apiError.Message;
            status.text = string.Empty;
            SetAuthControls(true, primary, secondary);
            RuntimeUi.Select(primary);
        }

        private IEnumerator PrepareOnlineAccount()
        {
            if (onlineDataRequestInFlight)
            {
                yield break;
            }

            onlineDataRequestInFlight = true;
            int sessionGeneration = onlineSessionGeneration;
            string accountId = ninjas.AccountId;
            string accessToken = authSession.AccessToken;
            bool completed = false;
            NinjaListPayload response = null;
            AuthApiError failure = null;
            yield return authApi.GetNinjas(
                accessToken,
                payload =>
                {
                    response = payload;
                    completed = true;
                },
                error =>
                {
                    failure = error;
                    completed = true;
                });

            onlineDataRequestInFlight = false;
            if (!completed)
            {
                yield break;
            }

            if (sessionGeneration != onlineSessionGeneration ||
                !authSession.IsAuthenticated ||
                !string.Equals(accountId, ninjas.AccountId, System.StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            if (failure != null && failure.IsUnauthorized)
            {
                HandleUnauthorized();
                yield break;
            }

            if (response != null)
            {
                ninjas.ApplyServerSnapshot(response);
                yield return RetryPendingScores();

                if (sessionGeneration != onlineSessionGeneration ||
                    !authSession.IsAuthenticated ||
                    !string.Equals(accountId, ninjas.AccountId, System.StringComparison.OrdinalIgnoreCase))
                {
                    yield break;
                }
            }
            else if (!ninjas.HasCloudSnapshot && !ninjas.HasCachedNinjas)
            {
                ShowOnlineRetry(failure == null ? "ONLINE DATA UNAVAILABLE" : failure.Message);
                yield break;
            }

            if (!ninjas.MigrationReviewed && ninjas.GetUnclaimedLegacyProfiles().Count > 0)
            {
                ShowLegacyImport(false);
                yield break;
            }

            if (ninjas.Ninjas.Count == 0)
            {
                ShowCreateNinja(false);
                yield break;
            }

            ShowMainMenu();
            RefreshOnlineSummary();
        }

        private IEnumerator RetryPendingScores()
        {
            if (!authSession.IsAuthenticated || ninjas == null)
            {
                yield break;
            }

            int sessionGeneration = onlineSessionGeneration;
            string accountId = ninjas.AccountId;
            string accessToken = authSession.AccessToken;
            foreach (OnlineNinjaProfile ninja in ninjas.Ninjas)
            {
                if (sessionGeneration != onlineSessionGeneration ||
                    !authSession.IsAuthenticated ||
                    !string.Equals(accountId, ninjas.AccountId, System.StringComparison.OrdinalIgnoreCase))
                {
                    yield break;
                }

                int pendingScore = ninjas.GetPendingScore(ninja.id);
                if (pendingScore < 0)
                {
                    continue;
                }

                bool completed = false;
                ScoreSubmissionResponsePayload response = null;
                AuthApiError failure = null;
                yield return authApi.SubmitBestScore(
                    accessToken,
                    ninja.id,
                    pendingScore,
                    payload =>
                    {
                        response = payload;
                        completed = true;
                    },
                    error =>
                    {
                        failure = error;
                        completed = true;
                    });

                if (failure != null && failure.IsUnauthorized)
                {
                    HandleUnauthorized();
                    yield break;
                }

                if (completed && response != null)
                {
                    ninjas.ApplyScoreResponse(response);
                }
            }
        }

        private IEnumerator SubmitScoreAfterRun(
            string ninjaId,
            int score,
            GameController game)
        {
            if (!authSession.IsAuthenticated)
            {
                game?.SetScoreSyncState("SAVED LOCALLY — WILL RETRY");
                yield break;
            }

            int sessionGeneration = onlineSessionGeneration;
            string accountId = ninjas.AccountId;
            string accessToken = authSession.AccessToken;
            game?.SetScoreSyncState("SYNCING...");
            bool completed = false;
            ScoreSubmissionResponsePayload response = null;
            AuthApiError failure = null;
            yield return authApi.SubmitBestScore(
                accessToken,
                ninjaId,
                score,
                payload =>
                {
                    response = payload;
                    completed = true;
                },
                error =>
                {
                    failure = error;
                    completed = true;
                });

            scoreSyncRoutine = null;
            if (sessionGeneration != onlineSessionGeneration ||
                !string.Equals(accountId, ninjas.AccountId, System.StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }
            if (failure != null && failure.IsUnauthorized)
            {
                HandleUnauthorized();
                yield break;
            }

            if (completed && response != null)
            {
                ninjas.ApplyScoreResponse(response);
                game?.SetScoreSyncState(response.accountImproved
                    ? $"ONLINE ACCOUNT BEST  •  RANK {response.accountRank}"
                    : "ONLINE SCORE SAVED");
                RefreshOnlineSummary();
            }
            else
            {
                game?.SetScoreSyncState(
                    failure == null ? "SAVED LOCALLY — WILL RETRY" : "SAVED LOCALLY — WILL RETRY");
            }
        }

        private void RefreshOnlineSummary()
        {
            if (!authSession.IsAuthenticated || onlineRefreshRoutine != null)
            {
                return;
            }

            onlineRefreshRoutine = StartCoroutine(RefreshOnlineSummaryRoutine());
        }

        internal void RequestOnlineTargets(int currentScore)
        {
            if (!authSession.IsAuthenticated ||
                onlineTargetsRefreshRoutine != null)
            {
                return;
            }

            int fromScore = currentScore == int.MaxValue ? int.MaxValue : currentScore + 1;
            onlineTargetsRefreshRoutine = StartCoroutine(
                RefreshOnlineTargetsRoutine(fromScore));
        }

        private IEnumerator RefreshOnlineTargetsRoutine(int fromScore)
        {
            int sessionGeneration = onlineSessionGeneration;
            string accountId = ninjas.AccountId;
            string accessToken = authSession.AccessToken;
            bool completed = false;
            LeaderboardTargetsPayload targets = null;
            yield return authApi.GetTargets(
                accessToken,
                fromScore,
                20,
                response =>
                {
                    targets = response;
                    completed = true;
                },
                _ => completed = true);

            onlineTargetsRefreshRoutine = null;
            if (sessionGeneration != onlineSessionGeneration ||
                !authSession.IsAuthenticated ||
                !string.Equals(accountId, ninjas.AccountId, System.StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            if (completed && targets != null)
            {
                targetsCache = targets;
                currentGame?.MergeOnlineTargets(targets);
            }
        }

        private IEnumerator RefreshOnlineSummaryRoutine()
        {
            int sessionGeneration = onlineSessionGeneration;
            string accountId = ninjas.AccountId;
            string accessToken = authSession.AccessToken;
            bool leaderboardCompleted = false;
            LeaderboardPayload leaderboard = null;
            AuthApiError leaderboardFailure = null;
            yield return authApi.GetLeaderboard(
                accessToken,
                100,
                response =>
                {
                    leaderboard = response;
                    leaderboardCompleted = true;
                },
                error =>
                {
                    leaderboardFailure = error;
                    leaderboardCompleted = true;
                });

            if (sessionGeneration != onlineSessionGeneration ||
                !authSession.IsAuthenticated ||
                !string.Equals(accountId, ninjas.AccountId, System.StringComparison.OrdinalIgnoreCase))
            {
                onlineRefreshRoutine = null;
                yield break;
            }

            if (leaderboardFailure != null && leaderboardFailure.IsUnauthorized)
            {
                onlineRefreshRoutine = null;
                HandleUnauthorized();
                yield break;
            }

            if (leaderboardCompleted && leaderboard != null)
            {
                leaderboardCache = leaderboard;
            }

            bool targetsCompleted = false;
            LeaderboardTargetsPayload targets = null;
            AuthApiError targetsFailure = null;
            int nextTargetFromScore = ninjas.AccountBestScore == int.MaxValue
                ? int.MaxValue
                : ninjas.AccountBestScore + 1;
            yield return authApi.GetTargets(
                accessToken,
                // The HUD only needs a milestone that can still be passed;
                // requesting strictly above the current account best avoids
                // showing an already-tied score as the next target.
                nextTargetFromScore,
                20,
                response =>
                {
                    targets = response;
                    targetsCompleted = true;
                },
                error =>
                {
                    targetsFailure = error;
                    targetsCompleted = true;
                });

            onlineRefreshRoutine = null;
            if (sessionGeneration != onlineSessionGeneration ||
                !authSession.IsAuthenticated ||
                !string.Equals(accountId, ninjas.AccountId, System.StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }
            if (targetsFailure != null && targetsFailure.IsUnauthorized)
            {
                HandleUnauthorized();
                yield break;
            }

            if (targetsCompleted && targets != null)
            {
                targetsCache = targets;
                currentGame?.MergeOnlineTargets(targets);
            }
        }

        private void HandleUnauthorized()
        {
            Time.timeScale = 1f;
            StopOnlineWork();
            onlineSessionGeneration++;
            leaderboardCache = null;
            targetsCache = null;
            authSession.Clear();
            ninjas.ClearActiveAccount();
            DestroyCurrentGame();
            ShowLogin("SESSION EXPIRED — PLEASE LOG IN AGAIN");
        }

        private void ShowOnlineRetry(string message)
        {
            Transform screen = BeginScreen("Online Data Retry", RuntimeUi.Ink);
            Text heading = RuntimeUi.CreateText("Heading", screen, "ONLINE DATA\nUNAVAILABLE", 72, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            RuntimeUi.Place(heading.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 240f), new Vector2(0f, 310f));
            Text status = RuntimeUi.CreateText("Status", screen, message ?? "TRY AGAIN", 32, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.75f), FontStyle.Bold);
            RuntimeUi.Place(status.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 120f), new Vector2(0f, 80f));

            Button retry = RuntimeUi.CreateButton("Retry", screen, "RETRY", () =>
            {
                if (!onlineDataRequestInFlight)
                {
                    StartCoroutine(PrepareOnlineAccount());
                }
            }, RuntimeUi.Red);
            RuntimeUi.Place(retry.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(760f, 140f), new Vector2(0f, -130f));

            Button logout = RuntimeUi.CreateButton("Log Out", screen, "BACK TO LOGIN", Logout, RuntimeUi.Muted);
            RuntimeUi.Place(logout.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(760f, 120f), new Vector2(0f, -300f));
            RuntimeUi.Select(retry);
        }

        private static bool ValidateClientCredentials(string username, string password, Text error)
        {
            if (string.IsNullOrEmpty(username))
            {
                error.text = "USERNAME REQUIRED";
                return false;
            }

            if (string.IsNullOrEmpty(password))
            {
                error.text = "PASSWORD REQUIRED";
                return false;
            }

            return true;
        }

        private static void SetAuthControls(bool interactable, Button primary, Button secondary)
        {
            if (primary != null)
            {
                primary.interactable = interactable;
            }

            if (secondary != null)
            {
                secondary.interactable = interactable;
            }
        }

        private void ShowMainMenu()
        {
            Transform screen = BeginScreen("Main Menu", RuntimeUi.Paper);
            AddLogo(screen, new Vector2(380f, 380f), new Vector2(0f, 520f));

            Text title = RuntimeUi.CreateText("Title", screen, "JUMPING\nNINJA", 92, TextAnchor.MiddleCenter, RuntimeUi.Ink, FontStyle.Bold);
            RuntimeUi.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 260f), new Vector2(0f, 155f));

            OnlineNinjaProfile active = ninjas.ActiveNinja;
            string userLine = active == null
                ? "NO NINJA"
                : $"{active.name.ToUpperInvariant()}  •  BEST {active.bestScore}" +
                  (ninjas.HasCloudSnapshot ? string.Empty : "  •  CACHED");
            Text activeUser = RuntimeUi.CreateText("Active User", screen, userLine, 38, TextAnchor.MiddleCenter, RuntimeUi.Muted, FontStyle.Bold);
            RuntimeUi.Place(activeUser.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 80f), new Vector2(0f, -30f));

            string accountName = authSession.IsAuthenticated && authSession.CurrentUser != null
                ? authSession.CurrentUser.username.ToUpperInvariant()
                : "NOT SIGNED IN";
            Text onlineAccount = RuntimeUi.CreateText("Online Account", screen, $"ONLINE ACCOUNT  •  {accountName}", 30, TextAnchor.MiddleCenter, RuntimeUi.Muted, FontStyle.Bold);
            RuntimeUi.Place(onlineAccount.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 70f), new Vector2(0f, -105f));

            Button start = RuntimeUi.CreateButton("Start", screen, "START RUN", StartRun, RuntimeUi.Red);
            RuntimeUi.Place(start.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(760f, 145f), new Vector2(0f, -225f));

            Button leaderboard = RuntimeUi.CreateButton("Leaderboard", screen, "LEADERBOARD", ShowLeaderboard, RuntimeUi.Ink);
            RuntimeUi.Place(leaderboard.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(760f, 125f), new Vector2(0f, -395f));

            Button switchUser = RuntimeUi.CreateButton("Switch User", screen, "SWITCH NINJA", ShowSwitchUser, RuntimeUi.Ink);
            RuntimeUi.Place(switchUser.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(760f, 125f), new Vector2(0f, -545f));

            Button logout = RuntimeUi.CreateButton("Log Out", screen, "LOG OUT", Logout, RuntimeUi.Muted);
            RuntimeUi.Place(logout.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(760f, 115f), new Vector2(0f, -700f));

            Text footer = RuntimeUi.CreateText("Version", screen, "POTATOED MICE  •  V1", 28, TextAnchor.MiddleCenter, RuntimeUi.Muted, FontStyle.Bold);
            RuntimeUi.Place(footer.rectTransform, new Vector2(0.5f, 0f), new Vector2(900f, 80f), new Vector2(0f, 70f));
            RuntimeUi.Select(start);
            RefreshOnlineSummary();
        }

        private void Logout()
        {
            if (authRequestInFlight)
            {
                return;
            }

            StopOnlineWork();
            onlineSessionGeneration++;
            leaderboardCache = null;
            targetsCache = null;
            authSession.Clear();
            ninjas.ClearActiveAccount();
            DestroyCurrentGame();
            ShowLogin();
        }

        private void StopOnlineWork()
        {
            if (onlineRefreshRoutine != null)
            {
                StopCoroutine(onlineRefreshRoutine);
                onlineRefreshRoutine = null;
            }

            if (onlineTargetsRefreshRoutine != null)
            {
                StopCoroutine(onlineTargetsRefreshRoutine);
                onlineTargetsRefreshRoutine = null;
            }

            if (scoreSyncRoutine != null)
            {
                StopCoroutine(scoreSyncRoutine);
                scoreSyncRoutine = null;
            }

            onlineDataRequestInFlight = false;
        }

        private void ShowCreateNinja(bool canCancel)
        {
            Transform screen = BeginScreen("Create Ninja", RuntimeUi.Ink);
            Text heading = RuntimeUi.CreateText("Heading", screen, "CREATE YOUR\nNINJA", 86, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            RuntimeUi.Place(heading.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 260f), new Vector2(0f, 430f));

            Text hint = RuntimeUi.CreateText("Hint", screen, "Your Ninja and score are saved online.", 34, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f));
            RuntimeUi.Place(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 80f), new Vector2(0f, 230f));

            InputField input = RuntimeUi.CreateInputField("Name Input", screen, "Ninja name");
            RuntimeUi.Place(input.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(820f, 130f), new Vector2(0f, 40f));

            Text error = RuntimeUi.CreateText("Error", screen, string.Empty, 32, TextAnchor.MiddleCenter, new Color(1f, 0.55f, 0.45f, 1f), FontStyle.Bold);
            RuntimeUi.Place(error.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 80f), new Vector2(0f, -70f));
            Text status = RuntimeUi.CreateText("Status", screen, string.Empty, 28, TextAnchor.MiddleCenter, RuntimeUi.Muted, FontStyle.Bold);
            RuntimeUi.Place(status.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900f, 70f), new Vector2(0f, -135f));

            Button create = null;
            Button back = null;
            void CreateNinja()
            {
                if (onlineDataRequestInFlight)
                {
                    return;
                }

                string name = (input.text ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(name))
                {
                    error.text = "NINJA NAME REQUIRED";
                    return;
                }

                onlineDataRequestInFlight = true;
                int sessionGeneration = onlineSessionGeneration;
                string accountId = ninjas.AccountId;
                string accessToken = authSession.AccessToken;
                error.text = string.Empty;
                status.text = "CREATING NINJA...";
                SetAuthControls(false, create, back);
                StartCoroutine(authApi.CreateNinja(
                    accessToken,
                    name,
                    response =>
                    {
                        if (sessionGeneration != onlineSessionGeneration ||
                            !authSession.IsAuthenticated ||
                            !string.Equals(accountId, ninjas.AccountId, System.StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }

                        onlineDataRequestInFlight = false;
                        ninjas.ApplyCreatedNinja(response);
                        ninjas.MarkMigrationReviewed();
                        ShowMainMenu();
                    },
                    apiError =>
                    {
                        if (sessionGeneration != onlineSessionGeneration ||
                            !authSession.IsAuthenticated ||
                            !string.Equals(accountId, ninjas.AccountId, System.StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }

                        onlineDataRequestInFlight = false;
                        if (apiError != null && apiError.IsUnauthorized)
                        {
                            HandleUnauthorized();
                            return;
                        }

                        error.text = apiError == null ? "NINJA CREATION FAILED" : apiError.Message;
                        status.text = string.Empty;
                        SetAuthControls(true, create, back);
                        RuntimeUi.Select(create);
                    }));
            }

            create = RuntimeUi.CreateButton("Create", screen, "CREATE", CreateNinja, RuntimeUi.Red);
            RuntimeUi.Place(create.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(820f, 140f), new Vector2(0f, -220f));
            if (canCancel)
            {
                back = RuntimeUi.CreateButton("Back", screen, "BACK", ShowSwitchUser, RuntimeUi.Muted);
                RuntimeUi.Place(back.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(820f, 120f), new Vector2(0f, -390f));
            }
            RuntimeUi.Select(create);
        }

        private void ShowLegacyImport(
            bool canCancel,
            IEnumerable<string> preselectedIds = null,
            string feedbackMessage = null)
        {
            Transform screen = BeginScreen("Import Ninjas", RuntimeUi.Ink);
            Text heading = RuntimeUi.CreateText("Heading", screen, "IMPORT OLD\nNINJAS", 76, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            RuntimeUi.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(960f, 180f), new Vector2(0f, -145f));
            Text hint = RuntimeUi.CreateText("Hint", screen, "Choose the v1 Ninjas for this online account.", 30, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.72f));
            RuntimeUi.Place(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(900f, 70f), new Vector2(0f, -270f));

            Text status = RuntimeUi.CreateText("Status", screen, $"0 SELECTED  •  {ninjas.MaxNinjas - ninjas.Ninjas.Count} SLOTS LEFT", 28, TextAnchor.MiddleCenter, RuntimeUi.Muted, FontStyle.Bold);
            RuntimeUi.Place(status.rectTransform, new Vector2(0.5f, 1f), new Vector2(900f, 70f), new Vector2(0f, -335f));
            Text feedback = RuntimeUi.CreateText(
                "Feedback",
                screen,
                string.IsNullOrWhiteSpace(feedbackMessage) ? string.Empty : "IMPORT FAILED: " + feedbackMessage,
                26,
                TextAnchor.MiddleCenter,
                RuntimeUi.Red,
                FontStyle.Bold);
            RuntimeUi.Place(feedback.rectTransform, new Vector2(0.5f, 1f), new Vector2(900f, 60f), new Vector2(0f, -395f));

            RuntimeUi.CreateScrollView("Legacy List", screen, new Vector2(840f, 760f), new Vector2(0f, -70f), out Transform rows);
            List<UserProfile> candidates = ninjas.GetUnclaimedLegacyProfiles();
            HashSet<string> selected = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            if (preselectedIds != null)
            {
                foreach (string preselectedId in preselectedIds)
                {
                    selected.Add(LegacyProfileIdRules.Normalize(preselectedId));
                }
            }

            HashSet<string> candidateIds = new HashSet<string>(
                candidates.Select(profile => LegacyProfileIdRules.Normalize(profile.id)),
                System.StringComparer.OrdinalIgnoreCase);
            selected.RemoveWhere(profileId => !candidateIds.Contains(profileId));
            Button import = null;

            void RefreshSelection()
            {
                int slots = Mathf.Max(0, ninjas.MaxNinjas - ninjas.Ninjas.Count);
                status.text = $"{selected.Count} SELECTED  •  {slots} SLOTS LEFT";
                if (import != null)
                {
                    import.interactable = !onlineDataRequestInFlight && selected.Count > 0 && selected.Count <= slots;
                }
            }

            for (int index = 0; index < candidates.Count; index++)
            {
                UserProfile profile = candidates[index];
                string normalizedProfileId = LegacyProfileIdRules.Normalize(profile.id);
                Button item = null;
                Text itemLabel = null;
                Image itemImage = null;
                item = RuntimeUi.CreateButton(
                    "Legacy Ninja " + index,
                    rows,
                    string.Empty,
                    () =>
                    {
                        if (selected.Contains(normalizedProfileId))
                        {
                            selected.Remove(normalizedProfileId);
                        }
                        else
                        {
                            int slots = Mathf.Max(0, ninjas.MaxNinjas - ninjas.Ninjas.Count);
                            if (selected.Count >= slots)
                            {
                                return;
                            }

                            selected.Add(normalizedProfileId);
                        }

                        bool isSelected = selected.Contains(normalizedProfileId);
                        itemLabel.text = isSelected
                            ? "✓  " + profile.name + "   •   BEST " + profile.bestScore
                            : profile.name + "   •   BEST " + profile.bestScore;
                        itemLabel.color = isSelected ? Color.white : RuntimeUi.Ink;
                        itemImage.color = isSelected ? RuntimeUi.Red : RuntimeUi.Paper;
                        RefreshSelection();
                    },
                    RuntimeUi.Paper);
                itemLabel = item.GetComponentInChildren<Text>();
                itemImage = item.GetComponent<Image>();
                bool initiallySelected = selected.Contains(normalizedProfileId);
                itemLabel.text = initiallySelected
                    ? "✓  " + profile.name + "   •   BEST " + profile.bestScore
                    : profile.name + "   •   BEST " + profile.bestScore;
                itemLabel.color = initiallySelected ? Color.white : RuntimeUi.Ink;
                itemImage.color = initiallySelected ? RuntimeUi.Red : RuntimeUi.Paper;
                RuntimeUi.AddLayoutHeight(item.gameObject, 105f);
            }

            if (candidates.Count == 0)
            {
                Text empty = RuntimeUi.CreateText("Empty", rows, "NO OLD NINJAS FOUND", 34, TextAnchor.MiddleCenter, RuntimeUi.Muted, FontStyle.Bold);
                RuntimeUi.AddLayoutHeight(empty.gameObject, 100f);
            }

            void Skip()
            {
                if (onlineDataRequestInFlight)
                {
                    return;
                }

                ninjas.MarkMigrationReviewed();
                if (ninjas.Ninjas.Count == 0)
                {
                    ShowCreateNinja(false);
                }
                else
                {
                    ShowMainMenu();
                }
            }

            import = RuntimeUi.CreateButton("Import Selected", screen, "IMPORT SELECTED", () =>
            {
                if (onlineDataRequestInFlight)
                {
                    return;
                }

                List<UserProfile> selectedProfiles = candidates
                    .Where(profile => selected.Contains(LegacyProfileIdRules.Normalize(profile.id)))
                    .ToList();
                StartCoroutine(ImportSelectedLegacy(
                    selectedProfiles,
                    status,
                    import,
                    onlineSessionGeneration));
            }, RuntimeUi.Red);
            RuntimeUi.Place(import.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(820f, 120f), new Vector2(0f, 300f));

            Button skip = RuntimeUi.CreateButton("Skip", screen, "SKIP FOR NOW", Skip, RuntimeUi.Muted);
            RuntimeUi.Place(skip.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(820f, 115f), new Vector2(0f, 160f));
            if (canCancel)
            {
                Button back = RuntimeUi.CreateButton("Back", screen, "BACK", ShowSwitchUser, RuntimeUi.Muted);
                RuntimeUi.Place(back.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(820f, 105f), new Vector2(0f, 35f));
            }

            RefreshSelection();
            RuntimeUi.Select(import);
        }

        private IEnumerator ImportSelectedLegacy(
            List<UserProfile> profiles,
            Text status,
            Button import,
            int sessionGeneration)
        {
            onlineDataRequestInFlight = true;
            import.interactable = false;
            bool hadFailure = false;
            List<string> failedIds = new List<string>();
            string firstFailure = null;
            string accessToken = authSession.AccessToken;
            for (int index = 0; index < profiles.Count; index++)
            {
                UserProfile profile = profiles[index];
                if (sessionGeneration != onlineSessionGeneration || !authSession.IsAuthenticated)
                {
                    onlineDataRequestInFlight = false;
                    yield break;
                }

                status.text = $"IMPORTING {index + 1} / {profiles.Count}...";
                if (!LegacyProfileIdRules.TryNormalize(profile.id, out string normalizedLegacyProfileId))
                {
                    hadFailure = true;
                    failedIds.Add(profile.id);
                    firstFailure ??= "One or more old Ninja IDs are invalid.";
                    continue;
                }

                bool completed = false;
                NinjaImportResponsePayload response = null;
                AuthApiError failure = null;
                yield return authApi.ImportNinja(
                    accessToken,
                    normalizedLegacyProfileId,
                    profile.name,
                    Mathf.Max(0, profile.bestScore),
                    payload =>
                    {
                        response = payload;
                        completed = true;
                    },
                    error =>
                    {
                        failure = error;
                        completed = true;
                    });

                if (failure != null && failure.IsUnauthorized)
                {
                    onlineDataRequestInFlight = false;
                    HandleUnauthorized();
                    yield break;
                }

                if (completed && response != null)
                {
                    ninjas.ApplyImportedNinja(normalizedLegacyProfileId, response);
                }
                else
                {
                    hadFailure = true;
                    failedIds.Add(profile.id);
                    firstFailure ??= failure?.Message ?? "The server did not complete the import.";
                }
            }

            onlineDataRequestInFlight = false;
            if (hadFailure)
            {
                // Keep the migration flag unset so failed items remain
                // discoverable and can be retried from the import screen.
                ShowLegacyImport(true, failedIds, firstFailure);
                yield break;
            }

            ninjas.MarkMigrationReviewed();
            if (ninjas.Ninjas.Count == 0)
            {
                ShowCreateNinja(false);
            }
            else
            {
                ShowMainMenu();
            }
        }

        private void ShowLeaderboard()
        {
            Transform screen = BeginScreen("Leaderboard", RuntimeUi.Paper);
            Text heading = RuntimeUi.CreateText("Heading", screen, "ONLINE\nLEADERBOARD", 70, TextAnchor.MiddleCenter, RuntimeUi.Ink, FontStyle.Bold);
            RuntimeUi.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(960f, 180f), new Vector2(0f, -145f));
            Text status = RuntimeUi.CreateText("Status", screen, "LOADING ONLINE BOARD...", 28, TextAnchor.MiddleCenter, RuntimeUi.Muted, FontStyle.Bold);
            RuntimeUi.Place(status.rectTransform, new Vector2(0.5f, 1f), new Vector2(900f, 70f), new Vector2(0f, -260f));
            RuntimeUi.CreateScrollView("Leaderboard List", screen, new Vector2(860f, 950f), new Vector2(0f, -55f), out Transform rows);

            Button refresh = null;
            refresh = RuntimeUi.CreateButton("Refresh", screen, "REFRESH", () =>
            {
                if (currentScreen == screen.gameObject)
                {
                    StartCoroutine(LoadLeaderboardScreen(screen, status, rows, refresh));
                }
            }, RuntimeUi.Ink);
            RuntimeUi.Place(refresh.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(360f, 105f), new Vector2(-205f, 130f));

            Button back = RuntimeUi.CreateButton("Back", screen, "BACK", ShowMainMenu, RuntimeUi.Red);
            RuntimeUi.Place(back.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(360f, 105f), new Vector2(205f, 130f));
            RuntimeUi.Select(back);
            StartCoroutine(LoadLeaderboardScreen(screen, status, rows, refresh));
        }

        private IEnumerator LoadLeaderboardScreen(
            Transform screen,
            Text status,
            Transform rows,
            Button refresh)
        {
            int sessionGeneration = onlineSessionGeneration;
            string accessToken = authSession.AccessToken;
            refresh.interactable = false;
            bool completed = false;
            LeaderboardPayload response = null;
            AuthApiError failure = null;
            yield return authApi.GetLeaderboard(
                accessToken,
                100,
                payload =>
                {
                    response = payload;
                    completed = true;
                },
                error =>
                {
                    failure = error;
                    completed = true;
                });

            if (sessionGeneration != onlineSessionGeneration || !authSession.IsAuthenticated)
            {
                yield break;
            }

            if (failure != null && failure.IsUnauthorized)
            {
                HandleUnauthorized();
                yield break;
            }

            if (completed && response != null)
            {
                leaderboardCache = response;
                RenderLeaderboardRows(rows, response);
                status.text = "UPDATED ONLINE  •  " + FormatSnapshotTime(response.generatedAt);
            }
            else if (leaderboardCache != null)
            {
                RenderLeaderboardRows(rows, leaderboardCache);
                status.text = "OFFLINE — CACHED " + FormatSnapshotTime(leaderboardCache.generatedAt);
            }
            else
            {
                status.text = failure == null ? "ONLINE BOARD UNAVAILABLE" : failure.Message;
            }

            refresh.interactable = true;
        }

        private void RenderLeaderboardRows(Transform rows, LeaderboardPayload response)
        {
            foreach (Transform child in rows)
            {
                Destroy(child.gameObject);
            }

            LeaderboardEntryPayload[] entries = response?.entries ?? System.Array.Empty<LeaderboardEntryPayload>();
            if (entries.Length == 0)
            {
                Text empty = RuntimeUi.CreateText("Empty", rows, "NO ONLINE SCORES YET", 34, TextAnchor.MiddleCenter, RuntimeUi.Muted, FontStyle.Bold);
                RuntimeUi.AddLayoutHeight(empty.gameObject, 100f);
            }

            bool currentShown = false;
            foreach (LeaderboardEntryPayload entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                currentShown |= entry.isCurrentUser;
                string username = entry.username ?? string.Empty;
                string line = $"{entry.rank}.  {username.ToUpperInvariant()}  •  {entry.ninjaName}  •  {entry.bestScore}";
                Text row = RuntimeUi.CreateText(
                    "Leaderboard Row",
                    rows,
                    entry.isCurrentUser ? "◀  " + line : line,
                    34,
                    TextAnchor.MiddleLeft,
                    entry.isCurrentUser ? RuntimeUi.Red : RuntimeUi.Ink,
                    FontStyle.Bold);
                RuntimeUi.AddLayoutHeight(row.gameObject, 82f);
            }

            if (response?.currentUser != null && !currentShown)
            {
                LeaderboardEntryPayload current = response.currentUser;
                string username = current.username ?? string.Empty;
                Text row = RuntimeUi.CreateText(
                    "Current Rank",
                    rows,
                    $"YOUR RANK  {current.rank}  •  {username.ToUpperInvariant()}  •  {current.bestScore}",
                    34,
                    TextAnchor.MiddleLeft,
                    RuntimeUi.Red,
                    FontStyle.Bold);
                RuntimeUi.AddLayoutHeight(row.gameObject, 100f);
            }
        }

        private static string FormatSnapshotTime(string generatedAt)
        {
            return System.DateTimeOffset.TryParse(generatedAt, out System.DateTimeOffset timestamp)
                ? timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : "TIME UNKNOWN";
        }

        private void ShowSwitchUser()
        {
            Transform screen = BeginScreen("Switch Ninja", RuntimeUi.Paper);
            Text heading = RuntimeUi.CreateText("Heading", screen, "CHOOSE NINJA", 76, TextAnchor.MiddleCenter, RuntimeUi.Ink, FontStyle.Bold);
            RuntimeUi.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(960f, 160f), new Vector2(0f, -150f));

            IReadOnlyList<OnlineNinjaProfile> profiles = ninjas.Ninjas;
            Text capacity = RuntimeUi.CreateText(
                "Capacity",
                screen,
                $"{profiles.Count} / {ninjas.MaxNinjas} NINJAS",
                30,
                TextAnchor.MiddleCenter,
                RuntimeUi.Muted,
                FontStyle.Bold);
            RuntimeUi.Place(capacity.rectTransform, new Vector2(0.5f, 1f), new Vector2(900f, 70f), new Vector2(0f, -255f));

            RuntimeUi.CreateScrollView("Ninja List", screen, new Vector2(840f, 780f), new Vector2(0f, -40f), out Transform rows);
            Button initialSelection = null;
            for (int index = 0; index < profiles.Count; index++)
            {
                OnlineNinjaProfile profile = profiles[index];
                bool isActive = profile.id == ninjas.ActiveNinja?.id;
                bool hasPendingScore = ninjas.GetPendingScore(profile.id) >= 0;
                string pendingMarker = hasPendingScore ? "  •  PENDING" : string.Empty;
                Button ninjaButton = RuntimeUi.CreateButton(
                    "Ninja " + index,
                    rows,
                    (isActive ? "◀  " : string.Empty) + $"{profile.name}   •   BEST {profile.bestScore}{pendingMarker}",
                    () =>
                    {
                        ninjas.SetActiveNinja(profile.id);
                        ShowMainMenu();
                    },
                    isActive ? RuntimeUi.Red : RuntimeUi.Paper);
                RuntimeUi.AddLayoutHeight(ninjaButton.gameObject, 105f);
                if (initialSelection == null || isActive)
                {
                    initialSelection = ninjaButton;
                }
            }

            int slots = Mathf.Max(0, ninjas.MaxNinjas - profiles.Count);
            Button create = RuntimeUi.CreateButton(
                "New Ninja",
                screen,
                slots == 0 ? "NINJA LIMIT REACHED" : "NEW NINJA",
                () => ShowCreateNinja(true),
                slots == 0 ? RuntimeUi.Muted : RuntimeUi.Red);
            create.interactable = slots > 0;
            RuntimeUi.Place(create.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(820f, 115f), new Vector2(0f, 300f));

            List<UserProfile> legacy = ninjas.GetUnclaimedLegacyProfiles();
            Button import = RuntimeUi.CreateButton(
                "Import Ninjas",
                screen,
                "IMPORT OLD NINJAS",
                () => ShowLegacyImport(true),
                RuntimeUi.Ink);
            import.interactable = legacy.Count > 0 && slots > 0;
            RuntimeUi.Place(import.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(820f, 105f), new Vector2(0f, 165f));

            Button back = RuntimeUi.CreateButton("Back", screen, "BACK", ShowMainMenu, RuntimeUi.Muted);
            RuntimeUi.Place(back.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(820f, 100f), new Vector2(0f, 40f));
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
