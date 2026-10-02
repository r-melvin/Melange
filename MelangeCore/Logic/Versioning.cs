using System;

namespace Melange.Core
{
    /// <summary>The hub's compatibility rule: the same major version, and at least the minor a spoke was built against.</summary>
    public static class Versioning
    {
        public static bool IsCompatible(Version hub, Version builtAgainst, out string problem)
        {
            problem = null;
            if (hub.Major == builtAgainst.Major && hub.Minor >= builtAgainst.Minor) return true;
            problem = hub.Major != builtAgainst.Major
                ? $"needs Melange Core {builtAgainst.Major}.x, but {hub} is installed"
                : $"needs Melange Core {builtAgainst} or newer, but {hub} is installed";
            return false;
        }
    }
}
