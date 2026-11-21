// ================================================================================
// FILE: Analysis/ColorContrastEngine.cs
// PROJECT: AccessibilityStudio.Core
// LICENSE: MIT License
// ================================================================================
// PLAIN-ENGLISH OVERVIEW (100-LEVEL COLOR SCIENCE & WCAG 1.4.3 / 1.4.6 MATH GUIDE):
//   Why can't we just subtract RGB numbers to measure text readability?
//     Human eyes are far more sensitive to green light (`71.52%`) than red (`21.26%`)
//     or blue (`7.22%`), AND computer monitors apply a non-linear sRGB gamma curve.
//
//   How W3C / WCAG 2.1 & 2.2 Relative Luminance Works:
//     1. Convert each 8-bit channel (`0..255`) into `0.0..1.0` (`c = channel / 255.0`).
//     2. Linearize sRGB gamma:
//          if `c <= 0.04045` -> `c / 12.92`
//          else              -> `Math.Pow((c + 0.055) / 1.055, 2.4)`
//     3. Weight by human photopic vision:
//          `L = 0.2126 * R_lin + 0.7152 * G_lin + 0.0722 * B_lin`
//     4. Compute Contrast Ratio (`1.0:1` minimum up to `21.0:1` pure black on white):
//          `Ratio = (L_lighter + 0.05) / (L_darker + 0.05)`
//
//   What this class provides:
//     - Parses CSS hex (`#rgb`, `#rrggbb`), `rgb(r, g, b)`, and common named CSS colors.
//     - Computes exact WCAG contrast ratios and AA/AAA pass/fail verdicts.
//     - Includes `SuggestNearestPassingHex(...)`—which darkens or lightens a failing
//       foreground color in tiny binary-search steps until it hits `>= 4.5:1` (or `7.0:1`)
//       while keeping the designer's original hue!
// ================================================================================

using System.Globalization;
using System.Text.RegularExpressions;

namespace AccessibilityStudio.Core.Analysis;

/// <summary>
/// Immutable 24-bit sRGB color value (`R`, `G`, `B` in `0..255`).
/// </summary>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    /// <summary>Formats this color as an uppercase 6-digit CSS hex string (e.g. `"#0F6CBD"`).</summary>
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// Full WCAG 2.1 / 2.2 contrast evaluation result returned to the Contrast Workbench
/// and CSS Contrast Analyzer.
/// </summary>
public sealed record ContrastEvaluation(
    RgbColor Foreground,
    RgbColor Background,
    double Ratio,
    bool PassesNormalAA,
    bool PassesNormalAAA,
    bool PassesLargeTextOrUiAA,
    bool PassesLargeTextAAA
);

