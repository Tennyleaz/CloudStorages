using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace WpfSample
{
    /// <summary>
    /// App.xaml 的互動邏輯
    /// </summary>
    public partial class App : Application
    {
    }

    /// <summary>
    /// Midified main function for WPF program.
    /// see:
    /// https://www.codeproject.com/Articles/1224031/Passing-Parameters-to-a-Running-Application-in-WPF
    /// </summary>
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Process proc = Process.GetCurrentProcess();
            string processName = proc.ProcessName.Replace(".vshost", "");
            Process runningProcess = Process.GetProcesses()
                .FirstOrDefault(x => (x.ProcessName == processName ||
                                      x.ProcessName == proc.ProcessName ||
                                      x.ProcessName == proc.ProcessName + ".vshost") && x.Id != proc.Id);

            if (runningProcess == null)
            {
                var app = new App();
                app.InitializeComponent();
                var window = new MainWindow();
                //MainWindow.HandleParameter(args);
                app.Run(window);
                if (args.Length > 0)
                    MainWindow.HandleParameter(args);
                return; // In this case we just proceed on loading the program
            }
            else
            {
                if (args.Length > 0)
                    UnsafeNative.SendMessage(runningProcess.MainWindowHandle, string.Join(" ", args));
            }
        }
    }
}
