using System;
using System.Collections;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace JumpingNinja
{
    public sealed class AuthApiClient
    {
        private readonly string baseUrl;
        private readonly int timeoutSeconds;

        public AuthApiClient(string apiBaseUrl, int requestTimeoutSeconds)
        {
            baseUrl = (apiBaseUrl ?? string.Empty).Trim().TrimEnd('/');
            timeoutSeconds = Mathf.Max(1, requestTimeoutSeconds);
        }

        public IEnumerator Register(
            string username,
            string password,
            Action<AuthResponsePayload> onSuccess,
            Action<AuthApiError> onFailure)
        {
            AuthCredentialsPayload payload = new AuthCredentialsPayload
            {
                username = username,
                password = password
            };
            yield return SendAuthRequest(
                UnityWebRequest.Post(
                    BuildUrl("/api/v1/auth/register"),
                    JsonUtility.ToJson(payload),
                    "application/json"),
                onSuccess,
                onFailure);
        }

        public IEnumerator Login(
            string username,
            string password,
            Action<AuthResponsePayload> onSuccess,
            Action<AuthApiError> onFailure)
        {
            AuthCredentialsPayload payload = new AuthCredentialsPayload
            {
                username = username,
                password = password
            };
            yield return SendAuthRequest(
                UnityWebRequest.Post(
                    BuildUrl("/api/v1/auth/login"),
                    JsonUtility.ToJson(payload),
                    "application/json"),
                onSuccess,
                onFailure);
        }

        public IEnumerator GetMe(
            string accessToken,
            Action<AuthUserPayload> onSuccess,
            Action<AuthApiError> onFailure)
        {
            UnityWebRequest request = UnityWebRequest.Get(BuildUrl("/api/v1/auth/me"));
            request.SetRequestHeader("Authorization", "Bearer " + (accessToken ?? string.Empty));
            yield return SendUserRequest(request, onSuccess, onFailure);
        }

        public IEnumerator GetNinjas(
            string accessToken,
            Action<NinjaListPayload> onSuccess,
            Action<AuthApiError> onFailure)
        {
            UnityWebRequest request = UnityWebRequest.Get(BuildUrl("/api/v1/ninjas"));
            AddAuthorization(request, accessToken);
            yield return SendModelRequest(request, onSuccess, onFailure);
        }

        public IEnumerator CreateNinja(
            string accessToken,
            string name,
            Action<OnlineNinjaPayload> onSuccess,
            Action<AuthApiError> onFailure)
        {
            UnityWebRequest request = CreateJsonRequest(
                UnityWebRequest.kHttpVerbPOST,
                "/api/v1/ninjas",
                JsonUtility.ToJson(new NinjaCreatePayload { name = name }));
            AddAuthorization(request, accessToken);
            yield return SendModelRequest(request, onSuccess, onFailure);
        }

        public IEnumerator ImportNinja(
            string accessToken,
            string legacyProfileId,
            string name,
            int bestScore,
            Action<NinjaImportResponsePayload> onSuccess,
            Action<AuthApiError> onFailure)
        {
            string normalizedLegacyProfileId = LegacyProfileIdRules.Normalize(legacyProfileId);
            UnityWebRequest request = CreateJsonRequest(
                UnityWebRequest.kHttpVerbPOST,
                "/api/v1/ninjas/import",
                JsonUtility.ToJson(new NinjaImportPayload
                {
                    legacyProfileId = normalizedLegacyProfileId,
                    name = name,
                    bestScore = bestScore
                }));
            AddAuthorization(request, accessToken);
            yield return SendModelRequest(request, onSuccess, onFailure);
        }

        public IEnumerator SubmitBestScore(
            string accessToken,
            string ninjaId,
            int bestScore,
            Action<ScoreSubmissionResponsePayload> onSuccess,
            Action<AuthApiError> onFailure)
        {
            UnityWebRequest request = CreateJsonRequest(
                UnityWebRequest.kHttpVerbPUT,
                "/api/v1/ninjas/" + ninjaId + "/best-score",
                JsonUtility.ToJson(new BestScorePayload { bestScore = bestScore }));
            AddAuthorization(request, accessToken);
            yield return SendModelRequest(request, onSuccess, onFailure);
        }

        public IEnumerator GetLeaderboard(
            string accessToken,
            int limit,
            Action<LeaderboardPayload> onSuccess,
            Action<AuthApiError> onFailure)
        {
            UnityWebRequest request = UnityWebRequest.Get(
                BuildUrl("/api/v1/leaderboard?limit=" + Mathf.Clamp(limit, 1, 100)));
            AddAuthorization(request, accessToken);
            yield return SendModelRequest(request, onSuccess, onFailure);
        }

        public IEnumerator GetTargets(
            string accessToken,
            int fromScore,
            int limit,
            Action<LeaderboardTargetsPayload> onSuccess,
            Action<AuthApiError> onFailure)
        {
            UnityWebRequest request = UnityWebRequest.Get(
                BuildUrl(
                    "/api/v1/leaderboard/targets?fromScore=" + Mathf.Max(0, fromScore) +
                    "&limit=" + Mathf.Clamp(limit, 1, 20)));
            AddAuthorization(request, accessToken);
            yield return SendModelRequest(request, onSuccess, onFailure);
        }

        private IEnumerator SendAuthRequest(
            UnityWebRequest request,
            Action<AuthResponsePayload> onSuccess,
            Action<AuthApiError> onFailure)
        {
            yield return SendRequest(request);
            if (request.result == UnityWebRequest.Result.Success &&
                request.responseCode >= 200 &&
                request.responseCode < 300)
            {
                AuthResponsePayload response = TryParse<AuthResponsePayload>(request.downloadHandler.text);
                if (response != null &&
                    response.user != null &&
                    !string.IsNullOrEmpty(response.accessToken) &&
                    DateTimeOffset.TryParse(
                        response.expiresAt,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out _))
                {
                    onSuccess?.Invoke(response);
                }
                else
                {
                    onFailure?.Invoke(new AuthApiError(
                        request.responseCode,
                        "invalid_response",
                        "The server returned an invalid response."));
                }
            }
            else
            {
                onFailure?.Invoke(ParseError(request));
            }

            request.Dispose();
        }

        private IEnumerator SendUserRequest(
            UnityWebRequest request,
            Action<AuthUserPayload> onSuccess,
            Action<AuthApiError> onFailure)
        {
            yield return SendRequest(request);
            if (request.result == UnityWebRequest.Result.Success &&
                request.responseCode >= 200 &&
                request.responseCode < 300)
            {
                AuthUserPayload response = TryParse<AuthUserPayload>(request.downloadHandler.text);
                if (response != null && !string.IsNullOrEmpty(response.username))
                {
                    onSuccess?.Invoke(response);
                }
                else
                {
                    onFailure?.Invoke(new AuthApiError(
                        request.responseCode,
                        "invalid_response",
                        "The server returned an invalid response."));
                }
            }
            else
            {
                onFailure?.Invoke(ParseError(request));
            }

            request.Dispose();
        }

        private IEnumerator SendModelRequest<T>(
            UnityWebRequest request,
            Action<T> onSuccess,
            Action<AuthApiError> onFailure)
            where T : class
        {
            yield return SendRequest(request);
            if (request.result == UnityWebRequest.Result.Success &&
                request.responseCode >= 200 &&
                request.responseCode < 300)
            {
                T response = TryParse<T>(request.downloadHandler.text);
                if (response != null)
                {
                    onSuccess?.Invoke(response);
                }
                else
                {
                    onFailure?.Invoke(new AuthApiError(
                        request.responseCode,
                        "invalid_response",
                        "The server returned an invalid response."));
                }
            }
            else
            {
                onFailure?.Invoke(ParseError(request));
            }

            request.Dispose();
        }

        private IEnumerator SendRequest(UnityWebRequest request)
        {
            request.timeout = timeoutSeconds;
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Accept", "application/json");
            yield return request.SendWebRequest();
        }

        private static void AddAuthorization(UnityWebRequest request, string accessToken)
        {
            request.SetRequestHeader("Authorization", "Bearer " + (accessToken ?? string.Empty));
        }

        private UnityWebRequest CreateJsonRequest(string method, string path, string json)
        {
            UnityWebRequest request = new UnityWebRequest(BuildUrl(path), method);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json ?? string.Empty));
            request.SetRequestHeader("Content-Type", "application/json");
            return request;
        }

        private AuthApiError ParseError(UnityWebRequest request)
        {
            ErrorResponsePayload response = TryParse<ErrorResponsePayload>(
                request.downloadHandler == null ? string.Empty : request.downloadHandler.text);
            if (response != null && !string.IsNullOrEmpty(response.message))
            {
                return new AuthApiError(
                    request.responseCode,
                    string.IsNullOrEmpty(response.code) ? "request_failed" : response.code,
                    response.message);
            }

            if (request.responseCode == 0)
            {
                return new AuthApiError(
                    0,
                    "network_error",
                    "Unable to connect to the authentication server.");
            }

            return new AuthApiError(
                request.responseCode,
                "request_failed",
                "The authentication request could not be completed.");
        }

        private static T TryParse<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch
            {
                return null;
            }
        }

        private string BuildUrl(string path)
        {
            return baseUrl + path;
        }
    }
}
