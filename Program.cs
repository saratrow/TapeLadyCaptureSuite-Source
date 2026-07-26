namespace TapeLadyCaptureSuite;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        ApplicationConfiguration.Initialize();

        Application.ThreadException += (_, e) =>
            MessageBox.Show(
                e.Exception.Message,
                "Tape Lady Capture Suite",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var message = e.ExceptionObject is Exception exception
                ? exception.ToString()
                : "An unexpected error occurred.";

            MessageBox.Show(
                message,
                "Tape Lady Capture Suite",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        };

        try
        {
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            var logFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TapeLady",
                "CaptureSuite");
            Directory.CreateDirectory(logFolder);
            File.WriteAllText(Path.Combine(logFolder, "startup-error.txt"), ex.ToString());

            MessageBox.Show(
                ex.ToString(),
                "Tape Lady Capture Suite — Startup Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
