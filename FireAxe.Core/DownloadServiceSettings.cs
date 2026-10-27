using System;
using System.Net;

namespace FireAxe;

public class DownloadServiceSettings
{
    public IWebProxy? Proxy { get; init; } = null;

    /// <summary>
    /// Whether each download is split into multiple parallel chunks.
    /// </summary>
    public bool IsParallelDownloadEnabled { get; init; } = true;

    /// <summary>
    /// The number of parallel chunks per download when <see cref="IsParallelDownloadEnabled"/> is on.
    /// </summary>
    public int ChunkCount { get; init; } = 4;

    /// <summary>
    /// The maximum number of downloads that run at the same time.
    /// </summary>
    public int MaxConcurrentDownloads { get; init; } = 3;
}