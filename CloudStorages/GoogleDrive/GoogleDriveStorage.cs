using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;

namespace CloudStorages.GoogleDrive
{
    public class GoogleDriveStorage : ICloudStorageClient
    {
        private const string UerInfoEndpoint = "https://www.googleapis.com/userinfo/v2/me?oauth_token=";
        private const string FOLDER_TYPE = "application/vnd.google-apps.folder";
        private readonly string ApiKey, ApiSecret, AppName;
        private string LastRefreshToken;
        private GoogleDriveOauthClient oauthClient;
        private DriveService driveService;

        public Func<string> LoadAccessTokenDelegate { get; set; }
        public Func<string> LoadRefreshTokenDelegate { get; set; }
        public Action<string> SaveAccessTokenDelegate { get; set; }
        public Action<string> SaveRefreshTokenDelegate { get; set; }

        public event EventHandler<CloudStorageProgressArgs> ProgressChanged;

        public GoogleDriveStorage(string apiKey, string apiSecret, string appName)
        {
            AppName = appName;
            ApiKey = apiKey;
            ApiSecret = apiSecret;
        }

        public async Task<(CloudStorageResult result, string folderId)> CreateFolderAsync(string parentId, string folderName)
        {
            CloudStorageResult result = new CloudStorageResult();
            string folderId = null;
            try
            {
                folderId = await TryCreateFolder(parentId, folderName);
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
            }
            return (result, folderId);
        }

        public async Task<(CloudStorageResult result, string folderId)> CreateFolderAsync(string fullFolderPath)
        {
            CloudStorageResult result = new CloudStorageResult();
            string folderId = null;
            if (string.IsNullOrEmpty(fullFolderPath))
            {
                return (result, null);
            }

            try
            {
                // need to create folder recursively
                var folders = fullFolderPath.Split(new[] {'/'}, StringSplitOptions.RemoveEmptyEntries);

                // create first folder under root
                folderId = "root";
                foreach (string folderName in folders)
                {
                    folderId = await TryCreateFolder(folderId, folderName);
                }
                result.Success = true;
            }
            catch (Exception e)
            {
                result.Message = e.Message;
            }
            return (result, folderId);
        }

        /// <summary>
        /// 先檢查 parentId 下是否有同名資料夾，沒有再建立新資料夾。
        /// </summary>
        /// <param name="parentId"></param>
        /// <param name="folderName"></param>
        /// <returns>回傳現有或是新建立的資料夾 ID</returns>
        private async Task<string> TryCreateFolder(string parentId, string folderName)
        {
            string folderId = await FindFolderId(parentId, folderName);
            if (!string.IsNullOrEmpty(folderId))
                return folderId;
            return await CreateFolder(parentId, folderName);
        }

        private async Task<string> CreateFolder(string parentId, string folderName)
        {
            Google.Apis.Drive.v3.Data.File file = new Google.Apis.Drive.v3.Data.File();
            file.MimeType = FOLDER_TYPE;
            file.Name = folderName;
            if (!string.IsNullOrEmpty(parentId))
                file.Parents = new List<string> { parentId };
            var createRequest = driveService.Files.Create(file);
            file = await createRequest.ExecuteAsync();
            return file.Id;
        }

        /// <summary>
        /// Split a directory in its components.
        /// Input e.g: a/b/c/d.
        /// Output: d, c, b, a.
        /// </summary>
        /// <param name="Dir"></param>
        /// <returns></returns>
        private static IEnumerable<string> DirectorySplit(DirectoryInfo Dir)
        {
            while (Dir != null)
            {
                yield return Dir.Name;
                Dir = Dir.Parent;
            }
        }

        public Task<CloudStorageResult> DeleteFileByIdAsync(string fileID)
        {
            throw new NotImplementedException();
        }

        public Task<CloudStorageResult> DeleteFileByPathAsync(string filePath)
        {
            throw new NotImplementedException();
        }

