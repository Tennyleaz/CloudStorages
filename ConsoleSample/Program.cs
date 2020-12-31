using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CloudStorages;
using CloudStorages.DropBox;
using CloudStorages.GoogleDrive;
using CloudStorages.OneDrive;

namespace ConsoleSample
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                // create dropbox client
                string apiKey;
                string redirectUrl = @"http://localhost:51001/";
                try
                {
                    apiKey = File.ReadAllText(@"D:\Test Dir\CloudStorages\dbkey.txt");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Cannot read api key: " + ex.Message);
                    throw ex;
                }

                ICloudStorageClient dropBoxClient = new DropBoxStorage(apiKey, redirectUrl);

                Task.Run(() => Test(dropBoxClient)).Wait();
                Console.Write("Finish testing dropbox.\n\n");

                // create google drive client
                string appName = "Test App";
                string apiSecret;
                try
                {
                    apiKey = File.ReadAllText(@"D:\Test Dir\CloudStorages\gdkey.txt");
                    apiSecret = File.ReadAllText(@"D:\Test Dir\CloudStorages\gdsecret.txt");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Cannot read api key/secret: " + ex.Message);
                    throw ex;
                }

                ICloudStorageClient googleClient = new GoogleDriveStorage(apiKey, apiSecret, appName);

                Task.Run(() => Test(googleClient)).Wait();
                Console.Write("Finish testing google drive.\n\n");

                // create onedrive client
                try
                {
                    apiKey = File.ReadAllText(@"D:\Test Dir\CloudStorages\odkey.txt");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Cannot read api key: " + ex.Message);
                    throw ex;
                }
                ICloudStorageClient onedriveClient = new OneDriveStorage(apiKey);

                Task.Run(() => Test(onedriveClient)).Wait();
                Console.Write("Finish testing onedrive.\n\n");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            Console.WriteLine("Press any key to end.");
            Console.ReadKey();
        }

        private static async Task Test(ICloudStorageClient client)
        {
            TokenManager tokenManager = new TokenManager(client.GetType().Name);
            client.SaveAccessTokenDelegate = tokenManager.SaveAccessToken;
            client.SaveRefreshTokenDelegate = tokenManager.SaveRefreshToken;
            client.LoadAccessTokenDelegate = tokenManager.LoadAccessToken;
            client.LoadRefreshTokenDelegate = tokenManager.LoadRefresToken;

            var result = await client.InitAsync();
            if (result.Status != Status.Success)
            {
                Console.WriteLine("Initial failed, status=" + result.Status + ", reason=" + result.Message);
            }
            
            CloudStorageAccountInfo accountInfo;
            if (result.Status == Status.NeedAuthenticate)
            {
                (result, accountInfo) = await client.LoginAsync();
                client.StopListen();
            }
            else
                (result, accountInfo) = await client.GetAccountInfoAsync();

            if (result.Status != Status.Success)
            {
                Console.WriteLine("Login failed, reason=" + result.Message);
                return;
            }
            
            Console.WriteLine("Login success, account=" + accountInfo.userEmail + ", username=" + accountInfo.userName);
            IFormatProvider formatter = new CloudStorages.Utility.FileSizeFormatProvider();
            Console.WriteLine(string.Format(formatter, "Account space: {0:fs} / {1:fs}", accountInfo.usedSpace, accountInfo.totalSpace));

            string rootId = await client.GetRootFolderIdAsync();

            string folderId, folderName = "test";
            (result, folderId) = await client.CreateFolderAsync(rootId, folderName);
            if (result.Status != Status.Success)
            {
                Console.WriteLine("Create folder failed, reason=" + result.Message);
                return;
            }
            Console.WriteLine($"Created folder {folderName}, id={folderId}");

            string localFile = "test.txt";
            File.WriteAllText(localFile, "I am a test file.");
            CancellationTokenSource cts = new CancellationTokenSource();
            CloudStorageFile cloudFile;
            (result, cloudFile) = await client.UploadFileToFolderByIdAsync(localFile, folderId, cts.Token);
            if (result.Status != Status.Success)
            {
                Console.WriteLine("Upload file failed, reason=" + result.Message);
                return;
            }
            Console.WriteLine($"Uploaded file {localFile}");
            File.Delete(localFile);

            result = await client.DownloadFileByIdAsync(cloudFile.Id, localFile, cts.Token);
            if (result.Status != Status.Success)
            {
                Console.WriteLine("Download file failed, reason=" + result.Message);
                return;
            }
            Console.WriteLine($"Downloaded file {localFile}");

            result = await client.DeleteFileByIdAsync(cloudFile.Id);
            if (result.Status != Status.Success)
            {
                Console.WriteLine("Delete file failed, reason=" + result.Message);
                return;
            }
            Console.WriteLine($"Deleted file '{cloudFile.Name}'");

            // test a larger file
            const string testPic = @"D:\Test Dir\CloudStorages\surprised pikachu.png";
            (result, cloudFile) = await client.UploadFileToFolderByIdAsync(testPic, folderId, cts.Token);
            if (result.Status != Status.Success)
            {
                Console.WriteLine("Upload large file failed, reason=" + result.Message);
                return;
            }
            Console.WriteLine($"Uploaded large file '{testPic}'");

            const string testPic2 = @"D:\Test Dir\CloudStorages\surprised pikachu 2.png";
            result = await client.DownloadFileByIdAsync(cloudFile.Id, testPic2, cts.Token);
            if (result.Status != Status.Success)
            {
                Console.WriteLine("Download large file failed, reason=" + result.Message);
                return;
            }
            Console.WriteLine($"Downloaded large file '{testPic2}'");

            result = await client.DeleteFileByIdAsync(cloudFile.Id);
            if (result.Status != Status.Success)
            {
                Console.WriteLine("Delete file failed, reason=" + result.Message);
                return;
            }
            Console.WriteLine($"Deleted file '{cloudFile.Name}'");
            File.Delete(testPic2);
        }
    }
}
