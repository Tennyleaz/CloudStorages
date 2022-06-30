using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Dropbox.Api;
using Dropbox.Api.Files;

namespace CloudStorages.DropBox
{
    public class DropBoxStorage : IUriHandler
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

        public async Task<CloudStorageResult> InitAsync()
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
                        result.Status = Status.NeedAuthenticate;
                        LastAccessToken = LastRefreshToken = null;
                    }
                    else
                    {
                        result.Status = Status.Success;
                        // 儲存新的access token/refresh token
                        SaveAccessTokenDelegate?.Invoke(oAuthWrapper.AccessToken);
                        InitDriveService();
                    }
                }
                else if (!string.IsNullOrEmpty(LastAccessToken))  // legacy 類型的 token，不需要refresh
                {
                    result.Status = Status.Success;
                    InitDriveService();
                }
                else
                {
                    // each token is empty, need login
                    result.Status = Status.NeedAuthenticate;
                    result.Message = "Empty access and refresh token.";
                }
            }
            catch (Exception ex)
            {
                result.Status = Status.UnknownError;
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
                if (await oAuthWrapper.GetTokenAsync())
                {
                    SaveAccessTokenDelegate?.Invoke(oAuthWrapper.AccessToken);
                    SaveRefreshTokenDelegate?.Invoke(oAuthWrapper.RefreshToken);
                    LastRefreshToken = oAuthWrapper.RefreshToken;
                    LastAccessToken = oAuthWrapper.AccessToken;
                    InitDriveService();
                    (result, accountInfo.userName, accountInfo.userEmail) = await GetUserInfoAsync();
                    if (result.Status == Status.Success)
                    {
                        (result, accountInfo.usedSpace, accountInfo.totalSpace) = await GetRootInfoAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                result.Status = Status.UnknownError;
                result.Message = ex.Message;
            }
            return (result, accountInfo);
        }

        public async Task<(CloudStorageResult result, CloudStorageAccountInfo info)> GetAccountInfoAsync()
        {
            CloudStorageResult result = new CloudStorageResult();
            CloudStorageAccountInfo info = new CloudStorageAccountInfo();
            //if (string.IsNullOrEmpty(LastRefreshToken))
            //    return (result, info);

            (result, info.userName, info.userEmail) = await GetUserInfoAsync();
            if (result.Status == Status.Success)
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
                result.Status = Status.Success;
            }
            catch (Exception ex)
            {
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
                result.Status = Status.Success;
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
            }
            return (result, userName, userEmail);
        }

        public async Task<(CloudStorageResult result, string folderId)> CreateFolderAsync(string parentId, string folderName)
        {
            // check valid folder id format
            string fullFolderPath = null;
            if (string.IsNullOrEmpty(parentId))
                fullFolderPath = "/";  // dropbox root folder
            else if (parentId.StartsWith("id:"))
            {
                // convert id into absolute path
                fullFolderPath = await TryGetFullPathAsync(parentId);
                if (string.IsNullOrEmpty(fullFolderPath))
                {
                    var result = new CloudStorageResult
                    {
                        Status = Status.NotFound,
                        Message = "Cannot find parent folder id: " + parentId
                    };
                    return (result, null);
                }
            }
            else if (parentId != "/")
                return (new CloudStorageResult { Message = "Wrong dropbox folder id format." }, null);
            
            if (!fullFolderPath.EndsWith("/"))
                fullFolderPath += "/";
            fullFolderPath += folderName;
            return await CreateFolderAsync(fullFolderPath);
        }

        private async Task<(CloudStorageResult result, string folderId)> CreateFolderAsync(string fullFolderPath)
        {
            CloudStorageResult result = new CloudStorageResult();
            string folderId;
            try
            {
                // check if folder already exist
                folderId = await TryGetFolderIdAsync(fullFolderPath);
                if (!string.IsNullOrEmpty(folderId))
                {
                    result.Status = Status.Success;
                    return (result, folderId);
                }

                var createResult = await dropboxClient.Files.CreateFolderV2Async(fullFolderPath);
                folderId = createResult.Metadata.Id;
                result.Status = Status.Success;
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
                folderId = null;
            }
            return (result, folderId);
        }

        private async Task<string> TryGetFullPathAsync(string id)
        {
            try
            {
                var metadata = await dropboxClient.Files.GetMetadataAsync(id);
                return metadata.PathLower;
            }
            catch (Exception ex)
            {

            }
            return null;
        }

        private async Task<string> TryGetFolderIdAsync(string fullFolderPath)
        {
            try
            {
                var metadata = await dropboxClient.Files.GetMetadataAsync(fullFolderPath);
                if (metadata.IsFolder)
                    return metadata.AsFolder.Id;
            }
            catch (Exception ex)
            {
                
            }
            return null;
        }

        public async Task<CloudStorageResult> DeleteFileByIdAsync(string fileID)
        {
            CloudStorageResult result = new CloudStorageResult();
            if (string.IsNullOrEmpty(fileID))
                return result;
            try
            {
                //fileID = fileID.Replace("id:", string.Empty);  // 要刪除前綴 "id:" 才是 dropbox 正確格式
                List<string> deletedIds = new List<string> { fileID };
                var deleteResult = await dropboxClient.Files.DeleteV2Async(fileID);
                result.Status = Status.Success;
            }
            catch (ApiException<Dropbox.Api.FileRequests.DeleteFileRequestError> ex)
            {
                result.Message = ex.ToString();
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
            }
            return result;
        }

        [Obsolete]
        public async Task<CloudStorageResult> DeleteFileByPathAsync(string filePath)
        {
            CloudStorageResult result = new CloudStorageResult();
            try
            {
                var deleteResult = await dropboxClient.Files.DeleteV2Async(filePath);
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
            if (!fileID.StartsWith("id:"))
                return new CloudStorageResult {Message = "Wrong dropbox file id format."};
            return await DownloadFileByPathAsync(fileID, savePath, ct);
        }

        private async Task<CloudStorageResult> DownloadFileByPathAsync(string filePath, string savePath, CancellationToken ct)
        {
            CloudStorageResult result = new CloudStorageResult();
            try
            {
                // prepare file to download
                using (FileStream fileStream = new FileStream(savePath, FileMode.Create))
                {
                    var getResult = await dropboxClient.Files.DownloadAsync(filePath);
                    using (Stream getStream = await getResult.GetContentAsStreamAsync())
                    {
                        byte[] buffer = new byte[CHUNK_SIZE];
                        int length;
                        do
                        {
                            // check if user cancelling
                            if (ct.IsCancellationRequested)
                                throw new TaskCanceledException("Download cancelled.");
                            // get each chunk from remote
                            length = await getStream.ReadAsync(buffer, 0, CHUNK_SIZE, ct);
                            await fileStream.WriteAsync(buffer, 0, length, ct);
                            // Update progress bar with the percentage.
                            OnProgressChanged(length);
                        } while (length > 0);
                        getStream.Close();
                    }
                    // close the file
                    await fileStream.FlushAsync(ct);
                    fileStream.Close();
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

        public async Task<CloudStorageFile> GetFileInfoAsync(string fileId)
        {
            CloudStorageFile fileInfo;
            try
            {
                var metadata = await dropboxClient.Files.GetMetadataAsync(fileId);
                if (metadata.IsFile)
                {
                    fileInfo = new CloudStorageFile
                    {
                        Name = metadata.Name,
                        //CreatedTime = ,
                        ModifiedTime = metadata.AsFile.ServerModified,
                        Id = metadata.AsFile.Id,
                        Size = (long)metadata.AsFile.Size
                    };
                }
                else
                {
                    fileInfo = new CloudStorageFile
                    {
                        Name = metadata.Name,
                        //CreatedTime = ,
                        //ModifiedTime = ,
                        Id = metadata.AsFile.Id,
                        //Size = ,
                        IsFolder = true
                    };
                }
            }
            catch (Exception ex)
            {
                throw new FileNotFoundException(ex.Message, fileId);
            }
            return fileInfo;
        }

        public async Task<IEnumerable<CloudStorageFile>> GetFileInfosInPathAsync(string folderId)
        {
            List<CloudStorageFile> fileInfos = new List<CloudStorageFile>();
            try
            {
                var listResult = await dropboxClient.Files.ListFolderAsync(folderId);
                AddEntries(listResult.Entries);
                while (listResult.HasMore)
                {
                    listResult = await dropboxClient.Files.ListFolderContinueAsync(listResult.Cursor);
                    AddEntries(listResult.Entries);
                }

                void AddEntries(IEnumerable<Metadata> entries)
                {
                    foreach (var f in entries)
                    {
                        if (f.IsFile)
                        {
                            CloudStorageFile file = new CloudStorageFile
                            {
                                Name = f.Name,
                                //CreatedTime = ,
                                ModifiedTime = f.AsFile.ServerModified,
                                Id = f.AsFile.Id,
                                Size = (long)f.AsFile.Size
                            };
                            fileInfos.Add(file);
                        }
                        else
                        {
                            CloudStorageFile file = new CloudStorageFile
                            {
                                Name = f.Name,
                                //CreatedTime = ,
                                //ModifiedTime = ,
                                Id = f.AsFolder.Id,
                                //Size = 
                                IsFolder = true
                            };
                            fileInfos.Add(file);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new FileNotFoundException(ex.Message, folderId);
            }
            return fileInfos;
        }

        /// <summary>
        /// Dropbox root folder does not have an Id. Use "" or "/" instead.
        /// </summary>
        /// <returns></returns>
        public Task<string> GetRootFolderIdAsync()
        {
            return Task.FromResult("");
        }

        public async Task<string> GetFolderIdAsync(string parentId, string folderName)
        {
            try
            {
                List<Metadata> entries = new List<Metadata>();
                var listResult = await dropboxClient.Files.ListFolderAsync(parentId);
                entries.AddRange(listResult.Entries);
                while (listResult.HasMore)
                {
                    listResult = await dropboxClient.Files.ListFolderContinueAsync(listResult.Cursor);
                    entries.AddRange(listResult.Entries);
                }

                foreach (Metadata metadata in entries)
                {
                    if (metadata.IsFolder && metadata.Name == folderName)
                    {
                        return metadata.AsFolder.Id;
                    }
                }
                return null;
            }
            catch (Exception e)
            {
                return null;
            }
        }

        public void StopListen()
        {
            oAuthWrapper?.StopListen();
        }

        public async Task<(CloudStorageResult, CloudStorageFile)> UploadFileToFolderByIdAsync(string filePath, string folderId, CancellationToken ct)
        {
            // check valid folder id format
            if (string.IsNullOrEmpty(folderId))
                folderId = "/";  // dropbox root folder
            else if (!folderId.StartsWith("id:"))
                return (new CloudStorageResult {Message = "Wrong dropbox folder id format."}, null);

            return await UploadFileToFolderByPathAsync(filePath, folderId, ct);
        }

        private async Task<(CloudStorageResult, CloudStorageFile)> UploadFileToFolderByPathAsync(string filePath, string folderName, CancellationToken ct)
        {
            CloudStorageResult result = new CloudStorageResult();
            CloudStorageFile cloudFile = null;
            try
            {
                FileInfo fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists)
                {
                    result.Status = Status.NotFound;
                    result.Message = $"File '{filePath}' does not exist.";
                    return (result, null);
                }

                // upload to dropbox root folder if folderName is empty
                if (string.IsNullOrEmpty(folderName))
                    folderName = "/";

                long fileSize = fileInfo.Length;
                if (fileSize <= CHUNK_SIZE)
                {
                    cloudFile = await UploadSmaillFile(filePath, folderName);
                    OnProgressChanged(fileSize);  // 一次回報全部進度
                }
                else
                {
                    cloudFile = await UploadBigFile(filePath, folderName, ct);
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
            return (result, cloudFile);
        }

        /// <summary>
        /// 上傳小於等於 CHUNK_SIZE 的檔案。不檢查輸入檔案，不會回報進度，也不能取消。
        /// </summary>
        private async Task<CloudStorageFile> UploadSmaillFile(string filePath, string parentFolder)
        {
            using (FileStream fileStream = new FileStream(filePath, FileMode.Open))
            {
                if (!parentFolder.EndsWith("/"))
                    parentFolder += "/";
                parentFolder += Path.GetFileName(filePath);
                var uploadResult = await dropboxClient.Files.UploadAsync(parentFolder, autorename: true, body: fileStream);
                Console.WriteLine("Finished small file: " + uploadResult.PathDisplay);
                CloudStorageFile cloudFile = new CloudStorageFile()
                {
                    Id = uploadResult.Id,
                    ModifiedTime = uploadResult.ServerModified,
                    Name = uploadResult.Name,
                    Size = (long) uploadResult.Size
                };
                return cloudFile;
            }
        }

        /// <summary>
        /// 批次上傳大檔案，必須要大於 CHUNK_SIZE。僅會檢查檔案大小是否夠大。
        /// </summary>
        private async Task<CloudStorageFile> UploadBigFile(string filePath, string parentFolder, CancellationToken ct)
        {
            FileInfo fileInfo = new FileInfo(filePath);
            // file size must larger than 1 chunk size
            long fileSize = fileInfo.Length;
            if (fileSize <= CHUNK_SIZE)
                return null;

            using (FileStream fileStream = new FileStream(filePath, FileMode.Open))
            {
                byte[] buffer = new byte[CHUNK_SIZE];
                long uploaded = 0;
                string sessionId;

                // read first chunk from file
                int length = await fileStream.ReadAsync(buffer, 0, CHUNK_SIZE, ct);

                // upload first chunk
                using (MemoryStream firstStream = new MemoryStream(buffer, 0, length, false))
                {
                    var startResult = await dropboxClient.Files.UploadSessionStartAsync(body: firstStream);
                    sessionId = startResult.SessionId;
                    firstStream.Close();
                }

                // update progress bar of first chunk
                OnProgressChanged(length);
                uploaded += length;

                // upload middle chunks
                while (true)
                {
                    // check cancel
                    if (ct.IsCancellationRequested)
                        throw new TaskCanceledException("Upload cancelled.");

                    // read next chunk from file
                    length = await fileStream.ReadAsync(buffer, 0, CHUNK_SIZE, ct);
                    if (length <= 0)
                        break;

                    // if we reach last chung, don't upload now!
                    if (uploaded + length >= fileSize)
                        break;

                    // upload each chunk
                    using (MemoryStream tempStream = new MemoryStream(buffer, 0, length, false))
                    {
                        UploadSessionCursor cursor = new UploadSessionCursor(sessionId, (ulong)uploaded);
                        await dropboxClient.Files.UploadSessionAppendV2Async(cursor, body: tempStream);
                        tempStream.Close();
                    }

                    // Update progress bar
                    OnProgressChanged(length);
                    uploaded += length;
                }

                // ending upload session
                UploadSessionCursor endCursor = new UploadSessionCursor(sessionId, (ulong)uploaded);
                // prepare file info
                if (!parentFolder.EndsWith("/"))
                    parentFolder += "/";
                parentFolder += Path.GetFileName(filePath);
                CommitInfo info = new CommitInfo(parentFolder, autorename: true);
                UploadSessionFinishArg finishArg = new UploadSessionFinishArg(endCursor, info);

                // do last session
                FileMetadata finishResult;
                using (MemoryStream tempStream = new MemoryStream(buffer, 0, length, false))
                {
                    finishResult = await dropboxClient.Files.UploadSessionFinishAsync(finishArg, tempStream);
                    Console.WriteLine("Finished large file: " + finishResult.PathDisplay);
                }

                // update last progress
                OnProgressChanged(length);

                fileStream.Close();

                // return new file info
                CloudStorageFile cloudFile = new CloudStorageFile()
                {
                    Id = finishResult.Id,
                    ModifiedTime = finishResult.ServerModified,
                    Name = finishResult.Name,
                    Size = (long) finishResult.Size
                };
                return cloudFile;
            }
        }

        private void OnProgressChanged(long bytesSent)
        {
            var args = new CloudStorageProgressArgs { BytesSent = bytesSent };
            ProgressChanged?.Invoke(null, args);
        }

        public async Task<CloudStorageResult> AuthenticateFromUri(string state, string uri)
        {
            CloudStorageResult result = new CloudStorageResult();
            try
            {
                bool success = oAuthWrapper.ProcessUri(state, uri);
                if (success)
                {
                    result.Status = Status.Success;
                    LastAccessToken = oAuthWrapper.AccessToken;
                    LastRefreshToken = oAuthWrapper.RefreshToken;
                    SaveAccessTokenDelegate?.Invoke(LastAccessToken);
                    SaveRefreshTokenDelegate?.Invoke(LastRefreshToken);
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
            oAuthWrapper = new DropBoxOauthClient(ApiKey, RedirectUrl);
            string state = oAuthWrapper.GetTokenToUri();
            return state;
        }
    }
}
