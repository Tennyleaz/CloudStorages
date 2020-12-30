using System;
using System.Diagnostics;

namespace CloudStorages
{
    [DebuggerDisplay("[{Status}] {Message}")]
    public struct CloudStorageResult
    {
        public Status Status;
        public string Message;
    }

    public struct CloudStorageAccountInfo
    {
        public string userName, userEmail;
        public long totalSpace, usedSpace;
    }

    public class CloudStorageFile
    {
        public long Size;
        public string Name;
        public string Id;
        public DateTime ModifiedTime;
        public DateTime CreatedTime;
    }

    public class CloudStorageProgressArgs : EventArgs
    {
        public long BytesSent;
    }

    internal class OAuth2Response
    {
        public string AccessToken { get; private set; }

        public string Uid { get; private set; }

        public string State { get; private set; }

        public string TokenType { get; private set; }

        public string RefreshToken { get; private set; }

        public DateTime? ExpiresAt { get; private set; }

        public string[] ScopeList { get; private set; }

        internal OAuth2Response(string accessToken, string refreshToken, string uid, string state, string tokenType, int expiresIn, string[] scopeList)
        {
            if (string.IsNullOrEmpty(accessToken) || uid == null)
            {
                throw new ArgumentException("Invalid OAuth 2.0 response, missing access_token and/or uid.");
            }

            this.AccessToken = accessToken;
            this.Uid = uid;
            this.State = state;
            this.TokenType = tokenType;
            this.RefreshToken = refreshToken;
            this.ExpiresAt = DateTime.Now.AddSeconds(expiresIn);
            this.ScopeList = scopeList;
        }

        internal OAuth2Response(string accessToken, string uid, string state, string tokenType)
        {
            if (string.IsNullOrEmpty(accessToken) || uid == null)
            {
                throw new ArgumentException("Invalid OAuth 2.0 response, missing access_token and/or uid.");
            }

            this.AccessToken = accessToken;
            this.Uid = uid;
            this.State = state;
            this.TokenType = tokenType;
            this.RefreshToken = null;
            this.ExpiresAt = null;
            this.ScopeList = null;
        }

        internal OAuth2Response(string accessToken, string refreshToken, string[] scope, string tokenType, int expiresIn)
        {
            if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken))
            {
                throw new ArgumentException("Invalid OAuth 2.0 response, missing access_token and/or refresh_token.");
            }

            this.AccessToken = accessToken;
            this.Uid = null;
            this.State = null;
            this.TokenType = tokenType;
            this.RefreshToken = refreshToken;
            if (expiresIn > 0)
                this.ExpiresAt = DateTime.Now.AddSeconds(expiresIn);
            this.ScopeList = scope;
        }
    }
}
