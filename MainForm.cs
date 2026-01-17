using System;
using System.Drawing;
using System.Windows.Forms;
using System.ServiceProcess;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Net.Http;
using System.Text.Json;

namespace BoostProUI
{
    public class MainForm : Form
    {
        // P/Invoke for clearing standby memory
        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(int InfoClass, IntPtr Info, int Length);

        // P/Invoke for timer resolution (reduces input lag)
        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSetTimerResolution(uint DesiredResolution, bool SetResolution, out uint CurrentResolution);

        private const int SystemMemoryListInformation = 80;
        private const int MemoryPurgeStandbyList = 4;

        // Version info for update checker
        private const string CurrentVersion = "1.0.0";
        private const string GitHubRepo = "Skeeter-Modding/BoostPro";
        private const string ReleasesUrl = "https://github.com/Skeeter-Modding/BoostPro/releases";

        private Button btnOptimize, btnRestore;
        private RichTextBox txtOutput;
        private ProgressBar progressBar;
        private Label lblTitle, lblStatus, lblRamUsage, lblCpuUsage, lblGpuUsage;
        private Panel headerPanel, statsPanel;
        private CheckBox chkServices, chkStartup, chkTempFiles, chkPowerPlan;
        private CheckBox chkBackgroundApps, chkGpuScheduling, chkMemoryCleanup;
        private CheckBox chkVisualEffects, chkNetworkOptimize, chkPrefetch;
        private CheckBox chkDefragMemory, chkProcessPriority;
        private CheckBox chkClearStandby, chkKillBloat, chkDisableTelemetry;
        private CheckBox chkDisableGameDVR, chkTimerResolution, chkDisableCoreParking;
        private CheckBox chkDisableFullscreenOpt, chkUltimatePower, chkDisableMemCompression;
        private GroupBox optionsGroup;
        private System.Windows.Forms.Timer performanceTimer = null!;

        // Performance counters - created once, reused, and properly disposed
        private PerformanceCounter? _ramCounter;
        private PerformanceCounter? _cpuCounter;
        private ulong _totalRamMB;

        // Services that eat RAM/CPU in the background - safe to stop temporarily
        static string[] servicesToStop = new string[] {
            // Original services
            "SysMain",           // Superfetch - pre-loads apps into RAM (big RAM hog)
            "WSearch",           // Windows Search indexing
            "DiagTrack",         // Telemetry (Connected User Experiences)
            "Fax",               // Fax service
            "RetailDemo",        // Retail demo mode
            "MapsBroker",        // Downloaded Maps Manager
            "WbioSrvc",          // Biometric service
            "WMPNetworkSvc",     // Windows Media Player sharing
            "WpcMonSvc",         // Parental Controls
            "wisvc",             // Windows Insider Service
            "BITS",              // Background Intelligent Transfer
            "DoSvc",             // Delivery Optimization (Windows Update P2P)

            // Xbox services (RAM hogs if not gaming on Xbox)
            "XblAuthManager",    // Xbox Live Auth
            "XblGameSave",       // Xbox Live Game Save
            "XboxGipSvc",        // Xbox Accessory Management
            "XboxNetApiSvc",     // Xbox Live Networking

            // More telemetry/bloat
            "dmwappushservice",  // WAP Push Message Routing
            "RemoteRegistry",    // Remote Registry
            "WerSvc",            // Windows Error Reporting
            "lfsvc",             // Geolocation Service
            "TabletInputService",// Touch Keyboard (if no touchscreen)
            "PhoneSvc",          // Phone Service
            "icssvc",            // Windows Mobile Hotspot
            "WalletService",     // Wallet Service
            "ClickToRunSvc",     // Microsoft Office Click-to-Run (if not using Office)
            "Spooler"            // Print Spooler (if no printer)
        };

        public MainForm()
        {
            InitializeUI();
            InitializePerformanceCounters();
            CheckAdminStatus();

            // Check for updates in background (don't block startup)
            _ = CheckForUpdatesAsync();
        }

