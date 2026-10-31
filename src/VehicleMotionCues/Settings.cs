using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VehicleMotionCues;

public sealed class Settings
{
    public bool ShowCues { get; set; } = true;

    /// <summary>"Automatic" shows dots only while the vehicle is moving (like iOS); "Always" keeps them up.</summary>
    public string DisplayMode { get; set; } = "Automatic";
    public bool LargerDots { get; set; }
    public bool MoreDots { get; set; }
    public string Sensitivity { get; set; } = "Medium";
    public bool FlipLeftRight { get; set; }
    public bool FlipForwardBack { get; set; }
    public int Port { get; set; } = 47800;

    /// <summary>Random token embedded in the phone URL so other devices on the network can't inject motion.</summary>
    public string PairingKey { get; set; } = "";

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VehicleMotionCues");

    static string FilePath => Path.Combine(Directory, "settings.json");

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Dot-field acceleration in DIPs/s² per m/s² of vehicle acceleration. At Medium, an ordinary
    /// 2 m/s² turn streams the dots at roughly 150 DIPs/s.
    /// </summary>
    [JsonIgnore]
    public double Gain => Sensitivity switch
    {
        "Low" => 65,
        "High" => 160,
        _ => 105,
    };

    [JsonIgnore]
    public bool IsFirstRun { get; private set; }

    public static Settings Load()
    {
        Settings settings;
        try
        {
            settings = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOptions) ?? new Settings()
                : new Settings { IsFirstRun = true };
        }
        catch (Exception)
        {
            settings = new Settings();
        }

        if (string.IsNullOrEmpty(settings.PairingKey))
        {
            settings.PairingKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            settings.Save();
        }
        return settings;
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception)
        {
            // Settings are a convenience; failing to persist them must not take the overlay down.
        }
    }
}
