using System.Diagnostics;
using Pharaoh.Engine.Installation;
using Pharaoh.Engine.ReverseEngineering;
using Pharaoh.Engine.UI;

namespace Pharaoh.Desktop.Legacy.Managers;

[Flags]
internal enum OptionChange
{
    None = 0,
    ApplyDisplay = 1,
    RefreshAudio = 2,
    RefreshDifficulty = 4,
    ClosePanel = 8
}

/// <summary>Owns the mutable options model and its Pharaoh.inf persistence boundary.</summary>
internal sealed class OptionManager
{
    private readonly string installationRoot;

    public OptionManager(string installationRoot)
    {
        this.installationRoot = installationRoot;
        Options = Load(installationRoot);
    }

    public LegacyCityOptions Options { get; }

    [OriginalAddress(0x00403320, "FUN_00403320")]
    public int AdjustGameSpeed(int delta)
    {
        Options.GameSpeed = Math.Clamp(Options.GameSpeed + delta, 10, 100);
        Save();
        return Options.GameSpeed;
    }

    [RewriteOnly("Applies one typed city-options control action and persists the resulting Pharaoh.inf values")]
    public OptionChange Activate(LegacyCitySettingsPage page, int control)
    {
        OptionChange change = page switch
        {
            LegacyCitySettingsPage.Display => ActivateDisplay(control),
            LegacyCitySettingsPage.Sound => ActivateSound(control),
            LegacyCitySettingsPage.Speed => ActivateSpeed(control),
            LegacyCitySettingsPage.PopupMessages => ActivatePopupMessages(control),
            LegacyCitySettingsPage.Difficulty => ActivateDifficulty(control),
            _ => OptionChange.None
        };
        Save();
        return change;
    }

    private OptionChange ActivateDisplay(int control)
    {
        if (control == 0) Options.FullScreen = !Options.FullScreen;
        else if (control is >= 1 and <= 3)
            (Options.DisplayWidth, Options.DisplayHeight) = control switch
            { 1 => (640, 480), 2 => (800, 600), _ => (1024, 768) };
        else return OptionChange.ClosePanel;
        return OptionChange.ApplyDisplay;
    }

    private OptionChange ActivateSound(int control)
    {
        if (control < 4)
        {
            if (control == 0) Options.MusicEnabled = !Options.MusicEnabled;
            else if (control == 1) Options.SpeechEnabled = !Options.SpeechEnabled;
            else if (control == 2) Options.EffectsEnabled = !Options.EffectsEnabled;
            else Options.CitySoundsEnabled = !Options.CitySoundsEnabled;
        }
        else if (control < 12)
            AdjustSoundVolume((control - 4) / 2, control % 2 == 0 ? -10 : 10);
        else return OptionChange.RefreshAudio | OptionChange.ClosePanel;
        return OptionChange.RefreshAudio;
    }

    private OptionChange ActivateSpeed(int control)
    {
        if (control == 0) Options.GameSpeed = Math.Max(10, Options.GameSpeed - 10);
        else if (control == 1) Options.GameSpeed = Math.Min(100, Options.GameSpeed + 10);
        else if (control == 2) Options.ScrollSpeed = Math.Max(0, Options.ScrollSpeed - 10);
        else if (control == 3) Options.ScrollSpeed = Math.Min(100, Options.ScrollSpeed + 10);
        else return OptionChange.ClosePanel;
        return OptionChange.None;
    }

    private OptionChange ActivatePopupMessages(int control)
    {
        if (control < 12)
        {
            Options.PopupMessageMask ^= 1 << (control + 1);
            return OptionChange.None;
        }
        return OptionChange.ClosePanel;
    }

    private OptionChange ActivateDifficulty(int control)
    {
        if (control == 0) Options.Difficulty = Math.Max(0, Options.Difficulty - 1);
        else if (control == 1) Options.Difficulty = Math.Min(4, Options.Difficulty + 1);
        return OptionChange.RefreshDifficulty;
    }

    [RewriteOnly("Updates the four volume globals rendered by FUN_00531ef0")]
    private void AdjustSoundVolume(int channel, int delta)
    {
        switch (channel)
        {
            case 0: Options.MusicVolume = Math.Clamp(Options.MusicVolume + delta, 0, 100); break;
            case 1: Options.SpeechVolume = Math.Clamp(Options.SpeechVolume + delta, 0, 100); break;
            case 2: Options.EffectsVolume = Math.Clamp(Options.EffectsVolume + delta, 0, 100); break;
            case 3: Options.CityVolume = Math.Clamp(Options.CityVolume + delta, 0, 100); break;
        }
    }

    [OriginalAddress(0x004147F0, "FUN_004147f0")]
    private static LegacyCityOptions Load(string root)
    {
        try
        {
            return LegacyGameSettingsStore.Load(root);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Debug.WriteLine($"Could not load Pharaoh.inf: {error.Message}");
            return LegacyGameSettingsStore.CreateDefaults();
        }
    }

    [OriginalAddress(0x00414750, "FUN_00414750")]
    public void Save()
    {
        try
        {
            LegacyGameSettingsStore.Save(installationRoot, Options);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Debug.WriteLine($"Could not save Pharaoh.inf: {error.Message}");
        }
    }
}
