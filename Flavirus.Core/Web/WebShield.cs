namespace Flavirus.Core.Web;

public class WebShield
{
    private readonly HashSet<string> _blockedDomains = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _blockedUrls = new(StringComparer.OrdinalIgnoreCase);

    public void LoadFromList(IEnumerable<string> patterns)
    {
        foreach (var p in patterns)
        {
            var pattern = p.Trim();
            if (pattern.Length == 0)
                continue;

            if (pattern.Contains('/'))
                _blockedUrls.Add(pattern);
            else
                _blockedDomains.Add(pattern.TrimEnd('.'));
        }
    }

    public bool IsBlocked(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        var host = uri.IdnHost.TrimEnd('.');
        if (_blockedDomains.Any(domain =>
                host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        foreach (var b in _blockedUrls)
        {
            if (Uri.TryCreate(b, UriKind.Absolute, out var blockedUri))
            {
                if (!uri.Scheme.Equals(blockedUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
                    !host.Equals(blockedUri.IdnHost.TrimEnd('.'), StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(blockedUri.Query) &&
                     !uri.Query.Equals(blockedUri.Query, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var blockedPath = blockedUri.AbsolutePath.TrimEnd('/');
                var requestPath = uri.AbsolutePath.TrimEnd('/');
                if (blockedPath.Length == 0 ||
                    requestPath.Equals(blockedPath, StringComparison.OrdinalIgnoreCase) ||
                    requestPath.StartsWith(blockedPath + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
