using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CloudStorages;
using CloudStorages.DropBox;
using CloudStorages.OneDrive;
using Microsoft.Win32;

namespace WpfSample
{
    /// <summary>
    /// MainWindow.xaml 的互動邏輯
    /// </summary>
    public partial class MainWindow : Window
    {
        public static IntPtr WindowHandle { get; private set; }
        public ObservableCollection<FileVM> CloudFiles = new ObservableCollection<FileVM>();
        private IUriHandler cloudClient;
        private static bool loaded, closed;
        private const string redirectUrl = "cloudstoragewpf://test/";
        private string lastState;
        private static string tokenName;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadShieldImage();
            WindowHandle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(WindowHandle)?.AddHook(HandleMessages);
            loaded = true;

            treeView.ItemsSource = CloudFiles;

            // load other functions
            foreach (var method in typeof(ICloudStorageClient).GetMethods())
            {
                if (method.IsSpecialName)  // MethodInfo.IsSpecialName is set to true for properties and events accessors
                    continue;
                if (method.Name == nameof(ICloudStorageClient.InitAsync))
                    continue;
                Console.WriteLine(method.Name);
            }
        }

        private void MainWindow_OnClosing(object sender, CancelEventArgs e)
        {
            closed = true;
            // close existing connections if any
            cloudClient?.StopListen();
        }

        internal async void HandleParameterNonStatic(string[] args)
        {
            tbLog.Text += "HandleParameter:\n";
            foreach (string param in args)
            {
                tbLog.Text += param + "\n";
                if (param.StartsWith(redirectUrl))
                {
                    if (cloudClient == null)
                    {
                        tbLog.Text += "dropBoxClient is not initialized.";
                        return;
                    }

                    try
                    {
                        var result = await cloudClient.AuthenticateFromUri(lastState, param);
                        if (result.Status == Status.Success)
                        {
                            await GetUserAsync();
                            btnLogout.IsEnabled = true;
                        }
                        else
                        {
                            tbLog.Text += "AuthenticateFromUri() failed.\nStatus=" + result.Status + "\nMessage=" + result.Message;
                        }
                    }
                    catch (Exception e)
                    {
                        tbLog.Text += e.ToString() + "\n";
                    }
                }
            }
        }

