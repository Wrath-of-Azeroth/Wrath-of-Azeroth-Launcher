using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;

namespace WrathOfAzerothLauncher
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LauncherForm());
        }
    }

    internal sealed class LauncherForm : Form
    {
        private const string DefaultManifestUrl = "https://github.com/Wrath-of-Azeroth/Wrath-of-Azeroth-Launcher/raw/refs/heads/main/launcher-feed/manifest.json";
        private const string DefaultRealmListEndpoint = "wrath-of-azeroth.playit.plus:6946";
        private const int ArtworkWidth = 1672;
        private const int ArtworkHeight = 941;
        private const int WmNcLButtonDown = 0x00A1;
        private const int HtCaption = 0x0002;
        private static readonly HttpClient Http = CreateHttpClient();
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly TextBox _clientPath = new TextBox();
        private readonly TextBox _manifestUrl = new TextBox();
        private readonly string _settingsPath;
        private readonly Image _artwork;
        private readonly Timer _realmTimer = new Timer();
        private bool _realmCheckInProgress;
        private bool _realmOnline;
        private bool _busy;
        private string _statusMessage = String.Empty;
        private string _activeRealmEndpoint = DefaultRealmListEndpoint;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        public LauncherForm()
        {
            Text = "Wrath of Azeroth Launcher";
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(ArtworkWidth, ArtworkHeight);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.Black;
            DoubleBuffered = true;

            string artworkPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LauncherArtwork.png");
            if (!File.Exists(artworkPath))
                throw new FileNotFoundException("LauncherArtwork.png must be beside the launcher executable.", artworkPath);
            _artwork = Image.FromFile(artworkPath);
            if (_artwork.Width != ArtworkWidth || _artwork.Height != ArtworkHeight)
                throw new InvalidOperationException("Launcher artwork must remain at its original 1672x941 resolution.");

            string dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WrathOfAzerothLauncher");
            Directory.CreateDirectory(dataDirectory);
            _settingsPath = Path.Combine(dataDirectory, "settings.json");
            _realmTimer.Interval = 15000;
            _realmTimer.Tick += async delegate { await CheckRealmStatusAsync(); };
            Shown += async delegate
            {
                await CheckRealmStatusAsync();
                _realmTimer.Start();
            };
            FormClosed += delegate
            {
                _realmTimer.Stop();
                _realmTimer.Dispose();
                _artwork.Dispose();
            };

            LoadSettings();
            Uri savedManifestUri;
            if (String.IsNullOrWhiteSpace(_manifestUrl.Text) ||
                (Uri.TryCreate(_manifestUrl.Text, UriKind.Absolute, out savedManifestUri) && savedManifestUri.IsFile))
            {
                _manifestUrl.Text = DefaultManifestUrl;
                SaveSettings(null, EventArgs.Empty);
            }
            if (String.IsNullOrWhiteSpace(_clientPath.Text))
            {
                string launcherDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (File.Exists(Path.Combine(launcherDirectory, "Wow.exe")))
                {
                    _clientPath.Text = launcherDirectory;
                    SaveSettings(null, EventArgs.Empty);
                }
            }
        }

        private static HttpClient CreateHttpClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpClient client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(60);
            return client;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int x = (ClientSize.Width - _artwork.Width) / 2;
            int y = (ClientSize.Height - _artwork.Height) / 2;
            e.Graphics.DrawImageUnscaled(_artwork, x, y);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            Point point = ToArtworkPoint(e.Location);
            if (_busy)
                return;

            if (e.Button == MouseButtons.Left && InRect(point, 67, 628, 397, 94))
                UpdateAndPlay(this, EventArgs.Empty);
            else if (e.Button == MouseButtons.Left && InRect(point, 57, 779, 310, 48))
                UpdateAddons(this, EventArgs.Empty);
            else if (e.Button == MouseButtons.Left && InRect(point, 395, 779, 55, 48))
                ShowSettings(this, EventArgs.Empty);
            else if (e.Button == MouseButtons.Left && InRect(point, 1507, 27, 50, 40))
                WindowState = FormWindowState.Minimized;
            else if (e.Button == MouseButtons.Left && InRect(point, 1558, 27, 49, 40))
            {
                WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                Invalidate();
            }
            else if (e.Button == MouseButtons.Left && InRect(point, 1609, 27, 44, 40))
                Close();
            else if (e.Button == MouseButtons.Left && InRect(point, 45, 407, 435, 100))
                ShowRealmStatus();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point point = ToArtworkPoint(e.Location);
            bool clickable = InRect(point, 67, 628, 397, 94) || InRect(point, 57, 779, 310, 48) ||
                InRect(point, 395, 779, 55, 48) || InRect(point, 1507, 27, 146, 40) || InRect(point, 45, 407, 435, 100);
            Cursor = clickable ? Cursors.Hand : (point.Y >= 0 && point.Y < 25 && point.X >= 80 && point.X < 1495 ? Cursors.SizeAll : Cursors.Default);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Point point = ToArtworkPoint(e.Location);
            if (e.Button == MouseButtons.Left && point.Y >= 0 && point.Y < 25 && point.X >= 80 && point.X < 1495)
            {
                ReleaseCapture();
                SendMessage(Handle, WmNcLButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            Point point = ToArtworkPoint(e.Location);
            if (e.Button == MouseButtons.Left && point.Y >= 0 && point.Y < 25 && point.X >= 80 && point.X < 1495)
            {
                WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                Invalidate();
            }
        }

        private Point ToArtworkPoint(Point point)
        {
            return new Point(point.X - (ClientSize.Width - _artwork.Width) / 2, point.Y - (ClientSize.Height - _artwork.Height) / 2);
        }

        private static Label CreateLabel(string text, Font font, Color color, Point location, Size size)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = font;
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.Location = location;
            label.Size = size;
            label.AutoEllipsis = true;
            return label;
        }

        private static bool InRect(Point point, int x, int y, int width, int height)
        {
            return point.X >= x && point.X < x + width && point.Y >= y && point.Y < y + height;
        }

        private async Task CheckRealmStatusAsync()
        {
            if (_realmCheckInProgress)
                return;
            _realmCheckInProgress = true;
            try
            {
                Match match = Regex.Match(_activeRealmEndpoint, "^([A-Za-z0-9.-]+):([0-9]{1,5})$");
                int port;
                if (!match.Success || !Int32.TryParse(match.Groups[2].Value, out port))
                {
                    _realmOnline = false;
                    return;
                }

                using (TcpClient client = new TcpClient())
                {
                    Task connection = client.ConnectAsync(match.Groups[1].Value, port);
                    Task completed = await Task.WhenAny(connection, Task.Delay(3500));
                    if (!Object.ReferenceEquals(completed, connection))
                    {
                        _realmOnline = false;
                        return;
                    }

                    await connection;
                    _realmOnline = true;
                }
            }
            catch
            {
                _realmOnline = false;
            }
            finally
            {
                _realmCheckInProgress = false;
            }
        }

        private async void ShowRealmStatus()
        {
            await CheckRealmStatusAsync();
            string status = _realmOnline ? "The realm login server is online." : "The realm login server is offline.";
            MessageBox.Show(this, status, "Realm Status", MessageBoxButtons.OK,
                _realmOnline ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void ShowSettings(object sender, EventArgs e)
        {
            using (Form dialog = new Form())
            {
                dialog.Text = "Launcher Settings";
                dialog.ClientSize = new Size(620, 250);
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.BackColor = Color.FromArgb(13, 26, 42);
                dialog.ForeColor = Color.White;
                dialog.Font = new Font("Segoe UI", 9F);

                Label clientLabel = CreateLabel("Wrath client folder (must contain Wow.exe)", new Font("Segoe UI", 9F, FontStyle.Bold),
                    Color.FromArgb(220, 231, 243), new Point(22, 20), new Size(560, 22));
                dialog.Controls.Add(clientLabel);
                TextBox clientPath = new TextBox();
                clientPath.Text = _clientPath.Text;
                clientPath.Location = new Point(24, 47);
                clientPath.Size = new Size(460, 26);
                dialog.Controls.Add(clientPath);

                Button browse = new Button();
                browse.Text = "Browse...";
                browse.Location = new Point(494, 45);
                browse.Size = new Size(102, 29);
                browse.Click += delegate
                {
                    using (FolderBrowserDialog folder = new FolderBrowserDialog())
                    {
                        folder.Description = "Select the folder that contains Wow.exe.";
                        if (Directory.Exists(clientPath.Text))
                            folder.SelectedPath = clientPath.Text;
                        if (folder.ShowDialog(dialog) == DialogResult.OK)
                            clientPath.Text = folder.SelectedPath;
                    }
                };
                dialog.Controls.Add(browse);

                Label manifestLabel = CreateLabel("Update manifest URL", new Font("Segoe UI", 9F, FontStyle.Bold),
                    Color.FromArgb(220, 231, 243), new Point(22, 91), new Size(560, 22));
                dialog.Controls.Add(manifestLabel);
                TextBox manifestUrl = new TextBox();
                manifestUrl.Text = _manifestUrl.Text;
                manifestUrl.Location = new Point(24, 118);
                manifestUrl.Size = new Size(572, 26);
                dialog.Controls.Add(manifestUrl);

                Button save = new Button();
                save.Text = "Save";
                save.DialogResult = DialogResult.OK;
                save.Location = new Point(410, 185);
                save.Size = new Size(88, 34);
                dialog.Controls.Add(save);
                Button cancel = new Button();
                cancel.Text = "Cancel";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.Location = new Point(508, 185);
                cancel.Size = new Size(88, 34);
                dialog.Controls.Add(cancel);
                dialog.AcceptButton = save;
                dialog.CancelButton = cancel;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _clientPath.Text = clientPath.Text.Trim();
                    _manifestUrl.Text = manifestUrl.Text.Trim();
                    SaveSettings(null, EventArgs.Empty);
                }
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                    return;
                Dictionary<string, object> settings = _json.Deserialize<Dictionary<string, object>>(File.ReadAllText(_settingsPath));
                if (settings.ContainsKey("clientPath"))
                    _clientPath.Text = Convert.ToString(settings["clientPath"]);
                if (settings.ContainsKey("manifestUrl"))
                    _manifestUrl.Text = Convert.ToString(settings["manifestUrl"]);
            }
            catch (Exception exception)
            {
                SetStatus("Could not read launcher settings: " + exception.Message, true);
            }
        }

        private void SaveSettings(object sender, EventArgs e)
        {
            try
            {
                Dictionary<string, string> settings = new Dictionary<string, string>();
                settings["clientPath"] = _clientPath.Text.Trim();
                settings["manifestUrl"] = _manifestUrl.Text.Trim();
                File.WriteAllText(_settingsPath, _json.Serialize(settings), Encoding.UTF8);
            }
            catch (Exception exception)
            {
                SetStatus("Could not save launcher settings: " + exception.Message, true);
            }
        }

        private async void UpdateAddons(object sender, EventArgs e)
        {
            await SyncAndMaybePlay(false);
        }

        private async void UpdateAndPlay(object sender, EventArgs e)
        {
            await SyncAndMaybePlay(true);
        }

        private async Task SyncAndMaybePlay(bool playAfter)
        {
            string clientPath = _clientPath.Text.Trim();
            string wowPath = Path.Combine(clientPath, "Wow.exe");
            if (!File.Exists(wowPath))
            {
                SetStatus("Select the Wrath client folder that contains Wow.exe.", true);
                MessageBox.Show(this, _statusMessage, "Wrath of Azeroth Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string feedText = _manifestUrl.Text.Trim();
            if (String.IsNullOrEmpty(feedText))
            {
                SetStatus("Enter your server's update manifest URL to check for updates.", true);
                MessageBox.Show(this, _statusMessage, "Wrath of Azeroth Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            SetBusy(true);
            SaveSettings(null, EventArgs.Empty);
            try
            {
                Uri manifestUri;
                if (!Uri.TryCreate(feedText, UriKind.Absolute, out manifestUri))
                    throw new InvalidOperationException("The manifest URL must be an absolute HTTPS URL or a local file URL.");
                ValidateTransport(manifestUri);
                byte[] manifestBytes = await ReadUriBytes(manifestUri);
                Dictionary<string, object> manifest = _json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(manifestBytes));
                object schemaValue;
                if (manifest == null || !manifest.TryGetValue("schemaVersion", out schemaValue) || Convert.ToInt32(schemaValue) != 1)
                    throw new InvalidOperationException("This launcher does not support the manifest schema.");

                int updatedPackages = await SyncPackages(clientPath, manifestUri, manifest);
                int updatedFiles = await SyncFiles(clientPath, manifestUri, manifest);
                int removedLegacyPatches = CleanupSupersededPatches(clientPath, manifest);
                UpdateRealmStatusEndpoint(manifest);
                if (playAfter && await UpdateLauncherAndRestart(clientPath, manifestUri, manifest))
                    return;

                int updated = updatedPackages + updatedFiles;
                string status = updated == 0 ? "Your client is up to date." : "Installed or updated " + updated + " server file(s).";
                if (removedLegacyPatches > 0)
                    status += " Removed " + removedLegacyPatches + " obsolete WoA patch(es).";
                SetStatus(status, false);
                if (playAfter)
                    LaunchGame(clientPath);
                else
                    MessageBox.Show(this, status, "Wrath of Azeroth Launcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                SetStatus("Update failed: " + exception.Message, true);
                MessageBox.Show(this, _statusMessage, "Wrath of Azeroth Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void UpdateRealmStatusEndpoint(Dictionary<string, object> manifest)
        {
            object endpointValue;
            string endpoint = manifest.TryGetValue("realmlist", out endpointValue) ? Convert.ToString(endpointValue) : DefaultRealmListEndpoint;
            Match match = Regex.Match(endpoint, "^([A-Za-z0-9.-]+):([0-9]{1,5})$");
            int port;
            if (!match.Success || !Int32.TryParse(match.Groups[2].Value, out port) || port < 1 || port > 65535 ||
                Uri.CheckHostName(match.Groups[1].Value) == UriHostNameType.Unknown)
                throw new InvalidOperationException("The update manifest contains an invalid realm login address.");
            _activeRealmEndpoint = endpoint;
        }

        private async Task<bool> UpdateLauncherAndRestart(string clientPath, Uri manifestUri, Dictionary<string, object> manifest)
        {
            object launcherValue;
            Dictionary<string, object> launcher = manifest.TryGetValue("launcher", out launcherValue)
                ? launcherValue as Dictionary<string, object>
                : null;
            if (launcher == null)
                return false;

            string versionText = RequiredString(launcher, "version");
            Version latestVersion;
            Version currentVersion;
            if (!Version.TryParse(versionText, out latestVersion) || !Version.TryParse(LauncherBuildInfo.Version, out currentVersion))
                throw new InvalidOperationException("The launcher update contains an invalid version.");
            if (latestVersion <= currentVersion)
                return false;

            string fileUrl = RequiredString(launcher, "url");
            string expectedHash = RequiredString(launcher, "sha256").ToLowerInvariant();
            if (!Regex.IsMatch(expectedHash, "^[a-f0-9]{64}$"))
                throw new InvalidOperationException("The launcher update contains an invalid SHA-256 hash.");

            Uri launcherUri = ResolveFeedResource(manifestUri, fileUrl, "Launcher update");
            SetStatus("Downloading launcher update " + versionText + "…", false);
            byte[] launcherBytes = await ReadUriBytes(launcherUri);
            if (!String.Equals(ComputeSha256(launcherBytes), expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The downloaded launcher hash does not match.");

            string launcherPath = Application.ExecutablePath;
            string launcherDirectory = Path.GetDirectoryName(launcherPath);
            string stagedPath = Path.Combine(launcherDirectory, ".WoAClientLauncher-" + Guid.NewGuid().ToString("N") + ".new.exe");
            string updaterPath = Path.Combine(Path.GetTempPath(), "WoALauncherUpdate-" + Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllBytes(stagedPath, launcherBytes);
            try
            {
                File.WriteAllText(updaterPath, BuildLauncherUpdaterScript(
                    Process.GetCurrentProcess().Id, launcherPath, stagedPath, clientPath), new UTF8Encoding(false));

                ProcessStartInfo updater = new ProcessStartInfo();
                updater.FileName = "powershell.exe";
                updater.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + updaterPath + "\"";
                updater.UseShellExecute = false;
                updater.CreateNoWindow = true;
                Process.Start(updater);
            }
            catch
            {
                if (File.Exists(stagedPath))
                    File.Delete(stagedPath);
                if (File.Exists(updaterPath))
                    File.Delete(updaterPath);
                throw;
            }

            Close();
            return true;
        }

        private static string BuildLauncherUpdaterScript(int parentProcessId, string launcherPath, string stagedPath, string clientPath)
        {
            StringBuilder script = new StringBuilder();
            script.AppendLine("$ErrorActionPreference = 'Stop'");
            script.AppendLine("$parentProcessId = " + parentProcessId);
            script.AppendLine("$launcherPath = '" + EscapePowerShellString(launcherPath) + "'");
            script.AppendLine("$stagedPath = '" + EscapePowerShellString(stagedPath) + "'");
            script.AppendLine("$clientPath = '" + EscapePowerShellString(clientPath) + "'");
            script.AppendLine("$backupPath = $launcherPath + '.previous'");
            script.AppendLine("$updateInstalled = $false");
            script.AppendLine("while (Get-Process -Id $parentProcessId -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 500 }");
            script.AppendLine("try {");
            script.AppendLine("  if (Test-Path -LiteralPath $backupPath) { Remove-Item -LiteralPath $backupPath -Force }");
            script.AppendLine("  Move-Item -LiteralPath $launcherPath -Destination $backupPath -Force");
            script.AppendLine("  Move-Item -LiteralPath $stagedPath -Destination $launcherPath -Force");
            script.AppendLine("  $updateInstalled = $true");
            script.AppendLine("  Remove-Item -LiteralPath $backupPath -Force");
            script.AppendLine("  Start-Process -FilePath (Join-Path $clientPath 'Wow.exe') -WorkingDirectory $clientPath");
            script.AppendLine("} catch {");
            script.AppendLine("  if (!$updateInstalled -and !(Test-Path -LiteralPath $launcherPath) -and (Test-Path -LiteralPath $backupPath)) { Move-Item -LiteralPath $backupPath -Destination $launcherPath -Force }");
            script.AppendLine("  Add-Type -AssemblyName System.Windows.Forms");
            script.AppendLine("  $message = if ($updateInstalled) { 'The launcher updated, but WoW could not start. Please launch the game again.' } else { 'The launcher update could not be installed. The previous launcher was restored. Please try again or reinstall the launcher.' }");
            script.AppendLine("  [System.Windows.Forms.MessageBox]::Show($message, 'Wrath of Azeroth Launcher', 'OK', 'Error') | Out-Null");
            script.AppendLine("  if (Test-Path -LiteralPath $launcherPath) { Start-Process -FilePath $launcherPath }");
            script.AppendLine("}");
            script.AppendLine("if (Test-Path -LiteralPath $stagedPath) { Remove-Item -LiteralPath $stagedPath -Force }");
            script.AppendLine("$scriptPath = $MyInvocation.MyCommand.Path");
            script.AppendLine("Remove-Item -LiteralPath $scriptPath -Force -ErrorAction SilentlyContinue");
            return script.ToString();
        }

        private static string EscapePowerShellString(string value)
        {
            return value.Replace("'", "''");
        }

        private async Task<int> SyncPackages(string clientPath, Uri manifestUri, Dictionary<string, object> manifest)
        {
            object packageValue;
            IList packages = manifest.TryGetValue("packages", out packageValue) ? packageValue as IList : null;
            if (packages == null)
                return 0;

            string addonsPath = Path.Combine(clientPath, "Interface", "AddOns");
            Directory.CreateDirectory(addonsPath);
            int installedCount = 0;
            foreach (object packageObject in packages)
            {
                Dictionary<string, object> package = packageObject as Dictionary<string, object>;
                if (package == null)
                    throw new InvalidOperationException("A package entry is malformed.");

                string id = RequiredString(package, "id");
                string version = RequiredString(package, "version");
                string packageUrl = RequiredString(package, "url");
                string expectedHash = RequiredString(package, "sha256").ToLowerInvariant();
                if (!Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,64}$"))
                    throw new InvalidOperationException("The package has an invalid addon ID.");
                if (!Regex.IsMatch(expectedHash, "^[a-f0-9]{64}$"))
                    throw new InvalidOperationException("The package has an invalid SHA-256 hash.");

                Uri packageUri = ResolveFeedResource(manifestUri, packageUrl, "Packages");

                string destination = Path.Combine(addonsPath, id);
                if (IsCurrentPackage(destination, version, expectedHash))
                    continue;

                SetStatus("Downloading " + id + " " + version + "…", false);
                byte[] archiveBytes = await ReadUriBytes(packageUri);
                string actualHash = ComputeSha256(archiveBytes);
                if (!String.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The downloaded package hash does not match for " + id + ".");

                InstallPackage(id, version, expectedHash, archiveBytes, destination);
                installedCount++;
            }
            return installedCount;
        }

        private async Task<int> SyncFiles(string clientPath, Uri manifestUri, Dictionary<string, object> manifest)
        {
            object filesValue;
            IList files = manifest.TryGetValue("files", out filesValue) ? filesValue as IList : null;
            if (files == null)
                return 0;

            string clientRoot = Path.GetFullPath(clientPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            HashSet<string> destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int updatedCount = 0;
            foreach (object fileObject in files)
            {
                Dictionary<string, object> file = fileObject as Dictionary<string, object>;
                if (file == null)
                    throw new InvalidOperationException("A client file entry is malformed.");

                string relativePath = ValidateClientFilePath(RequiredString(file, "path"));
                string fileUrl = RequiredString(file, "url");
                string expectedHash = RequiredString(file, "sha256").ToLowerInvariant();
                if (!Regex.IsMatch(expectedHash, "^[a-f0-9]{64}$"))
                    throw new InvalidOperationException("A client file has an invalid SHA-256 hash.");

                string destination = Path.GetFullPath(Path.Combine(clientRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!destination.StartsWith(clientRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A client file path points outside the game folder.");
                if (!destinations.Add(destination))
                    throw new InvalidOperationException("The update manifest lists a client file more than once: " + relativePath);
                if (Directory.Exists(destination))
                    throw new InvalidOperationException("A client file destination is a directory: " + relativePath);

                if (File.Exists(destination) && String.Equals(ComputeSha256(File.ReadAllBytes(destination)), expectedHash, StringComparison.OrdinalIgnoreCase))
                    continue;

                Uri fileUri = ResolveFeedResource(manifestUri, fileUrl, "Client files");
                SetStatus("Checking " + relativePath + "…", false);
                byte[] fileBytes = await ReadUriBytes(fileUri);
                if (!String.Equals(ComputeSha256(fileBytes), expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The downloaded file hash does not match for " + relativePath + ".");

                string directory = Path.GetDirectoryName(destination);
                Directory.CreateDirectory(directory);
                string temporaryPath = destination + ".woa-update-" + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllBytes(temporaryPath, fileBytes);
                    if (File.Exists(destination))
                        File.Replace(temporaryPath, destination, null);
                    else
                        File.Move(temporaryPath, destination);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                updatedCount++;
            }
            return updatedCount;
        }

        private static int CleanupSupersededPatches(string clientPath, Dictionary<string, object> manifest)
        {
            // Only remove known WoA patch files with the exact hashes previously
            // published by this project. This preserves custom or modified MPQs.
            Dictionary<string, string> oldWoAPatches = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "patch-4.MPQ", "ab78ae88e6be8d818e890257607f8f2b1138bf03a13121626e0e258ce99945f8" },
                { "patch-5.MPQ", "0de08326fa48a0ebeea7de13db74038f137314a5e828877315cedaa5f616239e" },
                { "patch-6.MPQ", "6ca0f7a6134271b86e23c9f62ff1140718d668f0b54614d7a21785cf76ecac58" },
                { "patch-7.MPQ", "8f885ae5661d236662d552add2d6a8e1f19307ab764c332121c436651c2c0979" },
                { "patch-8.MPQ", "ea9cd049a997efb506efba1a42f8ea2cb4397e0cafd26c20b7372050d6828a48" }
            };

            object filesValue;
            IList files = manifest.TryGetValue("files", out filesValue) ? filesValue as IList : null;
            if (files == null)
                return 0;

            bool combinedPatchIsVerified = false;
            foreach (object fileObject in files)
            {
                Dictionary<string, object> file = fileObject as Dictionary<string, object>;
                if (file == null)
                    continue;

                string relativePath;
                object pathValue;
                if (!file.TryGetValue("path", out pathValue) || pathValue == null)
                    continue;
                relativePath = Convert.ToString(pathValue).Replace('\\', '/');
                if (!String.Equals(relativePath, "Data/patch-9.MPQ", StringComparison.OrdinalIgnoreCase))
                    continue;

                string expectedHash;
                object hashValue;
                if (!file.TryGetValue("sha256", out hashValue) || hashValue == null)
                    return 0;
                expectedHash = Convert.ToString(hashValue);
                string combinedPatchPath = Path.GetFullPath(Path.Combine(clientPath, "Data", "patch-9.MPQ"));
                string clientRoot = Path.GetFullPath(clientPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!combinedPatchPath.StartsWith(clientRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(combinedPatchPath))
                    return 0;
                combinedPatchIsVerified = String.Equals(ComputeSha256(File.ReadAllBytes(combinedPatchPath)), expectedHash, StringComparison.OrdinalIgnoreCase);
                break;
            }

            if (!combinedPatchIsVerified)
                return 0;

            string dataDirectory = Path.GetFullPath(Path.Combine(clientPath, "Data"));
            string clientDataRoot = Path.GetFullPath(clientPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!dataDirectory.StartsWith(clientDataRoot, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(dataDirectory))
                return 0;

            int removed = 0;
            foreach (KeyValuePair<string, string> oldPatch in oldWoAPatches)
            {
                string oldPath = Path.GetFullPath(Path.Combine(dataDirectory, oldPatch.Key));
                if (!oldPath.StartsWith(dataDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(oldPath))
                    continue;
                if (!String.Equals(ComputeSha256(File.ReadAllBytes(oldPath)), oldPatch.Value, StringComparison.OrdinalIgnoreCase))
                    continue;

                File.Delete(oldPath);
                removed++;
            }

            return removed;
        }

        private static Uri ResolveFeedResource(Uri manifestUri, string resourceUrl, string resourceType)
        {
            Uri resourceUri;
            if (!Uri.TryCreate(manifestUri, resourceUrl, out resourceUri))
                throw new InvalidOperationException(resourceType + " must use a valid URL relative to the manifest.");
            ValidateTransport(resourceUri);
            if (manifestUri.IsFile != resourceUri.IsFile || (!manifestUri.IsFile && !String.Equals(manifestUri.Host, resourceUri.Host, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(resourceType + " must be hosted beside the manifest on the same server.");
            return resourceUri;
        }

        private static string ValidateClientFilePath(string path)
        {
            string normalized = path.Trim().Replace('\\', '/');
            if (Path.IsPathRooted(normalized) || normalized.StartsWith("/", StringComparison.Ordinal) || normalized.IndexOf(':') >= 0)
                throw new InvalidOperationException("A client file path must be relative to the game folder.");

            string[] segments = normalized.Split('/');
            if (segments.Length < 2 || ( !String.Equals(segments[0], "Data", StringComparison.OrdinalIgnoreCase)
                    && !String.Equals(segments[0], "Interface", StringComparison.OrdinalIgnoreCase)
                    && !String.Equals(segments[0], "WTF", StringComparison.OrdinalIgnoreCase)
                    && !String.Equals(segments[0], "Fonts", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Client file updates are allowed only under Data, Interface, WTF, or Fonts.");

            foreach (string segment in segments)
            {
                if (segment.Length == 0 || segment == "." || segment == ".." || segment.EndsWith(".", StringComparison.Ordinal) || segment.EndsWith(" ", StringComparison.Ordinal))
                    throw new InvalidOperationException("A client file path contains an unsafe folder or file name.");
                foreach (char character in Path.GetInvalidFileNameChars())
                    if (segment.IndexOf(character) >= 0)
                        throw new InvalidOperationException("A client file path contains an invalid character.");
            }
            return String.Join(Path.DirectorySeparatorChar.ToString(), segments);
        }

        private static string RequiredString(Dictionary<string, object> values, string key)
        {
            object value;
            string result = values.TryGetValue(key, out value) ? Convert.ToString(value) : null;
            if (String.IsNullOrWhiteSpace(result))
                throw new InvalidOperationException("A package is missing " + key + ".");
            return result.Trim();
        }

        private static void ValidateTransport(Uri uri)
        {
            if (uri.IsFile)
                return;
            if (!String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Public update feeds must use HTTPS.");
        }

        private async Task<byte[]> ReadUriBytes(Uri uri)
        {
            if (uri.IsFile)
                return File.ReadAllBytes(uri.LocalPath);
            using (HttpResponseMessage response = await Http.GetAsync(uri))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync();
            }
        }

        private bool IsCurrentPackage(string destination, string version, string hash)
        {
            string markerPath = Path.Combine(destination, ".woa-launcher.json");
            if (!File.Exists(markerPath))
                return false;
            try
            {
                Dictionary<string, object> marker = _json.Deserialize<Dictionary<string, object>>(File.ReadAllText(markerPath));
                return marker.ContainsKey("version") && marker.ContainsKey("sha256") &&
                    String.Equals(Convert.ToString(marker["version"]), version, StringComparison.Ordinal) &&
                    String.Equals(Convert.ToString(marker["sha256"]), hash, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void InstallPackage(string id, string version, string hash, byte[] archiveBytes, string destination)
        {
            string stagingRoot = Path.Combine(Path.GetTempPath(), "WoALauncher", Guid.NewGuid().ToString("N"));
            string expectedRoot = Path.GetFullPath(Path.Combine(stagingRoot, id)) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(stagingRoot);
            bool foundToc = false;
            try
            {
                using (MemoryStream memory = new MemoryStream(archiveBytes))
                using (ZipArchive archive = new ZipArchive(memory, ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string normalized = entry.FullName.Replace('\\', '/');
                        if (normalized.EndsWith("/", StringComparison.Ordinal))
                            continue;
                        string[] segments = normalized.Split('/');
                        if (segments.Length < 2 || !String.Equals(segments[0], id, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("The package contains a file outside its addon folder.");
                        for (int index = 0; index < segments.Length; index++)
                            if (segments[index] == ".." || segments[index] == "." || segments[index].Length == 0)
                                throw new InvalidOperationException("The package contains an unsafe path.");

                        string relativePath = String.Join(Path.DirectorySeparatorChar.ToString(), segments, 1, segments.Length - 1);
                        string outputPath = Path.GetFullPath(Path.Combine(stagingRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
                        if (!outputPath.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("The package contains a path outside its addon folder.");
                        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                        using (Stream source = entry.Open())
                        using (FileStream output = File.Create(outputPath))
                            source.CopyTo(output);
                        if (String.Equals(Path.GetExtension(relativePath), ".toc", StringComparison.OrdinalIgnoreCase))
                            foundToc = true;
                    }
                }

                if (!foundToc)
                    throw new InvalidOperationException("The package does not contain an addon TOC file.");

                string stagedAddon = Path.Combine(stagingRoot, id);
                Directory.CreateDirectory(destination);
                CopyDirectory(stagedAddon, destination);
                Dictionary<string, string> marker = new Dictionary<string, string>();
                marker["version"] = version;
                marker["sha256"] = hash;
                File.WriteAllText(Path.Combine(destination, ".woa-launcher.json"), _json.Serialize(marker), Encoding.UTF8);
            }
            finally
            {
                try
                {
                    Directory.Delete(stagingRoot, true);
                }
                catch
                {
                    // A temporary extraction folder can be cleaned up by Windows later.
                }
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            foreach (string directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }

        private static string ComputeSha256(byte[] data)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] digest = sha256.ComputeHash(data);
                StringBuilder output = new StringBuilder(digest.Length * 2);
                foreach (byte value in digest)
                    output.Append(value.ToString("x2"));
                return output.ToString();
            }
        }

        private void LaunchGame(string clientPath)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = Path.Combine(clientPath, "Wow.exe");
            startInfo.WorkingDirectory = clientPath;
            startInfo.UseShellExecute = true;
            Process.Start(startInfo);
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
        }

        private void SetStatus(string message, bool isError)
        {
            _statusMessage = message;
        }
    }
}