        public Task<CloudStorageResult> DownloadFileByIdAsync(string fileID, string savePath, CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public Task<CloudStorageResult> DownloadFileByPathAsync(string filePath, string savePath, CancellationToken ct)
        {
            throw new NotImplementedException();
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

        public Task<CloudStorageFile> GetFileInfoAsync(string filePath)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<CloudStorageFile>> GetFileInfosInPathAsync(string folderPath)
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

        private async Task<string> FindFolderId(string parentId, string folderName)
        {
            string query = $"trashed=false and mimeType='{FOLDER_TYPE}'";
            if (!string.IsNullOrEmpty(parentId))
                query += $" and '{parentId}' in parents";
            try
            {
                var listRequest = driveService.Files.List();
                listRequest.Q = query;
                var folderList = await listRequest.ExecuteAsync();
                foreach (var f in folderList.Files)
                {
                    if (f.Name == folderName)
                    {
                        return f.Id;
                    }
                }
            }
            catch (Google.GoogleApiException ex)
            {
                if (ex.Error.Code == 404)  // 只處理找不到
                    return null;
                throw ex;
            }
            return null;
        }

        public async Task<(CloudStorageResult result, bool IsNeedLogin)> InitAsync()
        {
            CloudStorageResult result = new CloudStorageResult();
            bool IsNeedLogin = true;

            try
            {
                // 取得上次登入資訊
                LastRefreshToken = LoadRefreshTokenDelegate?.Invoke();

                // 初始化
                oauthClient = new GoogleDriveOauthClient(ApiKey, ApiSecret);
                if (!string.IsNullOrEmpty(LastRefreshToken))
                {
                    IsNeedLogin = !await oauthClient.RefreshTokenAsync(LastRefreshToken);
                    if (IsNeedLogin)
                    {
                        LastRefreshToken = null;
                    }
                    else
                    {
                        // 儲存新的access token/refresh token
                        SaveAccessTokenDelegate?.Invoke(oauthClient.AccessToken);
                        InitDriveService();
                    }
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
                if (await oauthClient.GetTokenAsync())
                {
                    SaveAccessTokenDelegate?.Invoke(oauthClient.AccessToken);
                    SaveRefreshTokenDelegate?.Invoke(oauthClient.RefreshToken);
                    (result, accountInfo.userName, accountInfo.userEmail) = await GetUserInfoAsync();
                    if (result.Success)
                    {
                        InitDriveService();
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

        private void InitDriveService()
        {
            // see: https://stackoverflow.com/q/38390197/3576052
            GoogleCredential credential = GoogleCredential.FromAccessToken(oauthClient.AccessToken);
            driveService = new DriveService(new BaseClientService.Initializer
                {
                    ApplicationName = AppName,
                    HttpClientInitializer = credential
                }
            );
        }

        /// <summary>
        /// 取得空間資訊，totalSpace=-1 代表無限空間
        /// </summary>
        private async Task<(CloudStorageResult result, long usedSpace, long totalSpace)> GetRootInfoAsync()
        {
            long usedSpace = 0, totalSpace = 0;
            CloudStorageResult result = new CloudStorageResult();

            try
            {
                var getRequest = driveService.About.Get();
                getRequest.Fields = "*";  // Error: 'fields' parameter is required for this method
                About about = await getRequest.ExecuteAsync();
                totalSpace = about.StorageQuota.Limit ?? -1;
                usedSpace = about.StorageQuota.Usage ?? -1;
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
        /// 取得User Name, Email
        /// </summary>
        private async Task<(CloudStorageResult result, string userName, string userEmail)> GetUserInfoAsync()
        {
            string userName = null, userEmail = null;
            CloudStorageResult result = new CloudStorageResult();
            
            try
            {
                System.Net.WebRequest request = System.Net.WebRequest.Create(UerInfoEndpoint + oauthClient.AccessToken);
                request.Method = "GET";
                request.ContentType = "application/json";
                // Get the response.
                System.Net.WebResponse response = await request.GetResponseAsync();
                // Display the status.
                //Console.WriteLine(((HttpWebResponse)response).StatusDescription);
                // Get the stream containing content returned by the server.
                Stream dataStream = response.GetResponseStream();
                // Open the stream using a StreamReader for easy access.
                StreamReader reader = new StreamReader(dataStream);
                // Read the content.
                string responseFromServer = await reader.ReadToEndAsync();
                // Display the content.
                Console.WriteLine(responseFromServer);
                // Clean up the streams and the response.
                reader.Close();
                reader.Dispose();
                dataStream.Close();
                dataStream.Dispose();
                response.Close();
                response.Dispose();
                if (!string.IsNullOrEmpty(responseFromServer))
                {
                    Newtonsoft.Json.Linq.JObject jObject = Newtonsoft.Json.Linq.JObject.Parse(responseFromServer);
                    userEmail = jObject["email"]?.ToString();
                    userName = jObject["name"]?.ToString();
                    result.Success = true;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
            }
            return (result, userName, userEmail);
        }

        public void StopListen()
        {
            oauthClient?.StopListen();
            oauthClient = null;
        }

        public Task<CloudStorageResult> UploadFileToFolderByIdAsync(string filePath, CancellationToken ct, string folderId = null)
        {
            throw new NotImplementedException();
        }

        public Task<CloudStorageResult> UploadFileToFolderByPathAsync(string filePath, CancellationToken ct, string folderName = null)
        {
            throw new NotImplementedException();
        }
    }
}