        internal static void HandleParameter(string[] args)
        {
            if (!loaded || closed)
                return;
            if (args == null || args.Length == 0)
                return;
            // Do stuff with the args
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.HandleParameterNonStatic(args);
            }
        }

        private static IntPtr HandleMessages(IntPtr handle, int message, IntPtr wParameter, IntPtr lParameter, ref bool handled)
        {
            var data = UnsafeNative.GetMessage(message, lParameter);

            if (data != null)
            {
                if (Application.Current.MainWindow == null)
                    return IntPtr.Zero;

                if (Application.Current.MainWindow.WindowState == WindowState.Minimized)
                    Application.Current.MainWindow.WindowState = WindowState.Normal;

                UnsafeNative.SetForegroundWindow(new WindowInteropHelper
                    (Application.Current.MainWindow).Handle);

                var args = data.Split(' ');
                HandleParameter(args);
                handled = true;
            }

            return IntPtr.Zero;
        }

        #region Load Admin Sheld Image
        private void LoadShieldImage()
        {
            var image = UnsafeNative.LoadImage(IntPtr.Zero, "#106", 1,
                System.Windows.Forms.SystemInformation.SmallIconSize.Width, System.Windows.Forms.SystemInformation.SmallIconSize.Height, 0);
            var imageSource = Imaging.CreateBitmapSourceFromHIcon(image, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            shieldImage1.Source = imageSource;
            shieldImage2.Source = imageSource;
        }
        #endregion

        private void BtnRegisterUri_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                UriRegistry.Register();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnUnRegisterUri_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                UriRegistry.Unregister();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnLogin_OnClick(object sender, RoutedEventArgs e)
        {
            btnLogin.IsEnabled = false;
            try
            {
                await LoginAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "", MessageBoxButton.OK, MessageBoxImage.Error);
                btnLogin.IsEnabled = true;
            }
        }

        private async Task LoginAsync()
        {
            // close existing connections if any
            cloudClient?.StopListen();

            // create dropbox client
            string apiKey, apiSecret;
            
            switch (comboCloud.SelectedIndex)
            {
                case 0:
                    try
                    {
                        apiKey = File.ReadAllText(@"D:\Test Dir\CloudStorages\dbkey.txt");
                    }
                    catch (Exception ex)
                    {
                        tbLog.Text += ex + "\n";
                        return;
                    }
                    cloudClient = new DropBoxStorage(apiKey, redirectUrl);
                    break;
                case 1:
                    try
                    {
                        apiKey = File.ReadAllText(@"D:\Test Dir\CloudStorages\odkey.txt");
                    }
                    catch (Exception ex)
                    {
                        tbLog.Text += ex + "\n";
                        return;
                    }
                    cloudClient = new OneDriveStorage(apiKey, null, redirectUrl);  // onedrive public client cannot use api secret.
                    break;
                default:
                    MessageBox.Show("Please select a cloud type.");
                    btnLogin.IsEnabled = true;
                    return;
            }

            tokenName = cloudClient.GetType().Name;
            cloudClient.SaveAccessTokenDelegate = SaveAccessToken;
            cloudClient.SaveRefreshTokenDelegate = SaveRefreshToken;
            cloudClient.LoadAccessTokenDelegate = LoadAccessToken;
            cloudClient.LoadRefreshTokenDelegate = LoadRefresToken;
            cloudClient.ProgressChanged += CloudClientProgressChanged;

            var result = await cloudClient.InitAsync();

            if (result.Status == Status.NeedAuthenticate)
            {
                tbLog.Text += "InitAsync() need login.\n" + result.Message + Environment.NewLine;
                lastState = cloudClient.LoginToUri();
                // wait for uri redirect
            }
            else if (result.Status == Status.Success)
            {
                await GetUserAsync();
                btnLogout.IsEnabled = true;
            }
            else
            {
                tbLog.Text += "Login failed. " + result.Message + "\n";
                btnLogin.IsEnabled = true;
            }
        }

        private void CloudClientProgressChanged(object sender, CloudStorageProgressArgs e)
        {
            tbLog.Text += "Progress: " + e.BytesSent + Environment.NewLine;
        }

        private async Task GetUserAsync()
        {
            var (result, accountInfo) = await cloudClient.GetAccountInfoAsync();

            lbUserName.Content = accountInfo.userName;
            lbUserEmail.Content = accountInfo.userEmail;

            IFormatProvider formatter = new CloudStorages.Utility.FileSizeFormatProvider();
            lbUsage.Content = string.Format(formatter, "Usage: {0:fs}/{1:fs}", accountInfo.usedSpace, accountInfo.totalSpace);
            progressUsage.Maximum = accountInfo.totalSpace;
            progressUsage.Value = accountInfo.usedSpace;
        }

        #region Token save/load
        private static string LoadAccessToken()
        {
            try
            {
                string path = $@"D:\Test Dir\CloudStorages\{tokenName}_access.txt";
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
                string path = $@"D:\Test Dir\CloudStorages\{tokenName}_refresh.txt";
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
                string path = $@"D:\Test Dir\CloudStorages\{tokenName}_access.txt";
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
                string path = $@"D:\Test Dir\CloudStorages\{tokenName}_refresh.txt";
                File.WriteAllText(path, token);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
        #endregion

        private void BtnLogout_OnClick(object sender, RoutedEventArgs e)
        {
            SaveAccessToken("");
            SaveRefreshToken("");
            btnLogout.IsEnabled = false;
            btnLogin.IsEnabled = true;
            lbUserName.Content = "User Name";
            lbUserEmail.Content = "User Email";
            lbUsage.Content = "Usage: 0/0";
            progressUsage.Value = 0;
            cloudClient = null;
        }

        private async void BtnList_OnClick(object sender, RoutedEventArgs e)
        {
            btnList.IsEnabled = false;
            this.Cursor = Cursors.Wait;

            try
            {
                string rootId = await cloudClient.GetRootFolderIdAsync();
                CloudStorageFile root = new CloudStorageFile
                {
                    Name = "root",
                    Id = rootId,
                    IsFolder = true
                };
                FileVM rootFolder = new FileVM(root);
                CloudFiles.Clear();
                CloudFiles.Add(rootFolder);
                rootFolder.AddRange(await ParseTreeAsync(rootId));

                // expand root folder
                rootFolder.IsExpand = true;
            }
            catch (Exception ex)
            {
                tbLog.Text += ex + Environment.NewLine;
            }

            btnList.IsEnabled = true;
            this.Cursor = Cursors.Arrow;
        }

        private async Task<ObservableCollection<FileVM>> ParseTreeAsync(string parentId)
        {
            var files = await cloudClient.GetFileInfosInPathAsync(parentId);

            ObservableCollection <FileVM> fileVms = new ObservableCollection<FileVM>();
            foreach (var file in files)
            {
                FileVM fileVm = new FileVM(file);

                // parse items in folder
                if (file.IsFolder)
                {
                    // add a fake item
                    fileVm.Members.Add(new FileVM());
                }

                fileVms.Add(fileVm);
            }

            return fileVms;
        }

        private void TryShowError(CloudStorageResult result)
        {
            if (result.Status == Status.Success)
            {
                //MessageBox.Show(this, "Success");
            }
            else
            {
                MessageBox.Show(this, $"Status:{result.Status}, \nMessage:{result.Message}", "", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnDownload_OnClick(object sender, RoutedEventArgs e)
        {
            if (treeView.SelectedItem is FileVM fileVm)
            {
                if (fileVm.IsFolder)
                {
                    MessageBox.Show(this, "Selected item is a folder.");
                    return;
                }

                btnDownload.IsEnabled = false;

                SaveFileDialog saveFileDialog = new SaveFileDialog();
                saveFileDialog.OverwritePrompt = true;
                saveFileDialog.FileName = fileVm.Name;
                if (saveFileDialog.ShowDialog() == true)
                {
                    CloudStorageResult result = await cloudClient.DownloadFileByIdAsync(fileVm.Id, saveFileDialog.FileName, new CancellationToken());
                    TryShowError(result);
                }
                btnDownload.IsEnabled = true;
            }
            else
            {
                MessageBox.Show(this, "Please select an item.");
            }
        }

        private async void BtnUpload_OnClick(object sender, RoutedEventArgs e)
        {
            FileVM selectedFolder;
            if (treeView.SelectedItem is FileVM fileVm && fileVm.IsFolder)
            {
                selectedFolder = fileVm;
            }
            else
            {
                MessageBox.Show(this, "Please select a folder.");
                return;
            }

            // pick file
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Multiselect = false;
            openFileDialog.CheckFileExists = true;
            if (openFileDialog.ShowDialog() != true)
                return;

            string message = $"Upload file '{Path.GetFileName(openFileDialog.FileName)}' to folder '{selectedFolder.Name}'?";
            if (MessageBox.Show(this, message, "", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;

            // upload
            btnUpload.IsEnabled = false;
            this.Cursor = Cursors.Wait;

            var (result, cloudFile) = await cloudClient.UploadFileToFolderByIdAsync(openFileDialog.FileName, selectedFolder.Id, new CancellationToken());

            // add result to tree
            TryShowError(result);
            if (result.Status == Status.Success)
            {
                FileVM newFile = new FileVM(cloudFile);
                newFile.Parent = selectedFolder;
                selectedFolder.Members.Add(newFile);
            }

            btnUpload.IsEnabled = true;
            this.Cursor = Cursors.Arrow;
        }

        private async void BtnCreate_OnClick(object sender, RoutedEventArgs e)
        {
            FileVM selectedFolder;
            if (treeView.SelectedItem is FileVM fileVm && fileVm.IsFolder)
            {
                selectedFolder = fileVm;
            }
            else
            {
                MessageBox.Show(this, "Please select a folder.");
                return;
            }

            NewFolderDialog dialog = new NewFolderDialog();
            dialog.Owner = this;
            if (dialog.ShowDialog() != true)
                return;

            btnCreate.IsEnabled = false;
            this.Cursor = Cursors.Wait;

            string newFolderName = dialog.tbFolderName.Text;
            var (result, newFolderId) = await cloudClient.CreateFolderAsync(selectedFolder.Id, newFolderName);

            // add result to tree
            TryShowError(result);
            if (result.Status == Status.Success)
            {
                CloudStorageFile newFolder = new CloudStorageFile
                {
                    Name = newFolderName,
                    Id = newFolderId,
                    IsFolder = true
                };
                FileVM newVm = new FileVM(newFolder);
                newVm.Parent = selectedFolder;
                selectedFolder.Members.Add(newVm);
            }

            btnCreate.IsEnabled = true;
            this.Cursor = Cursors.Arrow;
        }

        private async void BtnDelete_OnClick(object sender, RoutedEventArgs e)
        {
            if (treeView.SelectedItem is FileVM fileVm)
            {
                btnDelete.IsEnabled = false;
                string message;
                if (fileVm.IsFolder)
                    message = "Delete folder '" + fileVm.Name + "' and all the contents?";
                else
                    message = "Delete file '" + fileVm.Name + "' ?";
                if (MessageBox.Show(this, message, "", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    CloudStorageResult result = await cloudClient.DeleteFileByIdAsync(fileVm.Id);
                    TryShowError(result);
                    if (result.Status == Status.Success)
                    {
                        if (fileVm.Parent != null)
                            fileVm.Parent.Members.Remove(fileVm);
                        else
                            CloudFiles.Remove(fileVm);
                    }
                }
                btnDelete.IsEnabled = true;
            }
            else
            {
                MessageBox.Show(this, "Please select an item.");
            }
        }

        private ItemsControl GetSelectedTreeViewItemParent(TreeViewItem item)
        {
            DependencyObject parent = VisualTreeHelper.GetParent(item);
            if (parent == null)
                return null;
            while (!(parent is TreeViewItem || parent is TreeView))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
            return parent as ItemsControl;
        }

        private async void TreeView_OnExpanded(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is TreeViewItem tvi && tvi.DataContext is FileVM viewModel)
            {
                if (!viewModel.IsFolder)
                    return;
                if (viewModel.Members == null || viewModel.Members.Count == 0)  // 沒有成員的話就當成是已經展開過了
                    return;
                if (viewModel.Members.First().IsFake)  // 第一個成員是 fake 才是沒有展開過，清空全部
                {
                    this.Cursor = Cursors.Wait;

                    //viewModel.RemoveFake();
                    viewModel.Members.Clear();
                    viewModel.AddRange(await ParseTreeAsync(viewModel.Id));

                    this.Cursor = Cursors.Arrow;
                }
            }
        }
    }
}
