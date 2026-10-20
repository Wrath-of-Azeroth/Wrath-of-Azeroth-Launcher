using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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
        private static readonly HttpClient Http = CreateHttpClient();
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly TextBox _clientPath = new TextBox();
        private readonly TextBox _manifestUrl = new TextBox();
        private readonly Label _status = new Label();
        private readonly Button _updateButton = new Button();
        private readonly Button _updateAndPlayButton = new Button();
        private readonly string _settingsPath;
        private readonly LauncherBackgroundPanel _canvas = new LauncherBackgroundPanel();
        private readonly Label _realmStatus = new Label();
        private readonly StatusDot _realmDot = new StatusDot();
        private readonly Label _clientStatus = new Label();
        private readonly Timer _realmTimer = new Timer();
        private bool _realmCheckInProgress;
        private string _activeRealmEndpoint = DefaultRealmListEndpoint;

        public LauncherForm()
        {
            Text = "Wrath of Azeroth Launcher";
            MinimumSize = new Size(1024, 600);
            ClientSize = new Size(1280, 720);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.FromArgb(5, 12, 22);
            ForeColor = Color.White;
            Icon = SystemIcons.Application;
            DoubleBuffered = true;

            string dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WrathOfAzerothLauncher");
            Directory.CreateDirectory(dataDirectory);
            _settingsPath = Path.Combine(dataDirectory, "settings.json");
            _realmTimer.Interval = 15000;
            _realmTimer.Tick += async delegate { await CheckRealmStatusAsync(); };
            FormClosed += delegate
            {
                _realmTimer.Stop();
                _realmTimer.Dispose();
            };

            BuildInterface();
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

        private void BuildInterface()
        {
            Text = "Wrath of Azeroth Launcher";
            _canvas.Dock = DockStyle.Fill;
            Controls.Add(_canvas);

            GlassPanel glass = new GlassPanel();
            glass.Location = new Point(42, 48);
            glass.Size = new Size(430, 530);
            _canvas.Controls.Add(glass);

            Label title = CreateLabel("WRATH OF AZEROTH", new Font("Segoe UI", 24F, FontStyle.Bold),
                Color.FromArgb(245, 248, 255), new Point(28, 28), new Size(365, 42));
            glass.Controls.Add(title);

            Label subtitle = CreateLabel("WOTLK 3.3.5a", new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Color.FromArgb(135, 216, 244), new Point(30, 75), new Size(300, 22));
            glass.Controls.Add(subtitle);

            Button settings = CreateButton("⚙", new Point(1190, 24), new Size(46, 42),
                Color.FromArgb(185, 8, 15, 26), Color.FromArgb(215, 218, 232), 14F);
            settings.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            settings.FlatAppearance.BorderColor = Color.FromArgb(95, 168, 199, 219);
            settings.FlatAppearance.BorderSize = 1;
            settings.Click += ShowSettings;
            _canvas.Controls.Add(settings);

            Panel realmCard = new Panel();
            realmCard.Location = new Point(24, 122);
            realmCard.Size = new Size(378, 70);
            realmCard.BackColor = Color.FromArgb(110, 3, 10, 18);
            glass.Controls.Add(realmCard);

            Label realmName = CreateLabel("FROSTGATE", new Font("Segoe UI", 11F, FontStyle.Bold),
                Color.White, new Point(15, 9), new Size(195, 22));
            realmCard.Controls.Add(realmName);

            _realmStatus.Text = "CHECKING...";
            _realmStatus.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            _realmStatus.ForeColor = Color.FromArgb(132, 205, 226);
            _realmStatus.TextAlign = ContentAlignment.MiddleLeft;
            _realmStatus.Location = new Point(239, 12);
            _realmStatus.Size = new Size(124, 20);
            realmCard.Controls.Add(_realmStatus);

            _realmDot.Location = new Point(219, 16);
            _realmDot.Size = new Size(12, 12);
            realmCard.Controls.Add(_realmDot);

            Label realmDetail = CreateLabel("Wrath of the Lich King  •  3.3.5a", new Font("Segoe UI", 8.5F),
                Color.FromArgb(167, 186, 208), new Point(15, 33), new Size(330, 18));
            realmCard.Controls.Add(realmDetail);

            _clientStatus.Text = "CLIENT READY TO CHECK";
            _clientStatus.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            _clientStatus.ForeColor = Color.FromArgb(180, 199, 219);
            _clientStatus.BackColor = Color.Transparent;
            _clientStatus.Location = new Point(29, 215);
            _clientStatus.Size = new Size(365, 22);
            glass.Controls.Add(_clientStatus);

            _updateAndPlayButton.Text = "PLAY";
            _updateAndPlayButton.Location = new Point(24, 258);
            _updateAndPlayButton.Size = new Size(378, 58);
            _updateAndPlayButton.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _updateAndPlayButton.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            _updateAndPlayButton.ForeColor = Color.FromArgb(6, 18, 30);
            _updateAndPlayButton.BackColor = Color.FromArgb(107, 220, 247);
            _updateAndPlayButton.FlatStyle = FlatStyle.Flat;
            _updateAndPlayButton.FlatAppearance.BorderSize = 0;
            _updateAndPlayButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(156, 235, 255);
            _updateAndPlayButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(75, 187, 222);
            _updateAndPlayButton.Click += UpdateAndPlay;
            glass.Controls.Add(_updateAndPlayButton);

            _updateButton.Text = "CHECK FOR UPDATES";
            _updateButton.Location = new Point(24, 330);
            _updateButton.Size = new Size(378, 48);
            _updateButton.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _updateButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _updateButton.ForeColor = Color.FromArgb(222, 233, 244);
            _updateButton.BackColor = Color.FromArgb(170, 8, 18, 31);
            _updateButton.FlatStyle = FlatStyle.Flat;
            _updateButton.FlatAppearance.BorderColor = Color.FromArgb(135, 113, 190, 218);
            _updateButton.FlatAppearance.BorderSize = 1;
            _updateButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(205, 15, 41, 61);
            _updateButton.Click += UpdateAddons;
            glass.Controls.Add(_updateButton);

            _status.Text = "Ready. Play checks server files before launching the game.";
            _status.Location = new Point(28, 401);
            _status.Size = new Size(370, 50);
            _status.ForeColor = Color.FromArgb(184, 201, 220);
            _status.Font = new Font("Segoe UI", 8.5F);
            _status.BackColor = Color.Transparent;
            glass.Controls.Add(_status);

            Shown += async delegate
            {
                await CheckRealmStatusAsync();
                _realmTimer.Start();
            };
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

        private static Button CreateButton(string text, Point location, Size size, Color background, Color foreground, float fontSize)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = location;
            button.Size = size;
            button.Font = new Font("Segoe UI Symbol", fontSize, FontStyle.Bold);
            button.ForeColor = foreground;
            button.BackColor = background;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.TabStop = false;
            return button;
        }

        private async Task CheckRealmStatusAsync()
        {
            if (_realmCheckInProgress)
                return;
            _realmCheckInProgress = true;
            _realmStatus.Text = "CHECKING...";
            _realmStatus.ForeColor = Color.FromArgb(132, 205, 226);
            _realmDot.DotColor = Color.FromArgb(240, 183, 90);
            try
            {
                Match match = Regex.Match(_activeRealmEndpoint, "^([A-Za-z0-9.-]+):([0-9]{1,5})$");
                int port;
                if (!match.Success || !Int32.TryParse(match.Groups[2].Value, out port))
                {
                    _realmStatus.Text = "UNAVAILABLE";
                    _realmDot.DotColor = Color.FromArgb(236, 67, 72);
                    return;
                }

                using (TcpClient client = new TcpClient())
                {
                    Task connection = client.ConnectAsync(match.Groups[1].Value, port);
                    Task completed = await Task.WhenAny(connection, Task.Delay(3500));
                    if (!Object.ReferenceEquals(completed, connection))
                    {
                        _realmStatus.Text = "REALM OFFLINE";
                        _realmStatus.ForeColor = Color.FromArgb(255, 159, 129);
                        _realmDot.DotColor = Color.FromArgb(236, 67, 72);
                        return;
                    }

                    await connection;
                    _realmStatus.Text = "REALM ONLINE";
                    _realmStatus.ForeColor = Color.FromArgb(133, 225, 174);
                    _realmDot.DotColor = Color.FromArgb(62, 220, 128);
                }
            }
            catch
            {
                _realmStatus.Text = "REALM OFFLINE";
                _realmStatus.ForeColor = Color.FromArgb(255, 159, 129);
                _realmDot.DotColor = Color.FromArgb(236, 67, 72);
            }
            finally
            {
                _realmCheckInProgress = false;
            }
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
                return;
            }

            string feedText = _manifestUrl.Text.Trim();
            if (String.IsNullOrEmpty(feedText))
            {
                SetStatus("Enter your server's update manifest URL to check for updates.", true);
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
                ConfigureRealmlist(clientPath, manifest);
                int updated = updatedPackages + updatedFiles;
                string status = updated == 0 ? "Your client is up to date." : "Installed or updated " + updated + " server file(s).";
                _clientStatus.Text = updated == 0 ? "CLIENT UP TO DATE" : "CLIENT UPDATED";
                _clientStatus.ForeColor = Color.FromArgb(133, 225, 174);
                if (removedLegacyPatches > 0)
                    status += " Removed " + removedLegacyPatches + " obsolete WoA patch(es).";
                SetStatus(status, false);
                if (playAfter)
                    LaunchGame(clientPath);
            }
            catch (Exception exception)
            {
                _clientStatus.Text = "UPDATE CHECK FAILED";
                _clientStatus.ForeColor = Color.FromArgb(255, 159, 129);
                SetStatus("Update failed: " + exception.Message, true);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ConfigureRealmlist(string clientPath, Dictionary<string, object> manifest)
        {
            object endpointValue;
            string endpoint = manifest.TryGetValue("realmlist", out endpointValue) ? Convert.ToString(endpointValue) : DefaultRealmListEndpoint;
            Match match = Regex.Match(endpoint, "^([A-Za-z0-9.-]+):([0-9]{1,5})$");
            int port;
            if (!match.Success || !Int32.TryParse(match.Groups[2].Value, out port) || port < 1 || port > 65535 ||
                Uri.CheckHostName(match.Groups[1].Value) == UriHostNameType.Unknown)
                throw new InvalidOperationException("The update manifest contains an invalid realm login address.");
            _activeRealmEndpoint = endpoint;

            string dataPath = Path.Combine(clientPath, "Data");
            if (!Directory.Exists(dataPath))
                throw new InvalidOperationException("The selected client does not contain a Data folder.");

            string[] localeDirectories = Directory.GetDirectories(dataPath);
            int written = 0;
            foreach (string localeDirectory in localeDirectories)
            {
                string locale = Path.GetFileName(localeDirectory);
                if (!Regex.IsMatch(locale, "^[a-z]{2}[A-Z]{2}$"))
                    continue;

                string realmlistPath = Path.Combine(localeDirectory, "realmlist.wtf");
                File.WriteAllText(realmlistPath, "set realmlist " + endpoint + Environment.NewLine, new UTF8Encoding(false));
                written++;
            }

            if (written == 0)
                throw new InvalidOperationException("No WoW locale folder was found under the client's Data folder.");
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
            _updateButton.Enabled = !busy;
            _updateAndPlayButton.Enabled = !busy;
            _updateAndPlayButton.Text = busy ? "UPDATING..." : "PLAY";
        }

        private void SetStatus(string message, bool isError)
        {
            _status.Text = message;
            _status.ForeColor = isError ? Color.FromArgb(255, 125, 125) : Color.FromArgb(179, 199, 219);
        }
    }

    internal sealed class LauncherBackgroundPanel : Panel
    {
        private readonly Image _background;

        public LauncherBackgroundPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            string artworkPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Frostgate.png");
            try
            {
                if (File.Exists(artworkPath))
                    _background = Image.FromFile(artworkPath);
            }
            catch
            {
                _background = null;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (_background == null)
            {
                graphics.Clear(Color.FromArgb(7, 16, 28));
            }
            else
            {
                float scale = Math.Max((float)ClientSize.Width / _background.Width, (float)ClientSize.Height / _background.Height);
                float width = _background.Width * scale;
                float height = _background.Height * scale;
                RectangleF destination = new RectangleF((ClientSize.Width - width) / 2F, (ClientSize.Height - height) / 2F, width, height);
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    ColorMatrix matrix = new ColorMatrix(new float[][]
                    {
                        new float[] { 0.76F, 0F, 0F, 0F, 0F },
                        new float[] { 0F, 0.80F, 0F, 0F, 0F },
                        new float[] { 0F, 0F, 0.90F, 0F, 0F },
                        new float[] { 0F, 0F, 0F, 1F, 0F },
                        new float[] { 0F, 0F, 0F, 0F, 1F }
                    });
                    attributes.SetColorMatrix(matrix);
                    graphics.DrawImage(_background, Rectangle.Round(destination), 0, 0, _background.Width, _background.Height, GraphicsUnit.Pixel, attributes);
                }
            }

            using (LinearGradientBrush leftShade = new LinearGradientBrush(
                new Point(0, 0), new Point(Math.Max(500, ClientSize.Width / 2), 0),
                Color.FromArgb(62, 3, 10, 20), Color.FromArgb(4, 3, 10, 20)))
            {
                graphics.FillRectangle(leftShade, new Rectangle(0, 0, Math.Max(500, ClientSize.Width / 2), ClientSize.Height));
            }

            using (LinearGradientBrush bottomShade = new LinearGradientBrush(
                new Point(0, 0), new Point(0, ClientSize.Height),
                Color.FromArgb(0, 2, 8, 16), Color.FromArgb(100, 2, 8, 16)))
            {
                graphics.FillRectangle(bottomShade, ClientRectangle);
            }

            using (Pen edge = new Pen(Color.FromArgb(80, 102, 202, 232), 1F))
                graphics.DrawLine(edge, 0, 0, ClientSize.Width, 0);
        }
    }

    internal sealed class GlassPanel : Panel
    {
        public GlassPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = ClientRectangle;
            bounds.Inflate(-1, -1);
            using (GraphicsPath shape = CreateRoundedRectangle(bounds, 15))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(188, 5, 14, 27)))
            using (Pen border = new Pen(Color.FromArgb(132, 103, 194, 220), 1F))
            {
                e.Graphics.FillPath(fill, shape);
                e.Graphics.DrawPath(border, shape);
            }
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180F, 90F);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270F, 90F);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0F, 90F);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90F, 90F);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class StatusDot : Control
    {
        private Color _dotColor = Color.FromArgb(240, 183, 90);

        public Color DotColor
        {
            get { return _dotColor; }
            set
            {
                _dotColor = value;
                Invalidate();
            }
        }

        public StatusDot()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
            using (SolidBrush glow = new SolidBrush(Color.FromArgb(72, _dotColor.R, _dotColor.G, _dotColor.B)))
                e.Graphics.FillEllipse(glow, bounds);
            bounds.Inflate(-2, -2);
            using (SolidBrush dot = new SolidBrush(_dotColor))
                e.Graphics.FillEllipse(dot, bounds);
        }
    }
}
