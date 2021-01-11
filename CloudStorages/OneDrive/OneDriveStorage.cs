using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graph;

namespace CloudStorages.OneDrive
{
    public class OneDriveStorage : IUriHandler
    {
        private const int CHUNK_SIZE = 1024 * 320;  // default size is 320kb
        private readonly string ApiKey, ApiSecret, RedirectUri;
        private OneDriveOauthClient oauthClient;
        private GraphServiceClient graphServiceClient;
        private IDriveRequestBuilder myDriveBuilder;

        public Func<string> LoadAccessTokenDelegate { get; set; }
        public Func<string> LoadRefreshTokenDelegate { get; set; }
        public Action<string> SaveAccessTokenDelegate { get; set; }
        public Action<string> SaveRefreshTokenDelegate { get; set; }

        public event EventHandler<CloudStorageProgressArgs> ProgressChanged;

        public OneDriveStorage(string apiKey, string apiSecret = null, string redirectUri = null)
        {
            ApiKey = apiKey;
            ApiSecret = apiSecret;
            RedirectUri = redirectUri;
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

        private async Task<string> FindFolderId(string parentId, string folderName)
        {
            IDriveItemRequestBuilder driveItemsRequest;
            if (string.IsNullOrEmpty(parentId))
                driveItemsRequest = myDriveBuilder.Root;
            else
                driveItemsRequest = myDriveBuilder.Items[parentId];

            var page = await driveItemsRequest.Search(folderName).Request().GetAsync();
            foreach (DriveItem item in page.CurrentPage)
            {
                if (item.Folder != null && item.Name == folderName)
                    return item.Id;
            }

            return null;
        }

        private async Task<string> CreateFolder(string parentId, string folderName)
        {
            IDriveItemRequestBuilder driveItemsRequest;
            if (string.IsNullOrEmpty(parentId))
                driveItemsRequest = myDriveBuilder.Root;
            else
                driveItemsRequest = myDriveBuilder.Items[parentId];

            DriveItem newFolder = new DriveItem()
            {
                Name = folderName,
                Folder = new Folder()
            };
            newFolder = await driveItemsRequest.Children.Request().AddAsync(newFolder);
            return newFolder.Id;
        }

        public async Task<CloudStorageResult> DeleteFileByIdAsync(string fileID)
        {
            CloudStorageResult result = new CloudStorageResult();
            if (string.IsNullOrEmpty(fileID))
            {
                result.Status = Status.NotFound;
                return result;
            }

            try
            {
                await myDriveBuilder.Items[fileID].Request().DeleteAsync();
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
            if (string.IsNullOrEmpty(fileID))
            {
                result.Status = Status.NotFound;
                return result;
            }

            try
            {
                using (Stream downloadStream = await myDriveBuilder.Items[fileID].Content.Request().GetAsync(ct))
                {
                    using (FileStream fileStream = new FileStream(savePath, FileMode.Create))
                    {
                        byte[] buffer = new byte[CHUNK_SIZE];
                        int length;
                        do
                        {
                            // check if user cancelling
                            if (ct.IsCancellationRequested)
                                throw new TaskCanceledException("Download cancelled.");

                            // get each chunk from remote
                            length = await downloadStream.ReadAsync(buffer, 0, CHUNK_SIZE, ct);
                            await fileStream.WriteAsync(buffer, 0, length, ct);

                            // Update progress bar with the percentage.
                            Console.WriteLine($"Downloaded {length} bytes.");
                            OnProgressChanged(length);
                        } while (length > 0);
                        fileStream.Close();
                    }
                    downloadStream.Close();
                }
                result.Status = Status.Success;
            }
            catch (TaskCanceledException ex)
            {
                result.Status = Status.Cancelled;
                result.Message = ex.Message;
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
            }
            return result;
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

        public async Task<CloudStorageFile> GetFileInfoAsync(string fileId)
        {
            DriveItem item = await myDriveBuilder.Items[fileId].Request().GetAsync();
            return new CloudStorageFile
            {
                Name = item.Name,
                Id = item.Id,
                CreatedTime = item.CreatedDateTime?.DateTime ?? DateTime.MinValue,
                ModifiedTime = item.LastModifiedDateTime?.DateTime ?? DateTime.MinValue,
                Size = item.Size ?? 0,
                IsFolder = (item.Folder != null)
            };
        }

        public async Task<IEnumerable<CloudStorageFile>> GetFileInfosInPathAsync(string folderId)
        {
            List<CloudStorageFile> files = new List<CloudStorageFile>();
            IDriveItemRequestBuilder driveItemsRequest;
            if (string.IsNullOrEmpty(folderId))
                driveItemsRequest = myDriveBuilder.Root;
            else
                driveItemsRequest = myDriveBuilder.Items[folderId];
            var page = await driveItemsRequest.Children.Request().GetAsync();
            
            while (true)
            {
                foreach (DriveItem item in page.CurrentPage)
                {
                    files.Add(new CloudStorageFile
                    {
                        Name = item.Name,
                        CreatedTime = item.CreatedDateTime?.LocalDateTime ?? DateTime.MinValue,
                        ModifiedTime = item.LastModifiedDateTime?.LocalDateTime ?? DateTime.MinValue,
                        Id = item.Id,
                        Size = item.Size ?? 0,
                        IsFolder = (item.Folder != null)
                    });
                }

                // If NextPageRequest is not null there is another page to load
                if (page.NextPageRequest != null)
                {
                    // Load the next page
                    page = await page.NextPageRequest.GetAsync();
                }
                else
                {
                    // No other pages to load
                    break;
                }
            }

            return files;
        }

        public async Task<string> GetFolderIdAsync(string parentId, string folderName)
        {
            return await FindFolderId(parentId, folderName);
        }

        public async Task<string> GetRootFolderIdAsync()
        {
            var rootDrive = await myDriveBuilder.Root.Request().GetAsync();
            return rootDrive.Id;
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
                        result.Message = oauthClient.LastError;
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
        /// 取得空間資訊，並初始化 Drive 物件
        /// </summary>
        private async Task<(CloudStorageResult result, long usedSpace, long totalSpace)> GetRootInfoAsync()
        {
            CloudStorageResult result = new CloudStorageResult();
            long usedSpace = 0, totalSpace = 0;
            try
            {
                myDriveBuilder = graphServiceClient.Me.Drive;
                Drive myDrive = await myDriveBuilder.Request().GetAsync();
                usedSpace = myDrive.Quota.Used ?? 0;
                totalSpace = myDrive.Quota.Total ?? 0;
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

        public async Task<(CloudStorageResult, CloudStorageFile)> UploadFileToFolderByIdAsync(string filePath, string folderId, CancellationToken ct)
        {
            IDriveItemRequestBuilder driveItemsRequest;
            if (string.IsNullOrEmpty(folderId))
                driveItemsRequest = myDriveBuilder.Root;
            else
                driveItemsRequest = myDriveBuilder.Items[folderId];

            CloudStorageResult result = new CloudStorageResult();
            CloudStorageFile file = null;

            // Use properties to specify the conflict behavior in this case, replace
            var uploadProps = new DriveItemUploadableProperties
            {
                ODataType = null,
                AdditionalData = new Dictionary<string, object>
                {
                    { "@microsoft.graph.conflictBehavior", "replace" }
                }
            };


            try
            {
                // Create an upload session for a file with the same name of the user selected file
                UploadSession session = await driveItemsRequest.ItemWithPath(Path.GetFileName(filePath)).CreateUploadSession(uploadProps).Request().PostAsync(ct);

                using (FileStream fileStream = new FileStream(filePath, FileMode.Open))
                {
                    var fileUploadTask = new LargeFileUploadTask<DriveItem>(session, fileStream, CHUNK_SIZE);

                    // Create a callback that is invoked after each slice is uploaded
                    IProgress<long> progressCallback = new Progress<long>((progress) =>
                    {
                        Console.WriteLine($"Uploaded {progress} bytes.");
                        OnProgressChanged(progress);
                    });

                    // Upload the file
                    var uploadResult = await fileUploadTask.UploadAsync(progressCallback);

                    if (uploadResult.UploadSucceeded)
                    {
                        // The ItemResponse object in the result represents the created item.
                        file = new CloudStorageFile()
                        {
                            Name = uploadResult.ItemResponse.Name,
                            Id = uploadResult.ItemResponse.Id,
                            CreatedTime = uploadResult.ItemResponse.CreatedDateTime?.DateTime ?? DateTime.MinValue,
                            ModifiedTime = uploadResult.ItemResponse.LastModifiedDateTime?.DateTime ?? DateTime.MinValue,
                            Size = uploadResult.ItemResponse.Size ?? 0
                        };
                        result.Status = Status.Success;
                    }
                    else
                    {
                        result.Message = "Upload failed.";
                    }
                }
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
            }

            return (result, file);
        }

        private void OnProgressChanged(long bytesSent)
        {
            ProgressChanged?.Invoke(this, new CloudStorageProgressArgs {BytesSent = bytesSent});
        }

        public async Task<CloudStorageResult> AuthenticateFromUri(string state, string uri)
        {
            CloudStorageResult result = new CloudStorageResult();
            try
            {
                bool success = await oauthClient.ProcessUriAsync(state, uri);
                if (success)
                {
                    result.Status = Status.Success;
                    SaveAccessTokenDelegate?.Invoke(oauthClient.AccessToken);
                    SaveRefreshTokenDelegate?.Invoke(oauthClient.RefreshToken);
                    InitDriveService();
                }
                else
                    result.Status = Status.NeedAuthenticate;
            }
            catch (Exception e)
            {
                result.Message = e.Message;
            }
            return result;
        }

        public string LoginToUri()
        {
            StopListen();
            oauthClient = new OneDriveOauthClient(ApiKey, null, RedirectUri);
            string state = oauthClient.GetTokenToUri();
            return state;
        }
    }
}
