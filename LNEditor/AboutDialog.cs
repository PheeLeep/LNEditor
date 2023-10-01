using System.Diagnostics;
using System.Windows.Forms;

namespace LNEditor {
    public partial class AboutDialog : Form {
        public AboutDialog() {
            InitializeComponent();
        }

        private void PheeleepLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) {
            string repoLink = "https://github.com/PheeLeep";
            Process.Start(repoLink);
        }

        private void SourceCodeLinkLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) {
            string repoLink = "https://github.com/PheeLeep/LNEditor";
            Process.Start(repoLink);
        }
    }
}