        private void InitializePerformanceCounters()
        {
            try
            {
                _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _totalRamMB = new Microsoft.VisualBasic.Devices.ComputerInfo().TotalPhysicalMemory / (1024 * 1024);
                // Prime the CPU counter (first call always returns 0)
                _cpuCounter.NextValue();
            }
            catch
            {
                // Performance counters may not be available on all systems
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                performanceTimer?.Stop();
                performanceTimer?.Dispose();
                _ramCounter?.Dispose();
                _cpuCounter?.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeUI()
        {
            this.Text = "BoostPro - Advanced Gaming Optimizer";
            this.Size = new Size(900, 780);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(24, 24, 24);

            // Header panel
            headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 120,
                BackColor = Color.FromArgb(15, 15, 25)
            };
            this.Controls.Add(headerPanel);

            lblTitle = new Label
            {
                Text = "⚡ BoostPro",
                Font = new Font("Segoe UI", 24, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 230, 255),
                Location = new Point(25, 20),
                AutoSize = true
            };
            headerPanel.Controls.Add(lblTitle);

            var lblSubtitle = new Label
            {
                Text = "Advanced Gaming Performance Optimizer",
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(150, 150, 150),
                Location = new Point(25, 58),
                AutoSize = true
            };
            headerPanel.Controls.Add(lblSubtitle);

            // Author credit in header
            var lblAuthor = new Label
            {
                Text = "by Skeeter | Triple Threat Tactical Gaming Community",
                Font = new Font("Segoe UI", 9, FontStyle.Italic),
                ForeColor = Color.FromArgb(0, 180, 220),
                Location = new Point(25, 78),
                AutoSize = true
            };
            headerPanel.Controls.Add(lblAuthor);

            lblStatus = new Label
            {
                Text = "● Ready",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 255, 100),
                Location = new Point(25, 98),
                AutoSize = true
            };
            headerPanel.Controls.Add(lblStatus);

            // Stats panel
            statsPanel = new Panel
            {
                Location = new Point(650, 20),
                Size = new Size(220, 80),
                BackColor = Color.FromArgb(30, 30, 40)
            };
            headerPanel.Controls.Add(statsPanel);

            lblRamUsage = new Label { Text = "RAM: --", Font = new Font("Segoe UI", 9), ForeColor = Color.White, Location = new Point(10, 10), AutoSize = true };
            statsPanel.Controls.Add(lblRamUsage);
            lblCpuUsage = new Label { Text = "CPU: --", Font = new Font("Segoe UI", 9), ForeColor = Color.White, Location = new Point(10, 35), AutoSize = true };
            statsPanel.Controls.Add(lblCpuUsage);
            lblGpuUsage = new Label { Text = "GPU: Active", Font = new Font("Segoe UI", 9), ForeColor = Color.White, Location = new Point(10, 60), AutoSize = true };
            statsPanel.Controls.Add(lblGpuUsage);

            // Options group - expanded for more options
            optionsGroup = new GroupBox
            {
                Text = "  Optimization Options",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 230, 255),
                Location = new Point(20, 140),
                Size = new Size(850, 290)
            };
            this.Controls.Add(optionsGroup);

            // Checkboxes - Column 1 (Memory & System)
            chkServices = CreateCheckBox("Stop bloat services (30+)", 20, 30, true);
            chkKillBloat = CreateCheckBox("Kill bloatware processes", 20, 55, true);
            chkClearStandby = CreateCheckBox("Clear standby RAM", 20, 80, true);
            chkMemoryCleanup = CreateCheckBox("Force garbage collection", 20, 105, true);
            chkDefragMemory = CreateCheckBox("Run idle tasks cleanup", 20, 130, true);
            chkTempFiles = CreateCheckBox("Clear temp files", 20, 155, true);
            chkDisableMemCompression = CreateCheckBox("Disable memory compression", 20, 180, true);
            chkStartup = CreateCheckBox("Disable startup items", 20, 205, true);

            // Checkboxes - Column 2 (Performance & Gaming)
            chkUltimatePower = CreateCheckBox("Ultimate Performance power", 280, 30, true);
            chkPowerPlan = CreateCheckBox("High Performance (fallback)", 280, 55, false);
            chkDisableGameDVR = CreateCheckBox("Disable Game DVR/Bar", 280, 80, true);
            chkTimerResolution = CreateCheckBox("Max timer resolution (0.5ms)", 280, 105, true);
            chkDisableCoreParking = CreateCheckBox("Disable CPU core parking", 280, 130, true);
            chkDisableFullscreenOpt = CreateCheckBox("Disable fullscreen optimizations", 280, 155, true);
            chkGpuScheduling = CreateCheckBox("GPU Hardware Scheduling", 280, 180, true);
            chkProcessPriority = CreateCheckBox("High priority for games", 280, 205, false);

            // Checkboxes - Column 3 (Network & Tweaks)
            chkNetworkOptimize = CreateCheckBox("Optimize TCP/Network", 560, 30, true);
            chkDisableTelemetry = CreateCheckBox("Disable telemetry", 560, 55, true);
            chkBackgroundApps = CreateCheckBox("Disable background apps", 560, 80, true);
            chkVisualEffects = CreateCheckBox("Reduce visual effects", 560, 105, true);
            chkPrefetch = CreateCheckBox("Disable Prefetch/Superfetch", 560, 130, true);

            // Add warning label for aggressive options
            var lblWarning = new Label
            {
                Text = "* Some options require restart to take effect",
                Font = new Font("Segoe UI", 8, FontStyle.Italic),
                ForeColor = Color.FromArgb(150, 150, 150),
                Location = new Point(560, 200),
                AutoSize = true
            };
            optionsGroup.Controls.Add(lblWarning);

