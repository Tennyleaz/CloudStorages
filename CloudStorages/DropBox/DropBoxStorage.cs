using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Dropbox.Api;
using Dropbox.Api.Files;

namespace CloudStorages.DropBox
{
    public class DropBoxStorage : ICloudStorageClient
    {
        //private const string DateTimeFormat = "yyyy-MM-dd HHmmss";
        private const int CHUNK_SIZE = 1024 * 100;  // default size is 100kb
        private string LastRefreshToken = null;
        private string LastAccessToken = null;
        private DropBoxOauthClient oAuthWrapper;
        private DropboxClient dropboxClient;
        private readonly string ApiKey, RedirectUrl;

        public Func<string> LoadAccessTokenDelegate { get; set; }
        public Func<string> LoadRefreshTokenDelegate { get; set; }
        public Action<string> SaveAccessTokenDelegate { get; set; }
        public Action<string> SaveRefreshTokenDelegate { get; set; }

        public event EventHandler<CloudStorageProgressArgs> ProgressChanged;

        public DropBoxStorage(string apiKey, string redirectUrl)
        {
            ApiKey = apiKey;
            RedirectUrl = redirectUrl;
        }

        public async Task<(CloudStorageResult result, bool IsNeedLogin)> InitAsync()
        {
            CloudStorageResult result = new CloudStorageResult();
            bool IsNeedLogin = true;

            try
            {
                // 取得上次登入資訊
                LastAccessToken = LoadAccessTokenDelegate?.Invoke();
                LastRefreshToken = LoadRefreshTokenDelegate?.Invoke();

                // 初始化
                oAuthWrapper = new DropBoxOauthClient(ApiKey, RedirectUrl);
                if (!string.IsNullOrEmpty(LastRefreshToken))  // offline 類型的 token
                {
                    IsNeedLogin = !await oAuthWrapper.RefreshTokenAsync(LastRefreshToken);
                    if (IsNeedLogin)
                    {
                        LastAccessToken = LastRefreshToken = null;
                    }
                    else
                    {
                        // 儲存新的access token/refresh token
                        SaveAccessTokenDelegate?.Invoke(oAuthWrapper.AccessToken);
                        InitDriveService();
                    }
                }
                else if (!string.IsNullOrEmpty(LastAccessToken))  // legacy 類型的 token，不需要refresh
                {
                    InitDriveService();
                    IsNeedLogin = false;
                }
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
            }
            return (result, IsNeedLogin);
        }

        public async Task<(CloudStorageResult, CloudStorageAccountInfo)> LoginAsync()
        {
            CloudStorageResult result = new CloudStorageResult();
            CloudStorageAccountInfo accountInfo = new CloudStorageAccountInfo();
            try
            {
                if (await oAuthWrapper.GetTokenAsync())
                {
                    SaveAccessTokenDelegate?.Invoke(oAuthWrapper.AccessToken);
                    SaveRefreshTokenDelegate?.Invoke(oAuthWrapper.RefreshToken);
                    LastRefreshToken = oAuthWrapper.RefreshToken;
                    InitDriveService();
                    (result, accountInfo.userName, accountInfo.userEmail) = await GetUserInfoAsync();
                    if (result.Success)
                    {
                        (result, accountInfo.usedSpace, accountInfo.totalSpace) = await GetRootInfoAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
            }
            return (result, accountInfo);
        }

        public async Task<(CloudStorageResult result, CloudStorageAccountInfo info)> GetAccountInfoAsync()
        {
            CloudStorageResult result = new CloudStorageResult();
            CloudStorageAccountInfo info = new CloudStorageAccountInfo();
            if (string.IsNullOrEmpty(LastRefreshToken))
                return (result, info);

            (result, info.userName, info.userEmail) = await GetUserInfoAsync();
            if (result.Success)
                (result, info.usedSpace, info.totalSpace) = await GetRootInfoAsync();
            return (result, info);
        }

        private void InitDriveService()
        {
            dropboxClient?.Dispose();
            if (!string.IsNullOrEmpty(LastRefreshToken))
                dropboxClient = new DropboxClient(LastRefreshToken, ApiKey);  // offline 類型的 token
            else
                dropboxClient = new DropboxClient(LastAccessToken);  // legacy 類型的 token
        }

        /// <summary>
        /// 取得空間資訊
        /// </summary>
        private async Task<(CloudStorageResult result, long usedSpace, long totalSpace)> GetRootInfoAsync()
        {
            long usedSpace = 0, totalSpace = 0;
            CloudStorageResult result = new CloudStorageResult();
            try
            {
                var usage = await dropboxClient.Users.GetSpaceUsageAsync();
                totalSpace = (long)usage.Allocation.AsIndividual.Value.Allocated;
                usedSpace = (long)usage.Used;
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
            }
            return (result, usedSpace, totalSpace);
        }

        /// <summary>
        /// 取得 User Name, Email
        /// </summary>
        private async Task<(CloudStorageResult result, string userName, string userEmail)> GetUserInfoAsync()
        {
            string userName = null, userEmail = null;
            CloudStorageResult result = new CloudStorageResult();
            try
            {
                var fullAccount = await dropboxClient.Users.GetCurrentAccountAsync();
                userName = fullAccount.Name.DisplayName;
                userEmail = fullAccount.Email;
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
            }
            return (result, userName, userEmail);
        }

        public string CreateFolder(string parentId, string folderName)
        {
            throw new NotImplementedException();
        }

        public string CreateFolder(string fullFolderPath)
        {
            throw new NotImplementedException();
        }

        public Task<CloudStorageResult> DeleteFileByIdAsync(string fileID)
        {
            throw new NotImplementedException();
        }

        public Task<CloudStorageResult> DeleteFileByPathAsync(string filePath)
        {
            throw new NotImplementedException();
        }

        public CloudStorageResult DownloadFileById(string fileID, string savePath)
        {
            throw new NotImplementedException();
        }

        public CloudStorageResult DownloadFileByPath(string filePath, string savePath)
        {
            throw new NotImplementedException();
        }

        public CloudStorageFile GetFileInfo(string filePath)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<CloudStorageFile> GetFileInfosInPath(string filePath)
        {
            throw new NotImplementedException();
        }

        public string GetFolderId(string parentId, string folderName)
        {
            throw new NotImplementedException();
        }

        public string GetFolderId(string fullFolderPath)
        {
            throw new NotImplementedException();
        }

        public void StopListen()
        {
            oAuthWrapper?.StopListen();
        }

        public CloudStorageResult UploadFileToFolderById(string filePath, string folderId = null)
        {
            throw new NotImplementedException();
        }

        public CloudStorageResult UploadFileToFolderByPath(string filePath, string folderName = null)
        {
            throw new NotImplementedException();
        }
    }
}
