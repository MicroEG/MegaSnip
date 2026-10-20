using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MegaSnip.Clipboard;
using Xunit;

namespace MegaSnip.Tests.Clipboard;

public sealed class ClipboardServiceTests
{
    [Fact]
    public async Task TrySetTextAsync_WritesTextOnFirstAttempt()
    {
        string? written = null;
        var service = new ClipboardService(text => written = text, TimeSpan.Zero);

        bool copied = await service.TrySetTextAsync("plain text");

        Assert.True(copied);
        Assert.Equal("plain text", written);
    }

    [Fact]
    public async Task TrySetTextAsync_RetriesTransientClipboardLocks()
    {
        int attempts = 0;
        var service = new ClipboardService(
            _ =>
            {
                attempts++;
                if (attempts < 4)
                {
                    throw new ExternalException("Clipboard is busy.");
                }
            },
            TimeSpan.Zero);

        bool copied = await service.TrySetTextAsync("retried");

        Assert.True(copied);
        Assert.Equal(4, attempts);
    }

    [Fact]
    public async Task TrySetTextAsync_GivesUpAfterTenAttempts()
    {
        int attempts = 0;
        var service = new ClipboardService(
            _ =>
            {
                attempts++;
                throw new ExternalException("Clipboard is busy.");
            },
            TimeSpan.Zero);

        bool copied = await service.TrySetTextAsync("never copied");

        Assert.False(copied);
        Assert.Equal(10, attempts);
    }

    [Fact]
    public async Task TrySetTextAsync_DoesNotTouchClipboardWhenAlreadyCanceled()
    {
        int attempts = 0;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new ClipboardService(
            _ =>
            {
                attempts++;
                throw new ExternalException("Clipboard is busy.");
            },
            TimeSpan.FromMilliseconds(10));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.TrySetTextAsync("text", cancellation.Token));

        Assert.Equal(0, attempts);
    }

    [Fact]
    public async Task TrySetTextAsync_RejectsNullText()
    {
        var service = new ClipboardService(_ => { }, TimeSpan.Zero);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.TrySetTextAsync(null!));
    }
}
