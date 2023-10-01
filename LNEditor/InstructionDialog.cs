using System;
using System.Threading;
using System.Windows.Forms;

namespace LNEditor {
    public partial class InstructionDialog : Form {
        public InstructionDialog() {
            InitializeComponent();
        }

        private void InstructionDialog_Load(object sender, EventArgs e) {
            while (!IsHandleCreated)
                Thread.Sleep(10);

            richTextBox1.Text = Properties.Resources.InstructionText;
        }
    }
}
