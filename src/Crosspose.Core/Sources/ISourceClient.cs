using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Crosspose.Core.Sources;

public interface ISourceClient
{
    string SourceName { get; }
    string SourceUrl { get; }
    ILogger Logger { get; }

    Task<SourceDetectionResult> DetectAsync(SourceAuth? auth = null, CancellationToken cancellationToken = default);
    Task<SourceAuthResult> AuthenticateAsync(SourceAuth? auth = null, CancellationToken cancellationToken = default);
    Task<SourceListResult> ListAsync(SourceAuth? auth = null, CancellationToken cancellationToken = default);
    Task<SourceVersionResult> ListVersionsAsync(string chartName, SourceAuth? auth = null, CancellationToken cancellationToken = default);
}

public record SourceAuth(string? Username, string? Password, string? BearerToken = null);

public record SourceDetectionResult(bool IsDetected, string? Message = null, bool RequiresAuth = false);

public record SourceAuthResult(bool IsAuthenticated, string? Message = null);

public record SourceListResult(bool IsSuccess, IReadOnlyList<SourceChart> Items, string? Message = null);

public record SourceVersionResult(bool IsSuccess, IReadOnlyList<SourceVersion> Versions, string? Message = null);

public record SourceChart(string Name, string? Description = null);

public record SourceVersion(string Tag, string? Digest = null, DateTimeOffset? CreatedAt = null);