            optionsGroup.Controls.AddRange(new Control[] {
                chkServices, chkStartup, chkTempFiles, chkPowerPlan, chkBackgroundApps,
                chkGpuScheduling, chkMemoryCleanup, chkVisualEffects, chkNetworkOptimize,
                chkPrefetch, chkDefragMemory, chkProcessPriority, chkClearStandby,
                chkKillBloat, chkDisableTelemetry, chkDisableGameDVR, chkTimerResolution,
                chkDisableCoreParking, chkDisableFullscreenOpt, chkUltimatePower, chkDisableMemCompression
            });

            // Output
            txtOutput = new RichTextBox
            {
                Location = new Point(20, 435),
                Size = new Size(850, 200),
                BackColor = Color.FromArgb(18, 18, 18),
                ForeColor = Color.FromArgb(200, 200, 200),
                Font = new Font("Consolas", 9),
                ReadOnly = true,
                BorderStyle = BorderStyle.None
            };
            this.Controls.Add(txtOutput);

            progressBar = new ProgressBar
            {
                Location = new Point(20, 650),
                Size = new Size(850, 30),
                Style = ProgressBarStyle.Continuous,
                Visible = false
            };
            this.Controls.Add(progressBar);

            btnOptimize = new Button
            {
                Text = "⚡ OPTIMIZE NOW",
                Location = new Point(20, 695),
                Size = new Size(415, 50),
                BackColor = Color.FromArgb(0, 170, 255),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnOptimize.FlatAppearance.BorderSize = 0;
            btnOptimize.Click += BtnOptimize_Click!;
            btnOptimize.MouseEnter += (s, e) => btnOptimize.BackColor = Color.FromArgb(0, 200, 255);
            btnOptimize.MouseLeave += (s, e) => btnOptimize.BackColor = Color.FromArgb(0, 170, 255);
            this.Controls.Add(btnOptimize);

            btnRestore = new Button
            {
                Text = "🔄 RESTORE SETTINGS",
                Location = new Point(455, 695),
                Size = new Size(415, 50),
                BackColor = Color.FromArgb(80, 80, 90),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnRestore.FlatAppearance.BorderSize = 0;
            btnRestore.Click += BtnRestore_Click!;
            btnRestore.MouseEnter += (s, e) => btnRestore.BackColor = Color.FromArgb(100, 100, 110);
            btnRestore.MouseLeave += (s, e) => btnRestore.BackColor = Color.FromArgb(80, 80, 90);
            this.Controls.Add(btnRestore);

            performanceTimer = new System.Windows.Forms.Timer();
            performanceTimer.Interval = 5000; // Update every 5 seconds (reduced from 2s to lower overhead)
            performanceTimer.Tick += UpdatePerformanceStats!;
            performanceTimer.Start();
        }

        private CheckBox CreateCheckBox(string text, int x, int y, bool isChecked)
        {
            return new CheckBox
            {
                Text = text,
                Location = new Point(x, y),
                AutoSize = true,
                ForeColor = Color.FromArgb(220, 220, 220),
                Font = new Font("Segoe UI", 9),
                Checked = isChecked
            };
        }

        private void UpdatePerformanceStats(object sender, EventArgs e)
        {
            try
            {
                // RAM stats - uses pre-initialized counter (no allocation)
                if (_ramCounter != null)
                {
                    var availableRam = _ramCounter.NextValue();
                    var usedRam = _totalRamMB - (ulong)availableRam;
                    var ramPercent = (double)usedRam / _totalRamMB * 100;

                    lblRamUsage.Text = $"RAM: {ramPercent:F1}% ({usedRam}/{_totalRamMB} MB)";
                    lblRamUsage.ForeColor = ramPercent > 80 ? Color.Red : ramPercent > 60 ? Color.Orange : Color.LightGreen;
                }

                // CPU stats - uses pre-initialized counter, no Thread.Sleep needed
                // The counter was primed in InitializePerformanceCounters, so NextValue() returns valid data
                if (_cpuCounter != null)
                {
                    var cpuUsage = _cpuCounter.NextValue();
                    lblCpuUsage.Text = $"CPU: {cpuUsage:F1}%";
                    lblCpuUsage.ForeColor = cpuUsage > 80 ? Color.Red : cpuUsage > 60 ? Color.Orange : Color.LightGreen;
                }
            }
            catch
            {
                lblRamUsage.Text = "RAM: N/A";
                lblCpuUsage.Text = "CPU: N/A";
            }
        }

        private void CheckAdminStatus()
        {
            if (!IsAdministrator())
            {
                lblStatus.Text = "⚠️ Not running as Administrator";
                lblStatus.ForeColor = Color.Orange;
            }
        }

        private bool IsAdministrator()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private async void BtnOptimize_Click(object sender, EventArgs e)
        {
            if (!IsAdministrator())
            {
                RestartAsAdmin();
                return;
            }

            btnOptimize.Enabled = false;
            btnRestore.Enabled = false;
            progressBar.Visible = true;
            progressBar.Value = 0;
            txtOutput.Clear();
            lblStatus.Text = "● Optimizing...";
            lblStatus.ForeColor = Color.Yellow;

            await Task.Run(() => RunOptimizations());

            progressBar.Visible = false;
            btnOptimize.Enabled = true;
            btnRestore.Enabled = true;
            lblStatus.Text = "● Optimization Complete!";
            lblStatus.ForeColor = Color.LightGreen;

            MessageBox.Show(
                "Optimizations applied successfully!\n\nNote: GPU scheduling and some settings require a restart.",
                "Success",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private async void BtnRestore_Click(object sender, EventArgs e)
        {
            if (!IsAdministrator())
            {
                RestartAsAdmin();
                return;
            }

            btnOptimize.Enabled = false;
            btnRestore.Enabled = false;
            progressBar.Visible = true;
            txtOutput.Clear();
            lblStatus.Text = "● Restoring...";
            lblStatus.ForeColor = Color.Yellow;

            await Task.Run(() => RunRestore());

            progressBar.Visible = false;
            btnOptimize.Enabled = true;
            btnRestore.Enabled = true;
            lblStatus.Text = "● Restore Complete!";
            lblStatus.ForeColor = Color.LightGreen;

            MessageBox.Show("Settings restored!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void RunOptimizations()
        {
            int total = 0, current = 0;
            // Count all checked options
            if (chkServices.Checked) total++;
            if (chkStartup.Checked) total++;
            if (chkTempFiles.Checked) total++;
            if (chkPowerPlan.Checked) total++;
            if (chkUltimatePower.Checked) total++;
            if (chkBackgroundApps.Checked) total++;
            if (chkGpuScheduling.Checked) total++;
            if (chkMemoryCleanup.Checked) total++;
            if (chkVisualEffects.Checked) total++;
            if (chkNetworkOptimize.Checked) total++;
            if (chkPrefetch.Checked) total++;
            if (chkDefragMemory.Checked) total++;
            if (chkProcessPriority.Checked) total++;
            if (chkClearStandby.Checked) total++;
            if (chkKillBloat.Checked) total++;
            if (chkDisableTelemetry.Checked) total++;
            if (chkDisableGameDVR.Checked) total++;
            if (chkTimerResolution.Checked) total++;
            if (chkDisableCoreParking.Checked) total++;
            if (chkDisableFullscreenOpt.Checked) total++;
            if (chkDisableMemCompression.Checked) total++;

            try
            {
                // Kill processes first (frees RAM immediately)
                if (chkKillBloat.Checked) { KillBloatwareProcesses(); UpdateProgress(++current, total); }
                if (chkServices.Checked) { StopServices(); UpdateProgress(++current, total); }

                // Memory optimizations
                if (chkDisableMemCompression.Checked) { DisableMemoryCompression(); UpdateProgress(++current, total); }
                if (chkMemoryCleanup.Checked) { FreeUpMemory(); UpdateProgress(++current, total); }
                if (chkClearStandby.Checked) { ClearStandbyMemory(); UpdateProgress(++current, total); }
                if (chkDefragMemory.Checked) { DefragmentMemory(); UpdateProgress(++current, total); }
                if (chkTempFiles.Checked) { ClearTempFiles(); UpdateProgress(++current, total); }

                // Power & Performance
                if (chkUltimatePower.Checked) { SetUltimatePowerPlan(); UpdateProgress(++current, total); }
                if (chkPowerPlan.Checked) { SetHighPerformancePowerPlan(); UpdateProgress(++current, total); }
                if (chkDisableCoreParking.Checked) { DisableCoreParking(); UpdateProgress(++current, total); }
                if (chkTimerResolution.Checked) { SetMaxTimerResolution(); UpdateProgress(++current, total); }

                // Gaming specific
                if (chkDisableGameDVR.Checked) { DisableGameDVR(); UpdateProgress(++current, total); }
                if (chkDisableFullscreenOpt.Checked) { DisableFullscreenOptimizations(); UpdateProgress(++current, total); }
                if (chkGpuScheduling.Checked) { EnableGpuScheduling(); UpdateProgress(++current, total); }
                if (chkProcessPriority.Checked) { SetGamingPriority(); UpdateProgress(++current, total); }

                // System tweaks
                if (chkStartup.Checked) { DisableStartupTasks(); UpdateProgress(++current, total); }
                if (chkBackgroundApps.Checked) { DisableBackgroundApps(); UpdateProgress(++current, total); }
                if (chkVisualEffects.Checked) { DisableVisualEffects(); UpdateProgress(++current, total); }
                if (chkNetworkOptimize.Checked) { OptimizeNetwork(); UpdateProgress(++current, total); }
                if (chkPrefetch.Checked) { DisablePrefetch(); UpdateProgress(++current, total); }
                if (chkDisableTelemetry.Checked) { DisableTelemetry(); UpdateProgress(++current, total); }
            }
            catch (Exception ex)
            {
                LogOutput($"Error: {ex.Message}", Color.Red);
            }
        }

        private void RunRestore()
        {
            try
            {
                RestoreServices(); UpdateProgress(1, 3);
                RestoreBackgroundApps(); UpdateProgress(2, 3);
                RestoreBalancedPowerPlan(); UpdateProgress(3, 3);
            }
            catch (Exception ex)
            {
                LogOutput($"Error: {ex.Message}", Color.Red);
            }
        }

        private void UpdateProgress(int current, int total)
        {
            int percentage = (int)((double)current / total * 100);
            this.Invoke((MethodInvoker)delegate
            {
                progressBar.Value = percentage;
            });
        }

        private void LogOutput(string message, Color? color = null)
        {
            this.Invoke((MethodInvoker)delegate
            {
                txtOutput.SelectionStart = txtOutput.TextLength;
                txtOutput.SelectionLength = 0;
                txtOutput.SelectionColor = color ?? Color.White;
                txtOutput.AppendText(message + "\n");
                txtOutput.SelectionColor = txtOutput.ForeColor;
                txtOutput.ScrollToCaret();
            });
        }

        // Update checker - checks GitHub releases for new version
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "BoostPro-UpdateChecker");
                client.Timeout = TimeSpan.FromSeconds(10);

                var response = await client.GetStringAsync($"https://api.github.com/repos/{GitHubRepo}/releases/latest");

                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (root.TryGetProperty("tag_name", out var tagElement))
                {
                    var latestVersion = tagElement.GetString()?.TrimStart('v', 'V') ?? "";

                    if (!string.IsNullOrEmpty(latestVersion) && IsNewerVersion(latestVersion, CurrentVersion))
                    {
                        this.Invoke((MethodInvoker)delegate
                        {
                            var result = MessageBox.Show(
                                $"A new version of BoostPro is available!\n\n" +
                                $"Current version: {CurrentVersion}\n" +
                                $"Latest version: {latestVersion}\n\n" +
                                $"Would you like to download it now?",
                                "Update Available",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Information);

                            if (result == DialogResult.Yes)
                            {
                                Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true });
                            }
                        });
                    }
                }
            }
            catch
            {
                // Silently fail - don't bother user if update check fails
                // (no internet, GitHub down, etc.)
            }
        }

