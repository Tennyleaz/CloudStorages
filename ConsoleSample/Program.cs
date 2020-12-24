using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CloudStorages;
using CloudStorages.DropBox;

namespace ConsoleSample
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                Task.Run(Test).Wait();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            Console.WriteLine("Press any key to end.");
            Console.ReadKey();
        }

        private static async Task Test()
        {
            string apiKey;
            string redirectUrl = @"http://localhost:51001/";
            try
            {
                apiKey = File.ReadAllText(@"D:\Test Dir\CloudStorages\dbkey.txt");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Cannot read api key: " + ex.Message);
                return;
            }

            ICloudStorageClient dropboxClent = new DropBoxStorage(apiKey, redirectUrl);
            dropboxClent.SaveAccessTokenDelegate = SaveAccessToken;
            dropboxClent.SaveRefreshTokenDelegate = SaveRefreshToken;
            dropboxClent.LoadAccessTokenDelegate = LoadAccessToken;
            dropboxClent.LoadRefreshTokenDelegate = LoadRefresToken;
            
            var (result, isNeedLogin) = await dropboxClent.InitAsync();
            dropboxClent.StopListen();
            if (!result.Success)
            {
                Console.WriteLine("Initial failed, reason=" + result.Message);
                return;
            }
            else
                Console.WriteLine("Initial success, isNeedLogin=" + isNeedLogin);

            CloudStorageAccountInfo accountInfo;
            if (isNeedLogin)
                (result, accountInfo) = await dropboxClent.LoginAsync();
            else
                (result, accountInfo) = await dropboxClent.GetAccountInfoAsync();

            if (!result.Success)
            {
                Console.WriteLine("Login failed, reason=" + result.Message);
                return;
            }
            
            Console.WriteLine("Login success, account=" + accountInfo.userEmail);
            IFormatProvider formatter = new CloudStorages.Utility.FileSizeFormatProvider();
            Console.WriteLine(string.Format(formatter, "Account space: {0:fs} / {1:fs}", accountInfo.usedSpace, accountInfo.totalSpace));

            string folderId, folderName = "/test/folder1";
            (result, folderId) = await dropboxClent.CreateFolderAsync(folderName);
            if (!result.Success)
            {
                Console.WriteLine("Create folder failed, reason=" + result.Message);
                return;
            }
            Console.WriteLine($"Created folder {folderName}, id={folderId}");

            string localFile = "test.txt";
            File.WriteAllText(localFile, "I am a test file.");
            CancellationTokenSource cts = new CancellationTokenSource();
            result = await dropboxClent.UploadFileToFolderByPathAsync(localFile, cts.Token, folderName);
            if (!result.Success)
            {
                Console.WriteLine("Upload file failed, reason=" + result.Message);
                return;
            }
            Console.WriteLine($"Uploaded file {localFile}");
            File.Delete(localFile);

            string cloudFile = folderName + "/" + localFile;
            result = await dropboxClent.DownloadFileByPathAsync(cloudFile, localFile, cts.Token);
            if (!result.Success)
            {
                Console.WriteLine("Download file failed, reason=" + result.Message);
                return;
            }
            Console.WriteLine($"Downloaded file {localFile}");

            result = await dropboxClent.DeleteFileByPathAsync(cloudFile);
            if (!result.Success)
            {
                Console.WriteLine("Delete file failed, reason=" + result.Message);
                return;
            }
            Console.WriteLine($"Deleted file {localFile}");
        }

        #region Token save/load
        private static string LoadAccessToken()
        {
            try
            {
                string path = @"D:\Test Dir\CloudStorages\dbaccess.txt";
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return null;
            }
        }

        private static string LoadRefresToken()
        {
            try
            {
                string path = @"D:\Test Dir\CloudStorages\dbrefresh.txt";
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return null;
            }
        }

        private static void SaveAccessToken(string token)
        {
            try
            {
                string path = @"D:\Test Dir\CloudStorages\dbaccess.txt";
                File.WriteAllText(path, token);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private static void SaveRefreshToken(string token)
        {
            try
            {
                string path = @"D:\Test Dir\CloudStorages\dbrefresh.txt";
                File.WriteAllText(path, token);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
        #endregion
    }
}
