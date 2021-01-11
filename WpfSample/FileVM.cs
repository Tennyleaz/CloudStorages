using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CloudStorages;

namespace WpfSample
{
    public class FileVM : INotifyPropertyChanged
    {
        public ObservableCollection<FileVM> Members { get; } = new ObservableCollection<FileVM>();
        public event PropertyChangedEventHandler PropertyChanged;
        private readonly CloudStorageFile file;
        private static readonly IFormatProvider formatProvider = new CloudStorages.Utility.FileSizeFormatProvider();

        public FileVM(CloudStorageFile file)
        {
            this.file = file;
        }

        public FileVM()
        {
            IsFake = true;
            file = new CloudStorageFile();
        }

        public void AddRange(IEnumerable<FileVM> items)
        {
            foreach (FileVM i in items)
            {
                i.Parent = this;
                Members.Add(i);
            }
        }

        public void RemoveFake()
        {
            for (int i = Members.Count - 1; i >= 0; i--)
            {
                if (Members[i].IsFake)
                {
                    Members.RemoveAt(i);
                }
            }
        }

        public FileVM Parent { get; set; }

        public string Name => file.Name;

        public string Id => file.Id;

        public bool IsFolder => file.IsFolder;

        public bool IsFake { get; }

        public string Size
        {
            get
            {
                if (file.IsFolder)
                    return null;
                return string.Format(formatProvider, "{0:fs}", file.Size);
            }
        }

        public string ModifiedTime 
        {
            get
            {
                if (file.IsFolder)
                    return null;
                return file.ModifiedTime.ToString("yyyy-MM-dd HH:mm:ss");
            }
        }


        private bool isExpand;
        public bool IsExpand
        {
            get => isExpand;
            set
            {
                isExpand = value;
                OnPropertyChanged();
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
