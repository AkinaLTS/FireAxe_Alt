using System;
using System.Collections.Frozen;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace FireAxe;

public static class PublishedFileUtils
{
    private static readonly Regex s_publishedFileIdLinkRegex = new(@"steamcommunity\.com/(?:sharedfiles|workshop)/filedetails/\?.*id=(\d+)");

    private static readonly FrozenSet<string> s_imageFileExtensions = FrozenSet.ToFrozenSet(
        [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp"], StringComparer.OrdinalIgnoreCase);

    // A workshop collection (or any entry whose payload is an image rather than a downloadable file)
    // is not a valid addon target. Collections return a preview image url and file name here.
    private static bool IsCollectionOrImage(PublishedFileDetails details)
    {
        if (details.FileUrl.Length == 0)
        {
            return false;
        }

        var fileNameExtension = Path.GetExtension(details.FileName);
        if (fileNameExtension.Length > 0 && s_imageFileExtensions.Contains(fileNameExtension))
        {
            return true;
        }

        return Uri.TryCreate(details.FileUrl, UriKind.Absolute, out var uri)
            && uri.Host.StartsWith("images.", StringComparison.OrdinalIgnoreCase)
            && uri.Host.EndsWith(".steamusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<GetPublishedFileDetailsResult> GetPublishedFileDetailsAsync(ulong publishedFileId, HttpClient httpClient, CancellationToken cancellationToken)
    {
        PublishedFileDetails? content = null;
        PublishedFileDetails? collectionOrImageContent = null;
        GetPublishedFileDetailsResultStatus status = GetPublishedFileDetailsResultStatus.Failed;

        try
        {
            using var postContent = new StringContent($"itemcount=1&publishedfileids[0]={publishedFileId}", Encoding.UTF8, "application/x-www-form-urlencoded");
            using var response = await httpClient.PostAsync("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", postContent, cancellationToken).ConfigureAwait(false);
            var responseContentStr = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var json = JObject.Parse(responseContentStr);
            if (json.TryGetValue("response", out var responseToken) && responseToken is JObject responseObj)
            {
                if (responseObj.TryGetValue("publishedfiledetails", out var detailsToken) && detailsToken is JArray detailsArray)
                {
                    if (detailsArray.Count == 1)
                    {
                        var elementToken = detailsArray[0];
                        if (elementToken is JObject element)
                        {
                            if (element.TryGetValue("result", out var resultTypeToken) && resultTypeToken.Type == JTokenType.Integer)
                            {
                                int resultType = (int)resultTypeToken;
                                if (resultType == 1)
                                {
                                    if (element.TryGetValue("consumer_app_id", out var consumerAppIdToken) && consumerAppIdToken.Type == JTokenType.Integer && (int)consumerAppIdToken == 550)
                                    {
                                        var details = element.ToObject<PublishedFileDetails>()!;
                                        if (IsCollectionOrImage(details))
                                        {
                                            status = GetPublishedFileDetailsResultStatus.InvalidPublishedFileId;
                                            // Keep the details so callers that only need metadata (e.g. a collection title)
                                            // can still use them, while the status marks it as not downloadable.
                                            collectionOrImageContent = details;
                                        }
                                        else
                                        {
                                            content = details;
                                        }
                                    }
                                    else
                                    {
                                        status = GetPublishedFileDetailsResultStatus.InvalidPublishedFileId;
                                    }

                                }
                                else if (resultType == 9)
                                {
                                    status = GetPublishedFileDetailsResultStatus.InvalidPublishedFileId;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, $"Exception occurred during {nameof(PublishedFileUtils)}.{nameof(GetPublishedFileDetailsAsync)}.");
        }

        if (content != null)
        {
            status = GetPublishedFileDetailsResultStatus.Succeeded;
        }
        return new GetPublishedFileDetailsResult(content ?? collectionOrImageContent, content != null, status);
    }

    public static bool TryParsePublishedFileId(string input, out ulong id)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (ulong.TryParse(input, out id))
        {
            return true;
        }

        if (TryParsePublishedFileIdLink(input, out id))
        {
            return true;
        }

        return false;
    }

    public static bool TryParsePublishedFileIdLink(string input, out ulong id)
    {
        ArgumentNullException.ThrowIfNull(input);

        id = 0;

        var match = s_publishedFileIdLinkRegex.Match(input);
        if (match.Success)
        {
            if (ulong.TryParse(match.Groups[1].ValueSpan, out id))
            {
                return true;
            }
        }

        return false;
    }
}

public class GetPublishedFileDetailsResult
{
    private PublishedFileDetails? _content;
    private GetPublishedFileDetailsResultStatus _status;

    internal GetPublishedFileDetailsResult(PublishedFileDetails? content, bool isDownloadable, GetPublishedFileDetailsResultStatus status)
    {
        if (status == GetPublishedFileDetailsResultStatus.Succeeded && content == null)
        {
            throw new ArgumentNullException(nameof(content));
        }
        if (isDownloadable && status != GetPublishedFileDetailsResultStatus.Succeeded)
        {
            throw new ArgumentException($"A downloadable result must be {nameof(GetPublishedFileDetailsResultStatus.Succeeded)}.", nameof(isDownloadable));
        }

        _content = content;
        IsDownloadable = isDownloadable;
        _status = status;
    }

    /// <summary>
    /// The parsed details. Available for any item the API returned usable data for,
    /// including collections and image entries, so callers can read metadata such as the title.
    /// May be null when the request failed or the id was truly invalid.
    /// </summary>
    public PublishedFileDetails? Details => _content;

    /// <summary>
    /// Whether the item is an actual downloadable addon (a VPK), as opposed to a
    /// collection or an image-only entry which must not be treated as an addon.
    /// </summary>
    public bool IsDownloadable { get; }

    public PublishedFileDetails Content
    {
        get
        {
            if (!IsSucceeded)
            {
                throw new InvalidOperationException("The status isn't succeeded.");
            }
            return _content!;
        }
    }

    public GetPublishedFileDetailsResultStatus Status => _status;

    public bool IsSucceeded => _status == GetPublishedFileDetailsResultStatus.Succeeded;
}

public enum GetPublishedFileDetailsResultStatus
{
    Succeeded,
    Failed,
    InvalidPublishedFileId,
}
