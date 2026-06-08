using System.Drawing;
using System.Windows.Forms;
using SplitPlay.TestTarget;

// Entry point. Parses the launch arguments and shows a single test window.
Application.EnableVisualStyles();
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
Application.SetCompatibleTextRenderingDefault(false);

var options = TestWindowOptions.Parse(args);
Application.Run(new TestWindowForm(options));
