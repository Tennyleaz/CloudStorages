using System;
using System.Threading.Tasks;

namespace CloudStorages
{
    public interface IOauthClient
    {
        void StopListen();

        Task<bool> RefreshTokenAsync(string refreshToken);

        Task<bool> GetTokenAsync();

        string AccessToken { get; }

        string RefreshToken { get; }

        DateTime? ExpiresAt { get; }
    }
}
