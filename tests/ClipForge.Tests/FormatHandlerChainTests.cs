using ClipForge.Core;

using Xunit;

namespace ClipForge.Tests;

/// <summary>
/// Handler-chain selection: priority order, and — the regression these tests
/// pin — a handler that claims the clipboard but fails to capture must NOT
/// drop the copy. Before the fall-through fix, the first claiming handler's
/// null result ended the capture, so a clipboard offering image + text lost
/// the copy entirely when the image read failed.
/// </summary>
public sealed class FormatHandlerChainTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);

    private static CapturedClip Clip(string text) =>
        new(ClipType.Text, text, null, null, "fake", null, null, T0);

    private sealed class FakeHandler : IClipFormatHandler
    {
        private readonly bool _canHandle;
        private readonly CapturedClip? _result;

        public FakeHandler(int priority, bool canHandle, CapturedClip? result)
        {
            Priority = priority;
            _canHandle = canHandle;
            _result = result;
        }

        public string Name => "Fake";
        public int Priority { get; }
        public int CaptureCalls { get; private set; }
        public bool CanHandle() => _canHandle;

        public CapturedClip? Capture()
        {
            CaptureCalls++;
            return _result;
        }
    }

    [Fact]
    public void ClaimingHandlerThatFailsFallsThroughToNextHandler()
    {
        var image = new FakeHandler(priority: 10, canHandle: true, result: null);
        var text = new FakeHandler(priority: 30, canHandle: true, result: Clip("copied text"));

        var captured = FormatHandlerChain.CaptureFirst(new IClipFormatHandler[] { image, text });

        Assert.Equal("copied text", captured?.Text);
        Assert.Equal(1, image.CaptureCalls);
        Assert.Equal(1, text.CaptureCalls);
    }

    [Fact]
    public void FirstSuccessfulCaptureWinsAndLaterHandlersAreNotConsulted()
    {
        var first = new FakeHandler(priority: 10, canHandle: true, result: Clip("first"));
        var second = new FakeHandler(priority: 30, canHandle: true, result: Clip("second"));

        var captured = FormatHandlerChain.CaptureFirst(new IClipFormatHandler[] { first, second });

        Assert.Equal("first", captured?.Text);
        Assert.Equal(0, second.CaptureCalls);
    }

    [Fact]
    public void PriorityOrderIsRespectedRegardlessOfListOrder()
    {
        var low = new FakeHandler(priority: 30, canHandle: true, result: Clip("low"));
        var high = new FakeHandler(priority: 0, canHandle: true, result: Clip("high"));

        var captured = FormatHandlerChain.CaptureFirst(new IClipFormatHandler[] { low, high });

        Assert.Equal("high", captured?.Text);
        Assert.Equal(0, low.CaptureCalls);
    }

    [Fact]
    public void HandlerThatCannotHandleIsSkippedWithoutCapturing()
    {
        var absent = new FakeHandler(priority: 0, canHandle: false, result: Clip("unreachable"));
        var present = new FakeHandler(priority: 30, canHandle: true, result: Clip("present"));

        var captured = FormatHandlerChain.CaptureFirst(new IClipFormatHandler[] { absent, present });

        Assert.Equal("present", captured?.Text);
        Assert.Equal(0, absent.CaptureCalls);
    }

    [Fact]
    public void AllHandlersFailingReturnsNull()
    {
        var a = new FakeHandler(priority: 0, canHandle: true, result: null);
        var b = new FakeHandler(priority: 10, canHandle: true, result: null);

        var captured = FormatHandlerChain.CaptureFirst(new IClipFormatHandler[] { a, b });

        Assert.Null(captured);
        Assert.Equal(1, a.CaptureCalls);
        Assert.Equal(1, b.CaptureCalls);
    }

    [Fact]
    public void NoHandlerClaimingReturnsNull()
    {
        var a = new FakeHandler(priority: 0, canHandle: false, result: Clip("unreachable"));

        Assert.Null(FormatHandlerChain.CaptureFirst(new IClipFormatHandler[] { a }));
        Assert.Equal(0, a.CaptureCalls);
    }

    [Fact]
    public void EmptyHandlerListReturnsNull()
    {
        Assert.Null(FormatHandlerChain.CaptureFirst(Array.Empty<IClipFormatHandler>()));
    }

    [Fact]
    public void NullHandlerListThrows()
    {
        Assert.Throws<ArgumentNullException>(() => FormatHandlerChain.CaptureFirst(null!));
    }
}
