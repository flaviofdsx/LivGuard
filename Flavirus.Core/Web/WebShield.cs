namespace Flavirus.Core.Web;

public class WebShield
{
    private readonly HashSet<string> _blockedDomains = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _blockedUrls = new(StringComparer.OrdinalIgnoreCase);

    public void LoadFromList(IEnumerable<string> patterns)
    {
        foreach (var p in patterns)
        {
            if (p.Contains('/')) _blockedUrls.Add(p);
            else _blockedDomains.Add(p);
        }
    }

    public bool IsBlocked(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (_blockedDomains.Contains(uri.Host)) return true;
        foreach (var b in _blockedUrls)
            if (url.StartsWith(b, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
