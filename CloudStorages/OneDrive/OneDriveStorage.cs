using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graph;

namespace CloudStorages.OneDrive
{
    public class OneDriveStorage : ICloudStorageClient
    {
        private readonly string ApiKey, ApiSecret, AppName;
        private OneDriveOauthClient oauthClient;
        private GraphServiceClient graphServiceClient;

        public Func<string> LoadAccessTokenDelegate { get; set; }
        public Func<string> LoadRefreshTokenDelegate { get; set; }
        public Action<string> SaveAccessTokenDelegate { get; set; }
        public Action<string> SaveRefreshTokenDelegate { get; set; }

        public event EventHandler<CloudStorageProgressArgs> ProgressChanged;

        public OneDriveStorage(string apiKey, string apiSecret = null)
        {
            ApiKey = apiKey;
            ApiSecret = apiSecret;
        }

        public Task<(CloudStorageResult result, string folderId)> CreateFolderAsync(string parentId, string folderName)
        {
            throw new NotImplementedException();
        }

        public Task<CloudStorageResult> DeleteFileByIdAsync(string fileID)
        {
            throw new NotImplementedException();
        }

        public Task<CloudStorageResult> DownloadFileByIdAsync(string fileID, string savePath, CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public async Task<(CloudStorageResult result, CloudStorageAccountInfo info)> GetAccountInfoAsync()
        {
            CloudStorageResult result;
            CloudStorageAccountInfo info = new CloudStorageAccountInfo();

            (result, info.userName, info.userEmail) = await GetUserInfoAsync();
            if (result.Status == Status.Success)
                (result, info.usedSpace, info.totalSpace) = await GetRootInfoAsync();
            return (result, info);
        }

        public Task<CloudStorageFile> GetFileInfoAsync(string fileId)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<CloudStorageFile>> GetFileInfosInPathAsync(string folderId)
        {
            throw new NotImplementedException();
        }

        public Task<string> GetFolderIdAsync(string parentId, string folderName)
        {
            throw new NotImplementedException();
        }

        public Task<string> GetRootFolderIdAsync()
        {
            throw new NotImplementedException();
        }

        public async Task<CloudStorageResult> InitAsync()
        {
            CloudStorageResult result = new CloudStorageResult();
            try
            {
                // 取得上次登入資訊
                string lastRefreshToken = LoadRefreshTokenDelegate?.Invoke();

                // 初始化
                result.Status = Status.NeedAuthenticate;
                oauthClient = new OneDriveOauthClient(ApiKey, ApiSecret);
                if (!string.IsNullOrEmpty(lastRefreshToken))
                {
                    bool needLogin = !await oauthClient.RefreshTokenAsync(lastRefreshToken);
                    if (needLogin)
                    {
                        lastRefreshToken = null;
                    }
                    else
                    {
                        result.Status = Status.Success;
                        // 儲存新的access token/refresh token
                        SaveAccessTokenDelegate?.Invoke(oauthClient.AccessToken);
                        InitDriveService();
                    }
                }
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
            }
            return result;
        }

        public async Task<(CloudStorageResult, CloudStorageAccountInfo)> LoginAsync()
        {
            CloudStorageResult result = new CloudStorageResult();
            CloudStorageAccountInfo accountInfo = new CloudStorageAccountInfo();
            try
            {
                if (await oauthClient.GetTokenAsync())
                {
                    SaveAccessTokenDelegate?.Invoke(oauthClient.AccessToken);
                    SaveRefreshTokenDelegate?.Invoke(oauthClient.RefreshToken);
                    InitDriveService();
                    (result, accountInfo.userName, accountInfo.userEmail) = await GetUserInfoAsync();
                    if (result.Status == Status.Success)
                        (result, accountInfo.usedSpace, accountInfo.totalSpace) = await GetRootInfoAsync();
                }
                else
                    result.Message = oauthClient.LastError;
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
            }
            return (result, accountInfo);
        }

        private void InitDriveService()
        {
            var provider = new DelegateAuthenticationProvider((requestMessage) =>
            {
                requestMessage
                    .Headers
                    .Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", oauthClient.AccessToken);
                return Task.FromResult(0);
            });
            graphServiceClient = new GraphServiceClient(provider);
        }

        /// <summary>
        /// 取得空間資訊
        /// </summary>
        private async Task<(CloudStorageResult result, long usedSpace, long totalSpace)> GetRootInfoAsync()
        {
            CloudStorageResult result = new CloudStorageResult();
            long usedSpace = 0, totalSpace = 0;
            try
            {
                Drive drive = await graphServiceClient.Me.Drive.Request().GetAsync();
                usedSpace = drive.Quota.Used ?? 0;
                totalSpace = drive.Quota.Total ?? 0;
                result.Status = Status.Success;
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
            }
            return (result, usedSpace, totalSpace);
        }

        /// <summary>
        /// 取得User Name, Email
        /// </summary>
        private async Task<(CloudStorageResult result, string userName, string userEmail)> GetUserInfoAsync()
        {
            CloudStorageResult result = new CloudStorageResult();
            string userName = null, userEmail = null;
            try
            {
                User me = await graphServiceClient.Users["me"].Request().GetAsync();
                userName = me.DisplayName;
                userEmail = me.Mail ?? me.UserPrincipalName;
                result.Status = Status.Success;
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
            }
            return (result, userName, userEmail);
        }

        public void StopListen()
        {
            oauthClient?.StopListen();
        }

        public Task<(CloudStorageResult, CloudStorageFile)> UploadFileToFolderByIdAsync(string filePath, string folderId, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
    }
}