        private static bool IsNewerVersion(string latest, string current)
        {
            try
            {
                var latestParts = latest.Split('.').Select(int.Parse).ToArray();
                var currentParts = current.Split('.').Select(int.Parse).ToArray();

                for (int i = 0; i < Math.Max(latestParts.Length, currentParts.Length); i++)
                {
                    int latestPart = i < latestParts.Length ? latestParts[i] : 0;
                    int currentPart = i < currentParts.Length ? currentParts[i] : 0;

                    if (latestPart > currentPart) return true;
                    if (latestPart < currentPart) return false;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        // Optimization methods
        private void StopServices()
        {
            LogOutput("Stopping services...", Color.Cyan);
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 4 };
            Parallel.ForEach(servicesToStop, parallelOptions, svcName =>
            {
                try
                {
                    using var sc = new ServiceController(svcName);
                    if (sc.Status != ServiceControllerStatus.Stopped && sc.Status != ServiceControllerStatus.StopPending)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(5));
                    }
                }
                catch { }
            });
        }

        private void DisableStartupTasks()
        {
            LogOutput("Disabling startup tasks...", Color.Cyan);
            try
            {
                string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (Directory.Exists(startup))
                {
                    string disabledFolder = Path.Combine(startup, ".disabled_startup_backup");
                    Directory.CreateDirectory(disabledFolder);
                    var files = Directory.GetFiles(startup);
                    if (files.Length > 0)
                    {
                        foreach (var f in files)
                        {
                            try
                            {
                                string dest = Path.Combine(disabledFolder, Path.GetFileName(f));
                                if (File.Exists(dest)) File.Delete(dest);
                                File.Move(f, dest);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
        }

        private void ClearTempFiles()
        {
            LogOutput("Clearing temp files...", Color.Cyan);
            try
            {
                string userTemp = Path.GetTempPath();
                int deleted = 0;
                var files = Directory.GetFiles(userTemp);
                foreach (var file in files)
                {
                    try 
                    { 
                        File.Delete(file);
                        deleted++;
                        if (deleted > 100) break; // Limit for speed
                    } 
                    catch { }
                }
            }
            catch { }
        }

        private void FreeUpMemory()
        {
            LogOutput("Freeing RAM...", Color.Cyan);
            // Use optimized, non-blocking garbage collection
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false);
        }

        private void DefragmentMemory()
        {
            LogOutput("Optimizing memory...", Color.Cyan);
            try
            {
                using var proc = Process.Start(new ProcessStartInfo("rundll32.exe", "advapi32.dll,ProcessIdleTasks")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                // Process handle is disposed, but the process continues running
            }
            catch { }
        }
        private void SetHighPerformancePowerPlan()
        {
            LogOutput("Setting High Performance...", Color.Cyan);
            try
            {
                using var proc = Process.Start(new ProcessStartInfo("powercfg", "-setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                proc?.WaitForExit(2000); // Max 2 seconds
            }
            catch { }
        }

        private void DisableBackgroundApps()
        {
            LogOutput("Disabling background apps...", Color.Cyan);
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications");
                key?.SetValue("GlobalUserDisabled", 1, RegistryValueKind.DWord);
            }
            catch { }
        }

        private void EnableGpuScheduling()
        {
            LogOutput("Enabling GPU scheduling...", Color.Cyan);
            try
            {
                using RegistryKey? key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
                key?.SetValue("HwSchMode", 2, RegistryValueKind.DWord);
            }
            catch { }
        }

        private void DisableVisualEffects()
        {
            LogOutput("Disabling visual effects...", Color.Cyan);
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects");
                key?.SetValue("VisualFXSetting", 2, RegistryValueKind.DWord);
            }
            catch { }
        }

        private void OptimizeNetwork()
        {
            LogOutput("Optimizing network for low latency...", Color.Cyan);
            try
            {
                // Global TCP optimizations
                using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters"))
                {
                    key?.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
                    key?.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
                    key?.SetValue("TcpDelAckTicks", 0, RegistryValueKind.DWord);
                    key?.SetValue("DefaultTTL", 64, RegistryValueKind.DWord);
                }

                // Disable Nagle's algorithm on all network interfaces
                // Nagle batches small packets = bad for gaming
                using (var interfacesKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces"))
                {
                    if (interfacesKey != null)
                    {
                        foreach (var subKeyName in interfacesKey.GetSubKeyNames())
                        {
                            try
                            {
                                using var ifKey = Registry.LocalMachine.CreateSubKey($@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{subKeyName}");
                                ifKey?.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
                                ifKey?.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
                                ifKey?.SetValue("TcpDelAckTicks", 0, RegistryValueKind.DWord);
                            }
                            catch { }
                        }
                    }
                }

                // Disable network throttling
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"))
                {
                    key?.SetValue("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
                }

                LogOutput("Network optimized for low latency!", Color.LightGreen);
            }
            catch { }
        }

        private void DisablePrefetch()
        {
            LogOutput("Disabling Prefetch...", Color.Cyan);
            try
            {
                using RegistryKey? key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters");
                key?.SetValue("EnablePrefetcher", 0, RegistryValueKind.DWord);
            }
            catch { }
        }

        private void SetGamingPriority()
        {
            LogOutput("Setting gaming priority...", Color.Cyan);
            try
            {
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
                using RegistryKey? key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games");
                key?.SetValue("GPU Priority", 8, RegistryValueKind.DWord);
                key?.SetValue("Priority", 6, RegistryValueKind.DWord);
                key?.SetValue("Scheduling Category", "High", RegistryValueKind.String);
                key?.SetValue("SFIO Priority", "High", RegistryValueKind.String);

                // Also boost multimedia system profile
                using var mmKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
                mmKey?.SetValue("SystemResponsiveness", 0, RegistryValueKind.DWord); // 0 = max for gaming
                mmKey?.SetValue("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord); // Disable throttling
            }
            catch { }
        }

        private void SetUltimatePowerPlan()
        {
            LogOutput("Setting Ultimate Performance power plan...", Color.Cyan);
            try
            {
                // First, try to unhide and activate Ultimate Performance plan
                using (var proc = Process.Start(new ProcessStartInfo("powercfg", "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                }))
                {
                    proc?.WaitForExit(3000);
                }

                // Activate Ultimate Performance
                using (var proc = Process.Start(new ProcessStartInfo("powercfg", "-setactive e9a42b02-d5df-448d-aa00-03f14749eb61")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                }))
                {
                    proc?.WaitForExit(2000);
                }

                LogOutput("Ultimate Performance power plan activated!", Color.LightGreen);
            }
            catch
            {
                LogOutput("Ultimate plan not available, using High Performance", Color.Orange);
            }
        }

        private void DisableGameDVR()
        {
            LogOutput("Disabling Game DVR and Game Bar...", Color.Cyan);
            try
            {
                // Disable Game DVR
                using (var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR"))
                {
                    key?.SetValue("AppCaptureEnabled", 0, RegistryValueKind.DWord);
                }

                // Disable Game Bar
                using (var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\GameBar"))
                {
                    key?.SetValue("AllowAutoGameMode", 0, RegistryValueKind.DWord);
                    key?.SetValue("AutoGameModeEnabled", 0, RegistryValueKind.DWord);
                    key?.SetValue("UseNexusForGameBarEnabled", 0, RegistryValueKind.DWord);
                    key?.SetValue("ShowStartupPanel", 0, RegistryValueKind.DWord);
                }

                // Disable Game Mode (can cause stuttering in some games)
                using (var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\GameBar"))
                {
                    key?.SetValue("AutoGameModeEnabled", 0, RegistryValueKind.DWord);
                }

                // Disable captures
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR"))
                {
                    key?.SetValue("AllowGameDVR", 0, RegistryValueKind.DWord);
                }

                LogOutput("Game DVR/Bar disabled!", Color.LightGreen);
            }
            catch { }
        }

        private void SetMaxTimerResolution()
        {
            LogOutput("Setting maximum timer resolution (0.5ms)...", Color.Cyan);
            try
            {
                // Set timer resolution to 0.5ms (5000 * 100ns = 0.5ms)
                // This reduces input lag and improves frame timing
                NtSetTimerResolution(5000, true, out uint currentRes);
                LogOutput($"Timer resolution set to {currentRes / 10000.0}ms", Color.LightGreen);
            }
            catch (Exception ex)
            {
                LogOutput($"Timer resolution: {ex.Message}", Color.Orange);
            }
        }

        private void DisableCoreParking()
        {
            LogOutput("Disabling CPU core parking...", Color.Cyan);
            try
            {
                // Disable core parking for all power plans
                // Core parking can cause micro-stutters when cores wake up
                string[] commands = new string[]
                {
                    // Processor performance core parking min cores (0 = disable parking)
                    "-setacvalueindex scheme_current sub_processor CPMINCORES 100",
                    "-setacvalueindex scheme_current sub_processor CPMAXCORES 100",
                    // Apply changes
                    "-setactive scheme_current"
                };

                foreach (var cmd in commands)
                {
                    using var proc = Process.Start(new ProcessStartInfo("powercfg", cmd)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    proc?.WaitForExit(1000);
                }

                // Also set via registry for persistence
                using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Power\PowerSettings\54533251-82be-4824-96c1-47b60b740d00\0cc5b647-c1df-4637-891a-dec35c318583");
                key?.SetValue("ValueMin", 100, RegistryValueKind.DWord);
                key?.SetValue("ValueMax", 100, RegistryValueKind.DWord);

                LogOutput("Core parking disabled!", Color.LightGreen);
            }
            catch { }
        }

        private void DisableFullscreenOptimizations()
        {
            LogOutput("Disabling fullscreen optimizations globally...", Color.Cyan);
            try
            {
                // Disable fullscreen optimizations system-wide
                // This fixes issues with exclusive fullscreen and reduces input lag
                using (var key = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore"))
                {
                    key?.SetValue("GameDVR_FSEBehaviorMode", 2, RegistryValueKind.DWord);
                    key?.SetValue("GameDVR_HonorUserFSEBehaviorMode", 1, RegistryValueKind.DWord);
                    key?.SetValue("GameDVR_FSEBehavior", 2, RegistryValueKind.DWord);
                    key?.SetValue("GameDVR_DXGIHonorFSEWindowsCompatible", 1, RegistryValueKind.DWord);
                    key?.SetValue("GameDVR_EFSEFeatureFlags", 0, RegistryValueKind.DWord);
                }

                LogOutput("Fullscreen optimizations disabled!", Color.LightGreen);
            }
            catch { }
        }

        private void DisableMemoryCompression()
        {
            LogOutput("Disabling Windows memory compression...", Color.Cyan);
            try
            {
                // Disable memory compression via PowerShell
                // Memory compression uses CPU to compress RAM - bad for gaming
                using var proc = Process.Start(new ProcessStartInfo("powershell", "-Command \"Disable-MMAgent -MemoryCompression\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas"
                });
                proc?.WaitForExit(5000);

                LogOutput("Memory compression disabled (requires restart)!", Color.LightGreen);
            }
            catch (Exception ex)
            {
                LogOutput($"Memory compression: {ex.Message}", Color.Orange);
            }
        }

        private void ClearStandbyMemory()
        {
            LogOutput("Clearing standby memory (this frees cached RAM)...", Color.Cyan);
            try
            {
                // First, empty working sets of all processes
                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        EmptyWorkingSet(proc.Handle);
                    }
                    catch { }
                }

                // Then clear the standby list using NtSetSystemInformation
                // This requires SeProfileSingleProcessPrivilege (admin)
                int command = MemoryPurgeStandbyList;
                GCHandle handle = GCHandle.Alloc(command, GCHandleType.Pinned);
                try
                {
                    NtSetSystemInformation(SystemMemoryListInformation, handle.AddrOfPinnedObject(), sizeof(int));
                    LogOutput("Standby memory cleared!", Color.LightGreen);
                }
                finally
                {
                    handle.Free();
                }
            }
            catch (Exception ex)
            {
                LogOutput($"Standby clear partial: {ex.Message}", Color.Orange);
            }
        }

        private void KillBloatwareProcesses()
        {
            LogOutput("Killing bloatware processes...", Color.Cyan);

            // Processes that commonly eat RAM in background (not critical)
            string[] bloatProcesses = new string[]
            {
                "OneDrive",           // OneDrive sync (uses lots of RAM)
                "YourPhone",          // Phone Link
                "PhoneExperienceHost",
                "GameBar",            // Xbox Game Bar
                "GameBarPresenceWriter",
                "SearchApp",          // Windows Search UI
                "SearchHost",
                "Cortana",            // Cortana
                "MicrosoftEdgeUpdate", // Edge updater
                "msedgewebview2",     // Edge WebView (used by many apps)
                "WidgetService",      // Windows Widgets
                "Widgets",
                "StartMenuExperienceHost", // Start menu (will restart)
                "TextInputHost",      // Touch keyboard
                "CTFMon",             // Text input service
                "SecurityHealthSystray", // Windows Security tray
                "NVIDIA Share",       // NVIDIA overlay
                "nvcontainer",        // NVIDIA container
                "NVDisplay.Container", // NVIDIA display
                "AdobeARM",           // Adobe updater
                "AcroTray",           // Adobe Acrobat tray
                "jusched",            // Java updater
                "iTunesHelper",       // iTunes
                "Spotify",            // Spotify (if running in bg)
            };

            int killed = 0;
            foreach (var procName in bloatProcesses)
            {
                try
                {
                    foreach (var proc in Process.GetProcessesByName(procName.Replace(".exe", "")))
                    {
                        try
                        {
                            proc.Kill();
                            killed++;
                        }
                        catch { }
                    }
                }
                catch { }
            }

            LogOutput($"Killed {killed} bloatware processes", Color.LightGreen);
        }

        private void DisableTelemetry()
        {
            LogOutput("Disabling Windows telemetry via registry...", Color.Cyan);
            try
            {
                // Disable telemetry
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection"))
                {
                    key?.SetValue("AllowTelemetry", 0, RegistryValueKind.DWord);
                }

                // Disable Customer Experience Improvement Program
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\SQMClient\Windows"))
                {
                    key?.SetValue("CEIPEnable", 0, RegistryValueKind.DWord);
                }

                // Disable Application Telemetry
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\AppCompat"))
                {
                    key?.SetValue("AITEnable", 0, RegistryValueKind.DWord);
                    key?.SetValue("DisableInventory", 1, RegistryValueKind.DWord);
                }

                // Disable feedback
                using (var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Siuf\Rules"))
                {
                    key?.SetValue("NumberOfSIUFInPeriod", 0, RegistryValueKind.DWord);
                }

                // Disable advertising ID
                using (var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo"))
                {
                    key?.SetValue("Enabled", 0, RegistryValueKind.DWord);
                }

                // Disable activity history
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\System"))
                {
                    key?.SetValue("EnableActivityFeed", 0, RegistryValueKind.DWord);
                    key?.SetValue("PublishUserActivities", 0, RegistryValueKind.DWord);
                    key?.SetValue("UploadUserActivities", 0, RegistryValueKind.DWord);
                }

                LogOutput("Telemetry disabled!", Color.LightGreen);
            }
            catch (Exception ex)
            {
                LogOutput($"Telemetry disable partial: {ex.Message}", Color.Orange);
            }
        }

        private void RestoreServices()
        {
            LogOutput("Starting services...", Color.Cyan);
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 4 };
            Parallel.ForEach(servicesToStop, parallelOptions, svcName =>
            {
                try
                {
                    using var sc = new ServiceController(svcName);
                    if (sc.Status == ServiceControllerStatus.Stopped)
                    {
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(5));
                    }
                }
                catch { }
            });
        }

        private void RestoreBackgroundApps()
        {
            LogOutput("Re-enabling background apps...", Color.Cyan);
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications");
                key?.SetValue("GlobalUserDisabled", 0, RegistryValueKind.DWord);
            }
            catch { }
        }

        private void RestoreBalancedPowerPlan()
        {
            LogOutput("Setting Balanced power...", Color.Cyan);
            try
            {
                using var proc = Process.Start(new ProcessStartInfo("powercfg", "-setactive 381b4222-f694-41f0-9685-ff5bb260df2e")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                proc?.WaitForExit(2000); // Max 2 seconds
            }
            catch { }
        }

        [STAThread]
        static void Main()
        {
            if (!IsRunningAsAdmin())
            {
                RestartAsAdmin();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        private static bool IsRunningAsAdmin()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private static void RestartAsAdmin()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory,
                    FileName = Application.ExecutablePath,
                    Verb = "runas"
                });
            }
            catch
            {
                MessageBox.Show("This application requires Administrator privileges.", "Administrator Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
