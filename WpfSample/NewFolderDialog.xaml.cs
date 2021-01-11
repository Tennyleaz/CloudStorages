using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace WpfSample
{
    /// <summary>
    /// NewFolderDialog.xaml 的互動邏輯
    /// </summary>
    public partial class NewFolderDialog : Window
    {
        public NewFolderDialog()
        {
            InitializeComponent();
            btnOk.Click += BtnOk_Click;
            btnCancel.Click += BtnCancel_Click;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(tbFolderName.Text))
            {
                tbFolderName.BorderBrush = Brushes.Red;
                return;
            }

            DialogResult = true;
            Close();
        }
    }
}
