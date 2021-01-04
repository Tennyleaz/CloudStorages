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
        private const int CHUNK_SIZE = Google.Apis.Upload.ResumableUpload.MinimumChunkSize * 2;  // default is 512KB
        private readonly string ApiKey, ApiSecret, AppName;
        private GoogleDriveOauthClient oauthClient;
        private DriveService driveService;
        private TaskCompletionSource<bool> tcs;
        private CloudStorageFile tempFile;

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
                result.Status = Status.Success;
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
            }
            return (result, folderId);
        }

        [Obsolete]
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
                result.Status = Status.Success;
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

        public async Task<CloudStorageResult> DeleteFileByIdAsync(string fileID)
        {
            CloudStorageResult result = new CloudStorageResult();
            try
            {
                var deleteRequest = driveService.Files.Delete(fileID);
                fileID = await deleteRequest.ExecuteAsync();
                result.Status = Status.Success;
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
            }
            return result;
        }

        public async Task<CloudStorageResult> DownloadFileByIdAsync(string fileID, string savePath, CancellationToken ct)
        {
            CloudStorageResult result = new CloudStorageResult();
            try
            {
                if (System.IO.File.Exists(savePath))
                    System.IO.File.Delete(savePath);

                var getRequest = driveService.Files.Get(fileID);
                using (FileStream fileStream = new FileStream(savePath, FileMode.Create))
                {
                    getRequest.MediaDownloader.ChunkSize = CHUNK_SIZE;
                    getRequest.MediaDownloader.ProgressChanged += OnProgressChangedDownload;
                    var downloadProgress = await getRequest.DownloadAsync(fileStream, ct);
                    result.Message = downloadProgress.ToString();
                    if (downloadProgress.Status == Google.Apis.Download.DownloadStatus.Completed)
                    {
                        result.Status = Status.Success;
                    }
                    fileStream.Close();
                }
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
            }
            return result;
        }

        public async Task<(CloudStorageResult result, CloudStorageAccountInfo info)> GetAccountInfoAsync()
        {
            CloudStorageResult result = new CloudStorageResult();
            CloudStorageAccountInfo info = new CloudStorageAccountInfo();

            (result, info.userName, info.userEmail) = await GetUserInfoAsync();
            if (result.Status == Status.Success)
                (result, info.usedSpace, info.totalSpace) = await GetRootInfoAsync();
            return (result, info);
        }

        public async Task<CloudStorageFile> GetFileInfoAsync(string fileId)
        {
            var getRequest = driveService.Files.Get(fileId);
            Google.Apis.Drive.v3.Data.File googleFile = await getRequest.ExecuteAsync();
            CloudStorageFile fileInfo = new CloudStorageFile
            {
                Name = googleFile.Name,
                Id = googleFile.Id,
                CreatedTime = googleFile.CreatedTime ?? DateTime.MinValue,
                ModifiedTime = googleFile.ModifiedTime ?? DateTime.MinValue,
                Size = googleFile.Size ?? 0
            };
            return fileInfo;
        }

        public async Task<IEnumerable<CloudStorageFile>> GetFileInfosInPathAsync(string folderId)
        {
            List<CloudStorageFile> files = new List<CloudStorageFile>();

            var listRequest = driveService.Files.List();
            listRequest.Q = $"trashed=false and '{folderId}' in parents";
            FileList googleFileList = await listRequest.ExecuteAsync();
            foreach (Google.Apis.Drive.v3.Data.File googleFile in googleFileList.Files)
            {
                files.Add(new CloudStorageFile
                {
                    Name = googleFile.Name,
                    Id = googleFile.Id,
                    CreatedTime = googleFile.CreatedTime ?? DateTime.MinValue,
                    ModifiedTime = googleFile.ModifiedTime ?? DateTime.MinValue,
                    Size = googleFile.Size ?? 0
                });
            }

            return files;
        }

        public Task<string> GetRootFolderIdAsync()
        {
            return Task.FromResult("root");
        }

        public async Task<string> GetFolderIdAsync(string parentId, string folderName)
        {
            try
            {
                return await FindFolderId(parentId, folderName);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            return null;
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

        public async Task<CloudStorageResult> InitAsync()
        {
            CloudStorageResult result = new CloudStorageResult();
            bool IsNeedLogin = true;

            try
            {
                // 取得上次登入資訊
                string LastRefreshToken = LoadRefreshTokenDelegate?.Invoke();

                // 初始化
                result.Status = Status.NeedAuthenticate;
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
                    (result, accountInfo.userName, accountInfo.userEmail) = await GetUserInfoAsync();
                    if (result.Status == Status.Success)
                    {
                        InitDriveService();
                        (result, accountInfo.usedSpace, accountInfo.totalSpace) = await GetRootInfoAsync();
                    }
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
                    result.Status = Status.Success;
                }
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
            oauthClient = null;
        }

        public async Task<(CloudStorageResult, CloudStorageFile)> UploadFileToFolderByIdAsync(string filePath, string folderId, CancellationToken ct)
        {
            CloudStorageResult result = new CloudStorageResult();
            CloudStorageFile newFile = null;

            try
            {
                Google.Apis.Drive.v3.Data.File file = new Google.Apis.Drive.v3.Data.File();
                file.Name = Path.GetFileName(filePath);
                if (!string.IsNullOrEmpty(folderId))
                    file.Parents = new List<string> { folderId };

                // fire after google upload completed
                tcs = new TaskCompletionSource<bool>();

                using (FileStream stream = new FileStream(filePath, FileMode.Open))
                {
                    var createRequest = driveService.Files.Create(file, stream, Utility.ContentTypes.GetContentType(filePath));
                    createRequest.ProgressChanged += OnProgressChangedUpload;
                    createRequest.ResponseReceived += Request_ResponseReceived;
                    createRequest.ChunkSize = CHUNK_SIZE;
                    var uploadProgress = await createRequest.UploadAsync(ct);
                    result.Message = uploadProgress.ToString();
                    if (uploadProgress.Status == Google.Apis.Upload.UploadStatus.Completed)
                    {
                        // wait google complete event
                        await tcs.Task;

                        result.Status = Status.Success;
                        newFile = tempFile;
                    }
                    stream.Close();
                }
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
            }
            finally
            {
                tcs = null;
            }
            return (result, newFile);
        }

        private void OnProgressChangedUpload(Google.Apis.Upload.IUploadProgress progress)
        {
            var args = new CloudStorageProgressArgs { BytesSent = progress.BytesSent };
            ProgressChanged?.Invoke(null, args);
        }

        private void OnProgressChangedDownload(Google.Apis.Download.IDownloadProgress progress)
        {
            var args = new CloudStorageProgressArgs { BytesSent = progress.BytesDownloaded };
            ProgressChanged?.Invoke(null, args);
        }

        private void Request_ResponseReceived(Google.Apis.Drive.v3.Data.File file)
        {
            tempFile = new CloudStorageFile
            {
                Name = file.Name,
                Id = file.Id,
                CreatedTime = file.CreatedTime ?? DateTime.MinValue,
                ModifiedTime = file.ModifiedTime ?? DateTime.MinValue,
                Size = file.Size ?? 0
            };
            tcs?.TrySetResult(true);
        }
    }
}
