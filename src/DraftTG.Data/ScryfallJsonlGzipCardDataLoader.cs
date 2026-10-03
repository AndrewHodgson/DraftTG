using System.IO.Compression;
using System.Text;

namespace DraftTG.Data;

public interface IScryfallCardDataFileLoader
{
    Task<ScryfallCardCatalogData> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}

/// <summary>Streams a gzip-compressed Scryfall JSONL file into normalized Domain data.</summary>
public sealed class ScryfallJsonlGzipCardDataLoader : IScryfallCardDataFileLoader
{
    public async Task<ScryfallCardCatalogData> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var builder = new ScryfallCardCatalogBuilder();
        long lineNumber = 0;
        try
        {
            await using var file = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var gzip = new GZipStream(file, CompressionMode.Decompress, leaveOpen: false);
            using var reader = new StreamReader(
                gzip,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 64 * 1024,
                leaveOpen: false);

            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                lineNumber++;
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line))
                {
                    throw new ScryfallCardDataException(
                        ScryfallCardDataErrorKind.MalformedJsonLine,
                        $"Scryfall JSONL line {lineNumber} was empty.",
                        lineNumber: lineNumber);
                }

                try
                {
                    builder.Add(ScryfallCardNormalizer.DecodeAndNormalize(line));
                }
                catch (CardCatalogImportException error)
                {
                    var kind = error.Kind == CardCatalogImportErrorKind.MalformedJson
                        ? ScryfallCardDataErrorKind.MalformedJsonLine
                        : ScryfallCardDataErrorKind.CatalogImportFailed;
                    throw new ScryfallCardDataException(
                        kind,
                        $"Scryfall JSONL line {lineNumber} could not be imported: {error.Message}",
                        error,
                        lineNumber);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ScryfallCardDataException)
        {
            throw;
        }
        catch (InvalidDataException error)
        {
            throw new ScryfallCardDataException(
                ScryfallCardDataErrorKind.InvalidGzip,
                "The Scryfall bulk file was not a valid gzip stream.",
                error,
                lineNumber == 0 ? null : lineNumber);
        }
        catch (DecoderFallbackException error)
        {
            throw new ScryfallCardDataException(
                ScryfallCardDataErrorKind.MalformedJsonLine,
                $"Scryfall JSONL contained invalid UTF-8 near line {lineNumber + 1}.",
                error,
                lineNumber + 1);
        }

        if (builder.Count == 0)
        {
            throw new ScryfallCardDataException(
                ScryfallCardDataErrorKind.EmptyBulkData,
                "The Scryfall bulk file contained no card records.");
        }

        return builder.Build();
    }
}