public static partial class ColorContrastEngine
{
    // Standard W3C CSS Color Module Level 4 named colors most frequently encountered in stylesheets.
    private static readonly Dictionary<string, RgbColor> NamedCssColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = new(0, 0, 0),
        ["white"] = new(255, 255, 255),
        ["red"] = new(255, 0, 0),
        ["green"] = new(0, 128, 0),
        ["blue"] = new(0, 0, 255),
        ["yellow"] = new(255, 255, 0),
        ["gray"] = new(128, 128, 128),
        ["grey"] = new(128, 128, 128),
        ["darkgray"] = new(169, 169, 169),
        ["lightgray"] = new(211, 211, 211),
        ["silver"] = new(192, 192, 192),
        ["navy"] = new(0, 0, 128),
        ["teal"] = new(0, 128, 128),
        ["maroon"] = new(128, 0, 0),
        ["purple"] = new(128, 0, 128),
        ["orange"] = new(255, 165, 0),
        ["rebeccapurple"] = new(102, 51, 153)
    };

    [GeneratedRegex(@"^rgba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})(?:\s*,\s*1(?:\.0+)?)?\s*\)$", RegexOptions.IgnoreCase)]
    private static partial Regex RgbFuncRegex();

    /// <summary>
    /// Attempts to parse a CSS color token (`#RGB`, `#RRGGBB`, `rgb(r,g,b)`, or named color)
    /// into an opaque <see cref="RgbColor"/>. Semi-transparent `rgba(..., 0.5)` returns `false`
    /// because static CSS analysis cannot know what backdrop image sits behind it without DOM rendering.
    /// </summary>
    public static bool TryParseCssColor(string rawToken, out RgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return false;
        }

        // Strip trailing `!important` if present in a CSS declaration value
        string token = rawToken.Trim();
        int impIdx = token.IndexOf("!important", StringComparison.OrdinalIgnoreCase);
        if (impIdx >= 0)
        {
            token = token[..impIdx].Trim();
        }

        if (NamedCssColors.TryGetValue(token, out color))
        {
            return true;
        }

        if (token.StartsWith('#'))
        {
            ReadOnlySpan<char> hex = token.AsSpan(1);
            // Expand 3-digit shorthand `#abc` -> `#aabbcc`
            if (hex.Length == 3 &&
                byte.TryParse([hex[0], hex[0]], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r3) &&
                byte.TryParse([hex[1], hex[1]], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g3) &&
                byte.TryParse([hex[2], hex[2]], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b3))
            {
                color = new RgbColor(r3, g3, b3);
                return true;
            }

            if (hex.Length == 6 &&
                byte.TryParse(hex[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r6) &&
                byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g6) &&
                byte.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b6))
            {
                color = new RgbColor(r6, g6, b6);
                return true;
            }

            return false;
        }

        Match m = RgbFuncRegex().Match(token);
        if (m.Success &&
            int.TryParse(m.Groups[1].Value, out int r) &&
            int.TryParse(m.Groups[2].Value, out int g) &&
            int.TryParse(m.Groups[3].Value, out int b) &&
            r is >= 0 and <= 255 &&
            g is >= 0 and <= 255 &&
            b is >= 0 and <= 255)
        {
            color = new RgbColor((byte)r, (byte)g, (byte)b);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Computes W3C WCAG 2.x Relative Luminance (`0.0` pure black to `1.0` pure white).
    /// </summary>
    public static double GetRelativeLuminance(RgbColor c)
    {
        static double Linearize(byte channel)
        {
            double s = channel / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Linearize(c.R)) +
               (0.7152 * Linearize(c.G)) +
               (0.0722 * Linearize(c.B));
    }

    /// <summary>
    /// Computes the exact WCAG contrast ratio (`1.0` to `21.0`) between two opaque colors.
    /// </summary>
    public static double GetContrastRatio(RgbColor fg, RgbColor bg)
    {
        double l1 = GetRelativeLuminance(fg);
        double l2 = GetRelativeLuminance(bg);
        double lighter = Math.Max(l1, l2);
        double darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// Evaluates a foreground/background pair against all four WCAG 2.1/2.2 contrast thresholds:
    ///   - Normal Text AA  : `>= 4.5:1` (WCAG 1.4.3)
    ///   - Normal Text AAA : `>= 7.0:1` (WCAG 1.4.6)
    ///   - Large Text / UI : `&gt;= 3.0:1` (WCAG 1.4.3 Large &amp; 1.4.11 Non-text Contrast)
    ///   - Large Text AAA  : `>= 4.5:1` (WCAG 1.4.6 Large)
    /// </summary>
    public static ContrastEvaluation Evaluate(RgbColor fg, RgbColor bg)
    {
        double ratio = GetContrastRatio(fg, bg);
        double rounded = Math.Round(ratio, 2);
        return new ContrastEvaluation(
            Foreground: fg,
            Background: bg,
            Ratio: rounded,
            PassesNormalAA: ratio >= 4.5,
            PassesNormalAAA: ratio >= 7.0,
            PassesLargeTextOrUiAA: ratio >= 3.0,
            PassesLargeTextAAA: ratio >= 4.5
        );
    }

    /// <summary>
    /// Suggests the closest foreground hex color that achieves at least <paramref name="targetRatio"/>
    /// (default `4.55:1` for comfortable AA safety margin) against <paramref name="background"/>
    /// by interpolating toward either pure black (`#000000`) or pure white (`#FFFFFF`)—whichever
    /// direction is capable of meeting the target ratio while changing the color as little as possible!
    /// </summary>
    public static RgbColor SuggestNearestPassingForeground(
        RgbColor originalFg,
        RgbColor background,
        double targetRatio = 4.55)
    {
        if (GetContrastRatio(originalFg, background) >= targetRatio)
        {
            return originalFg;
        }

        RgbColor black = new(0, 0, 0);
        RgbColor white = new(255, 255, 255);

        bool canPassTowardBlack = GetContrastRatio(black, background) >= targetRatio;
        bool canPassTowardWhite = GetContrastRatio(white, background) >= targetRatio;

        RgbColor? darkCandidate = canPassTowardBlack ? BinarySearchMix(originalFg, black, background, targetRatio) : null;
        RgbColor? lightCandidate = canPassTowardWhite ? BinarySearchMix(originalFg, white, background, targetRatio) : null;

        if (darkCandidate.HasValue && lightCandidate.HasValue)
        {
            // Pick whichever candidate stays closer in Euclidean RGB distance to the designer's original color!
            return ColorDistanceSq(originalFg, darkCandidate.Value) <= ColorDistanceSq(originalFg, lightCandidate.Value)
                ? darkCandidate.Value
                : lightCandidate.Value;
        }

        if (darkCandidate.HasValue) return darkCandidate.Value;
        if (lightCandidate.HasValue) return lightCandidate.Value;

        // Fallback if targetRatio > max achievable (e.g. AAA 7:1 on mid-gray): pick higher-contrast pole
        return GetContrastRatio(black, background) >= GetContrastRatio(white, background) ? black : white;
    }

    private static RgbColor BinarySearchMix(RgbColor start, RgbColor pole, RgbColor bg, double targetRatio)
    {
        double lo = 0.0;
        double hi = 1.0;
        RgbColor best = pole;

        // 20 binary-search iterations give sub-1/255 channel precision!
        for (int i = 0; i < 20; i++)
        {
            double mid = (lo + hi) * 0.5;
            RgbColor candidate = new(
                (byte)Math.Clamp((int)Math.Round(start.R + ((pole.R - start.R) * mid)), 0, 255),
                (byte)Math.Clamp((int)Math.Round(start.G + ((pole.G - start.G) * mid)), 0, 255),
                (byte)Math.Clamp((int)Math.Round(start.B + ((pole.B - start.B) * mid)), 0, 255)
            );

            if (GetContrastRatio(candidate, bg) >= targetRatio)
            {
                best = candidate;
                hi = mid;
            }
            else
            {
                lo = mid;
            }
        }

        return best;
    }

    private static int ColorDistanceSq(RgbColor a, RgbColor b)
    {
        int dr = a.R - b.R;
        int dg = a.G - b.G;
        int db = a.B - b.B;
        return (dr * dr) + (dg * dg) + (db * db);
    }
}
