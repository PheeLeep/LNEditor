using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.DirectoryServices.ActiveDirectory;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace LNEditor {
    public partial class MainForm : Form {

        #region Windows Natives

        /// <summary>
        /// Logs off the interactive user, shuts down the system, or shuts down and restarts the system.
        /// </summary>
        /// <param name="uFlags">The shuutdown type.</param>
        /// <param name="dwReason">The reason for initiating the shutdown.</param>
        /// <returns>If the function succeeds, the return value is true; Otherwise, false.</returns>
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

        /// <summary>
        /// Shuts down all processes running in the logon session of the process that called the 
        /// <see cref="ExitWindowsEx(uint, uint)"/> function. Then it logs the user off.
        /// </summary>
        private const uint EWX_LOGOFF = 0;

        /// <summary>
        /// Other issue.
        /// </summary>
        private const uint SHTDN_REASON_MAJOR_OTHER = 0;

        #endregion

        #region Variables

        /// <summary>
        /// A variable indicating whether the text of both the text and caption were changed.S
        /// </summary>
        private bool isEdited = false;

        /// <summary>
        /// A synchronization lock object.
        /// </summary>
        private readonly object _lock = new object();

        /// <summary>
        /// A string containing registry path pointing at the Winlogon key where
        /// 'LegalNoticeCaption' and 'LegalNoticeText' exists.
        /// </summary>
        private readonly string WinlogonPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";

        /// <summary>
        /// An <see cref="Exception"/> variable stores the last error occurred.
        /// </summary>
        private Exception StoredError = null;

        /// <summary>
        /// Determines whether the computer is on domain workstation or local.
        /// </summary>
        /// <remarks>
        /// NOTE: This is a 'cache-like' variable after invoking <see cref="IsInDomain"/> to compensate
        /// performance due to hang response while checking for domain workstation.
        /// </remarks>
        private bool isInDomainCacheRes = false;
        #endregion

        #region Form
        public MainForm() {
            InitializeComponent();
        }

        private void RTB_TextChanged(object sender, EventArgs e) {
            if (!isEdited) isEdited = true;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e) {
            lock (_lock) {
                if (isEdited) {
                    switch (MessageBox.Show(this, "Do you want to save or discard the changes?", "",
                                            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question)) {
                        case DialogResult.Yes:
                            ApplyToRegistryToolStripMenuItem_Click(sender, EventArgs.Empty);
                            break;
                        case DialogResult.Cancel:
                            e.Cancel = false;
                            return;
                    }
                }
            }
        }

        private void MainForm_Load(object sender, EventArgs e) {
            while (!IsHandleCreated)
                Thread.Sleep(100);

            if (!ReadRegistry()) {
                MessageBox.Show(null, $"Failed to read legal notice values.\n\nError: {StoredError.Message}.",
                                "Failed", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                Environment.Exit(StoredError.HResult);
                return;
            }

            IsInDomain();
            if (!IsAdmin() || isInDomainCacheRes) {

                UACErrorPanel.Show();
                LegalNoticeCaptionRTB.ReadOnly = true;
                LegalNoticeTextRTB.ReadOnly = true;

                if (isInDomainCacheRes)
                    label1.Text = "This computer is under domain workstation. Editing values are disabled.";
            }

            isEdited = false; // Reset 'isEdited' variable.
        }

        private void ApplyToRegistryToolStripMenuItem_Click(object sender, EventArgs e) {
            lock (_lock) {
                if (!isEdited) return;

                // Check if the either two rich textboxes are empty.
                if ((string.IsNullOrEmpty(LegalNoticeCaptionRTB.Text) || string.IsNullOrEmpty(LegalNoticeTextRTB.Text)) &&
                    MessageBox.Show(this, "The legal notice banner will not show if one or both of the textboxes are empty. " +
                                    "Continue?", "", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No)
                    return;

                if (!Save()) return;
                MessageBox.Show(this, "Changes has been saved to the registry.", "", MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
            }
        }

        private void RemoveLegalNoticeToolStripMenuItem_Click(object sender, EventArgs e) {
            lock (_lock) {
                if (!IsAdmin() || isInDomainCacheRes || !ReadRegistry(false)) return;
                if (MessageBox.Show(this, "This will remove text and caption values in order to remove legal notice from your computer. Continue?",
                                    "", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No)
                    return;

                ManipulateRTBEnables(false);
                LegalNoticeCaptionRTB.Clear();
                LegalNoticeTextRTB.Clear();
                isEdited = true;
                if (!Save()) return;
                MessageBox.Show(this, "Changes has been saved to the registry.", "", MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
            }
        }

        private void LogOffToolStripMenuItem_Click(object sender, EventArgs e) {
            lock (_lock) {
                if (MessageBox.Show(this, "Do you want to log off? Please save any work before you press Yes.", "",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
                    return;
                if (!Save()) return;
                if (!ExitWindowsEx(EWX_LOGOFF, SHTDN_REASON_MAJOR_OTHER)) {
                    StoredError = new Win32Exception(Marshal.GetLastWin32Error());
                    MessageBox.Show(this, $"Failed to initiate log off.\n\n({StoredError.Message})", "",
                                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                }
                Close();
            }
        }

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e) {
            Close();
        }

        private void InstructionToolStripMenuItem_Click(object sender, EventArgs e) {
            lock (_lock)
                new InstructionDialog().ShowDialog(this);
        }

        private void AboutToolStripMenuItem_Click(object sender, EventArgs e) {
            lock (_lock)
                new AboutDialog().ShowDialog(this);
        }

        #endregion

        #region Program Checks
        /// <summary>
        /// Checks whether the program is currently running as administrator.
        /// </summary>
        /// <returns>Returns true if the program is running as administrator.</returns>
        private bool IsAdmin() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        /// <summary>
        /// Checks whether the computer is in the domain workstation.
        /// The result will be cached on <see cref="isInDomainCacheRes"/>.
        /// </summary>
        private void IsInDomain() {
            try {
                _ = Domain.GetCurrentDomain(); // Use '_' as the object is unneccesary.
                isInDomainCacheRes = true;
            } catch {
                // This computer is on local machine. Skip.
                isInDomainCacheRes = false;
            }
        }

        #endregion

        #region Registry

        /// <summary>
        /// Retrieves the value from the specified registry.
        /// </summary>
        /// <param name="path">The path of the specified registry key.</param>
        /// <param name="name">The name of the registry value to be retrieved.</param>
        /// <param name="value">
        /// The value retrieved from the registry once succeed, or <see cref="string.Empty"/> if fails.
        /// </param>
        /// <returns>
        /// Returns true if succeed; Otherwise, false and the error will be stored to <see cref="StoredError"/>.
        /// </returns>
        private bool GetValueFromRegistry(string path, string name, out string value) {
            value = string.Empty;
            RegistryKey regKey = null;
            try {
                regKey = Registry.LocalMachine.OpenSubKey(path) ?? throw new InvalidOperationException("Registry was not opened");
                object obj = regKey.GetValue(name) ?? throw new InvalidOperationException("Couldn't acquire the value from the registry");
                value = obj.ToString();
                regKey.Close();
                return true;
            } catch (Exception ex) {
                StoredError = ex;
                return false;
            } finally {
                regKey?.Close();
            }
        }

        /// <summary>
        /// Modifies the specified registry value.
        /// </summary>
        /// <param name="path">The path of the specified registry key.</param>
        /// <param name="name">The name of the registry value to be modified.</param>
        /// <param name="value">The value to be set to the specified registry key.</param>
        /// <returns>Returns true if succeed; Otherwise, false.</returns>
        private bool SetValueToRegistry(string path, string name, string value) {
            RegistryKey regKey = null;
            try {
                regKey = Registry.LocalMachine.OpenSubKey(path, true) ?? throw new InvalidOperationException("Registry was not opened.");
                regKey.SetValue(name, value);
                regKey.Flush();
                return true;
            } catch (Exception ex) {
                StoredError = ex;
                return false;
            } finally {
                regKey?.Close();
            }
        }

        #endregion

        #region Functions

        /// <summary>
        /// Save the values to the registry.
        /// </summary>
        /// <returns>Returns true if succeed; Otherwise, false</returns>
        private bool Save() {
            lock (_lock) {
                ManipulateRTBEnables(false); // Disable the rich textboxes.

                if (!SetValueToRegistry(WinlogonPath, "LegalNoticeCaption", LegalNoticeCaptionRTB.Text) ||
                    !SetValueToRegistry(WinlogonPath, "LegalNoticeText", LegalNoticeTextRTB.Text)) {
                    MessageBox.Show(this, $"Unable to save legal notice values.\n\nError: {StoredError.Message}",
                                    "Failed", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                    ManipulateRTBEnables(); // Enable it back.
                    return false;
                }
                if (!ReadRegistry())
                    MessageBox.Show(null, $"Failed to read legal notice values.\n\nError: {StoredError.Message}.",
                                    "Failed", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                isEdited = false;
                ManipulateRTBEnables();
                return true;
            }
        }

        /// <summary>
        /// Reads the registry values of the legal notice's caption and text.
        /// </summary>
        /// <param name="updateRTBs">
        /// Determines whether the rich textboxes' values will be updated once the read succeed.
        /// </param>
        /// <returns>Returns true if succeed; Otherwise, false.</returns>
        private bool ReadRegistry(bool updateRTBs = true) {
            if (!GetValueFromRegistry(WinlogonPath, "LegalNoticeCaption", out string caption) ||
                !GetValueFromRegistry(WinlogonPath, "LegalNoticeText", out string text)) {
                return false;
            }
            if (updateRTBs) {
                LegalNoticeCaptionRTB.Text = caption;
                LegalNoticeTextRTB.Text = text;
            }
            return true;
        }

        /// <summary>
        /// Enables or disables the rich textboxes.
        /// </summary>
        /// <param name="IsEnabled">
        /// Determines whether the rich textboxes should be enable or not.
        /// </param>
        private void ManipulateRTBEnables(bool IsEnabled = true) {
            LegalNoticeCaptionRTB.Enabled = IsEnabled;
            LegalNoticeTextRTB.Enabled = IsEnabled;
        }

        #endregion
    }
}
