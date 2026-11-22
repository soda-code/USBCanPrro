using System;
using System.Windows.Forms;
using CanUSB.UI;

namespace CanUSB
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }
}