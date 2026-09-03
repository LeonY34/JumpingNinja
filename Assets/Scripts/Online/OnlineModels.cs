using System;
using System.Globalization;

namespace JumpingNinja
{
    [Serializable]
    internal sealed class AuthCredentialsPayload
    {
        public string username;
        public string password;
    }

    [Serializable]
    public sealed class AuthUserPayload
    {
        public string id;
        public string username;
    }

    [Serializable]
    public sealed class AuthResponsePayload
    {
        public AuthUserPayload user;
        public string accessToken;
        public string expiresAt;
    }

    [Serializable]
    internal sealed class ErrorResponsePayload
    {
        public string code;
        public string message;
        public string field;
    }

    public sealed class AuthApiError
    {
        public AuthApiError(long statusCode, string code, string message)
        {
            StatusCode = statusCode;
            Code = code;
            Message = message;
        }

        public long StatusCode { get; }
        public string Code { get; }
        public string Message { get; }
        public bool IsUnauthorized => StatusCode == 401;
    }

    public sealed class OnlineAuthSession
    {
        public AuthUserPayload CurrentUser { get; private set; }
        public string AccessToken { get; private set; }
        public string ExpiresAt { get; private set; }
        public bool HasSession =>
            CurrentUser != null || !string.IsNullOrEmpty(AccessToken);
        public bool IsAuthenticated => HasSession && !IsExpired;
        public bool IsExpired
        {
            get
            {
                if (!HasSession)
                {
                    return false;
                }

                return !DateTimeOffset.TryParse(
                           ExpiresAt,
                           CultureInfo.InvariantCulture,
                           DateTimeStyles.RoundtripKind,
                           out DateTimeOffset expiresAt) ||
                       expiresAt <= DateTimeOffset.UtcNow;
            }
        }

        public void Apply(AuthResponsePayload response)
        {
            CurrentUser = response?.user;
            AccessToken = response?.accessToken;
            ExpiresAt = response?.expiresAt;
        }

        public void ApplyValidatedUser(AuthUserPayload user)
        {
            CurrentUser = user;
        }

        public void Clear()
        {
            CurrentUser = null;
            AccessToken = null;
            ExpiresAt = null;
        }
    }
}
