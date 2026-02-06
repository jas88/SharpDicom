using System;
using System.Diagnostics.CodeAnalysis;

namespace SharpDicom.Cli.Helpers;

/// <summary>
/// Parses PACS connection strings of the form <c>pacs://AET@host:port</c>.
/// </summary>
internal static class ConnectionStringParser
{
    /// <summary>
    /// Attempt to parse a connection string.
    /// </summary>
    /// <param name="connectionString">The connection string to parse.</param>
    /// <param name="host">The parsed host name.</param>
    /// <param name="port">The parsed port (default 104).</param>
    /// <param name="calledAe">The parsed Called AE Title.</param>
    /// <returns><c>true</c> if parsing succeeded; otherwise <c>false</c>.</returns>
    public static bool TryParse(
        string connectionString,
#if !NETSTANDARD2_0
        [NotNullWhen(true)]
#endif
        out string? host,
        out int port,
#if !NETSTANDARD2_0
        [NotNullWhen(true)]
#endif
        out string? calledAe)
    {
        host = null;
        port = 104;
        calledAe = null;

        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        // Expect pacs:// prefix
        const string prefix = "pacs://";
        if (!connectionString.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var remainder = connectionString.Substring(prefix.Length);
        if (string.IsNullOrEmpty(remainder))
            return false;

        // Split AET@host:port
        var atIndex = remainder.IndexOf('@');
        if (atIndex <= 0)
            return false;

        calledAe = remainder.Substring(0, atIndex);
        var hostPort = remainder.Substring(atIndex + 1);

        if (string.IsNullOrEmpty(hostPort))
            return false;

        var colonIndex = hostPort.LastIndexOf(':');
        if (colonIndex < 0)
        {
            // No port specified, use default
            host = hostPort;
            port = 104;
        }
        else
        {
            host = hostPort.Substring(0, colonIndex);
            var portStr = hostPort.Substring(colonIndex + 1);
            if (!int.TryParse(portStr, out port) || port < 1 || port > 65535)
                return false;
        }

        return !string.IsNullOrEmpty(host);
    }
}
