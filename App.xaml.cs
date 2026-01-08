using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.IO;

namespace EasyShare
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
            {
                LogException((Exception)ev.ExceptionObject, "AppDomain Unhandled");
            };

            DispatcherUnhandledException += (s, ev) =>
            {
                LogException(ev.Exception, "Dispatcher Unhandled");
                ev.Handled = true;
            };

            base.OnStartup(e);
        }

        private void LogException(Exception ex, string type)
        {
            string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crashlog.txt");
            string message = $"[{DateTime.Now}] {type} Exception: {ex.Message}\n{ex.StackTrace}\n\n";
            System.IO.File.AppendAllText(logPath, message);
            MessageBox.Show($"Une erreur critique est survenue. Détails dans crashlog.txt\n\nErreur: {ex.Message}", "EasyShare Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
