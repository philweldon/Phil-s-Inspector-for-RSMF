using System.Globalization;

namespace Rsmf.Core;

/// <summary>
/// Runs a block under the invariant culture so reports, folder names and messages are identical
/// whatever the PC's regional settings are. Restores the previous culture on dispose.
/// </summary>
public readonly struct InvariantScope : IDisposable
{
    private readonly CultureInfo _culture;
    private readonly CultureInfo _uiCulture;

    private InvariantScope(CultureInfo culture, CultureInfo uiCulture)
    {
        _culture = culture;
        _uiCulture = uiCulture;
    }

    public static InvariantScope Enter()
    {
        var scope = new InvariantScope(CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        return scope;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}
