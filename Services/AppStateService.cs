using System.Text.Json;
using TapeLadyCaptureSuite.Models;

namespace TapeLadyCaptureSuite.Services;

internal static class AppStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string StateFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TapeLady",
        "CaptureSuite");

    private static string StatePath => Path.Combine(StateFolder, "state.json");

    public static AppState Load()
    {
        try
        {
            if (!File.Exists(StatePath))
            {
                return new AppState();
            }

            return JsonSerializer.Deserialize<AppState>(File.ReadAllText(StatePath), JsonOptions)
                   ?? new AppState();
        }
        catch
        {
            return new AppState();
        }
    }

    public static void Save(AppState state)
    {
        Directory.CreateDirectory(StateFolder);
        var temporaryPath = StatePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temporaryPath, StatePath, true);
    }
}
