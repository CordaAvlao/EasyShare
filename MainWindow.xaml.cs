using System;
using System.Windows;
using System.IO;
using System.Windows.Forms;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyShare
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Log(string message)
        {
            StatusLog.Dispatcher.Invoke(() => {
                StatusLog.AppendText($"\n[{DateTime.Now:HH:mm:ss}] {message}");
                StatusLog.ScrollToEnd();
            });
        }

        private async void ShareTemp_Click(object sender, RoutedEventArgs e)
        {
            string user = UserField.Text;
            string pass = PassField.Text;
            bool isHidden = HiddenCheck.IsChecked ?? false;

            Log($"Préparation du partage pour : {ShareManager.TempFolderPath} (Utilisateur: {user})");
            try
            {
                if (!Directory.Exists(ShareManager.TempFolderPath))
                {
                    Directory.CreateDirectory(ShareManager.TempFolderPath);
                }

                await Task.Run(() => {
                    ShareManager.CreateUser(user, pass);
                    ShareManager.SetPermissions(ShareManager.TempFolderPath, user);
                    ShareManager.ShareFolder(ShareManager.TempFolderPath, ShareManager.TempFolderName, user, isHidden);
                });

                Log($"SUCCÈS : Dossier PartageTemp partagé.");
            }
            catch (Exception ex)
            {
                Log($"ERREUR : {ex.Message}");
            }
        }

        private async void BrowseShare_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string path = dialog.SelectedPath;
                string shareName = new DirectoryInfo(path).Name;
                string user = UserField.Text;
                string pass = PassField.Text;
                bool isHidden = HiddenCheck.IsChecked ?? false;

                Log($"Préparation du partage pour : {path} (Utilisateur: {user})");
                try
                {
                    await Task.Run(() => {
                        ShareManager.CreateUser(user, pass);
                        ShareManager.SetPermissions(path, user);
                        ShareManager.ShareFolder(path, shareName, user, isHidden);
                    });
                    Log($"SUCCÈS : Dossier '{shareName}' partagé.");
                }
                catch (Exception ex)
                {
                    Log($"ERREUR : {ex.Message}");
                }
            }
        }

        private void CleanupTab_Selected(object sender, RoutedEventArgs e) => RefreshResources_Click(null, null);

        private async void RefreshResources_Click(object sender, RoutedEventArgs e)
        {
            ResourceList.Children.Clear();
            ResourceList.Children.Add(new TextBlock { Text = "Chargement...", Foreground = Brushes.Gray });

            try
            {
                var shares = await Task.Run(() => ShareManager.GetEasyShareShares());
                var users = await Task.Run(() => ShareManager.GetEasyShareUsers());

                ResourceList.Children.Clear();
                
                if (shares.Count > 0)
                {
                    ResourceList.Children.Add(new TextBlock { Text = "PARTAGES SMB", Foreground = Brushes.Aqua, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,5) });
                    foreach (var s in shares)
                    {
                        var cb = new System.Windows.Controls.CheckBox { Content = s, Tag = "SHARE", Foreground = Brushes.White, Margin = new Thickness(5,2,0,2) };
                        ResourceList.Children.Add(cb);
                    }
                }

                if (users.Count > 0)
                {
                    ResourceList.Children.Add(new TextBlock { Text = "COMPTES UTILISATEURS", Foreground = Brushes.Aqua, FontWeight = FontWeights.Bold, Margin = new Thickness(0,10,0,5) });
                    foreach (var u in users)
                    {
                        var cb = new System.Windows.Controls.CheckBox { Content = u, Tag = "USER", Foreground = Brushes.White, Margin = new Thickness(5,2,0,2) };
                        ResourceList.Children.Add(cb);
                    }
                }

                if (shares.Count == 0 && users.Count == 0)
                {
                    ResourceList.Children.Add(new TextBlock { Text = "Aucun partage ou utilisateur créé trouvé.", Foreground = Brushes.Gray, FontStyle = FontStyles.Italic });
                }
            }
            catch (Exception ex)
            {
                Log($"Erreur lors du rafraîchissement : {ex.Message}");
            }
        }

        private async void SelectiveCleanup_Click(object sender, RoutedEventArgs e)
        {
            // Collect data on UI Thread
            var itemsToRemove = ResourceList.Children.OfType<System.Windows.Controls.CheckBox>()
                .Where(cb => cb.IsChecked == true)
                .Select(cb => new { Name = cb.Content.ToString(), Type = cb.Tag.ToString() })
                .ToList();

            if (itemsToRemove.Count == 0) return;

            Log($"Nettoyage de {itemsToRemove.Count} éléments selectionnés...");
            try
            {
                await Task.Run(() => {
                    foreach (var item in itemsToRemove)
                    {
                        if (item.Type == "SHARE") ShareManager.RemoveShare(item.Name);
                        else if (item.Type == "USER") ShareManager.RemoveUser(item.Name);
                    }
                });
                Log("Nettoyage sélectif terminé.");
                RefreshResources_Click(null, null);
            }
            catch (Exception ex)
            {
                Log($"ERREUR lors du nettoyage : {ex.Message}");
            }
        }

        private async void Cleanup_Click(object sender, RoutedEventArgs e)
        {
            string user = UserField.Text;
            Log("Lancement du nettoyage complet...");
            try
            {
                await Task.Run(() => ShareManager.Cleanup(user));
                Log("SUCCÈS : Nettoyage complet terminé.");
                RefreshResources_Click(null, null);
            }
            catch (Exception ex)
            {
                Log($"ERREUR : {ex.Message}");
            }
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Log($"Erreur lors de l'ouverture du lien : {ex.Message}");
            }
        }
    }
}
