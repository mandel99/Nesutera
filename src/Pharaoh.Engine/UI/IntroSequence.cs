using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Engine.UI;

public enum IntroVideoQuality
{
    Low,
    High
}

public readonly record struct IntroVideo(
    string RelativePath,
    UiRect Destination,
    bool CanSkipWithKeyboard,
    bool CanSkipWithLeftClick);

public enum LegacyMovieScreen
{
    Intro = 0x40,
    Credits = 0x41,
    Logo = 0x44
}

public static class IntroSequence
{
    public const int VideoWidth = 480;
    public const int VideoHeight = 286;

    public static string GetSmackerPath(LegacyMovieScreen screen) => screen switch
    {
        LegacyMovieScreen.Intro => @"smk\intro.smk",
        LegacyMovieScreen.Credits => @"smk\credits.smk",
        LegacyMovieScreen.Logo => @"smk\logo.smk",
        _ => throw new ArgumentOutOfRangeException(nameof(screen), screen, "Unknown legacy movie screen.")
    };

    [OriginalAddress(0x00413580, "FUN_00413580")]
    public static IntroVideo Create(int screenWidth, int screenHeight, IntroVideoQuality quality)
    {
        if (screenWidth <= 0 || screenHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(screenWidth), "Screen dimensions must be positive.");

        int left = (screenWidth - VideoWidth) / 2;
        int top = (screenHeight - VideoHeight) / 2;
        string path = quality == IntroVideoQuality.High
            ? @"Binks\high\intro_big.bik"
            : @"Binks\low\intro.bik";

        return new IntroVideo(
            path,
            new UiRect(left, top, left + VideoWidth, top + VideoHeight),
            CanSkipWithKeyboard: true,
            CanSkipWithLeftClick: true);
    }
}
