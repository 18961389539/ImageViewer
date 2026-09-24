using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using ImageViewer.Drawing;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.ViewModels;

namespace ImageViewerControl.Tests
{
    /// <summary>
    /// <see cref="IRoiDrawHost"/> 的测试替身：记录调用并允许预设返回值。
    /// Chinese: 让绘制会话可以脱离 WPF 事件与真实控件单独单测。
    /// English: Test double for <see cref="IRoiDrawHost"/>. Records calls and lets tests preset
    /// results so draw sessions can be unit tested without WPF input events or a real control.
    /// </summary>
    internal sealed class FakeRoiDrawHost : IRoiDrawHost
    {
        private readonly List<string> _calls = new();

        public FakeRoiDrawHost(RoiPluginRegistry? pluginRegistry = null)
        {
            ViewModel = new ImageViewerViewModel(pluginRegistry ?? RoiPluginRegistry.CreateBuiltIn());
        }

        public ImageViewerViewModel ViewModel { get; }

        public double Scale { get; set; } = 1.0;

        public double MinimumDrawableSize { get; set; } = 0.1;

        public double MinimumLineLength { get; set; } = 1.0;

        public double HitTestTolerance { get; set; } = 5.0;

        /// <summary>预设的吸附结果；为 null 时原样返回入参。</summary>
        public Point? SnapPointResult { get; set; }

        public RoiBase? HitTestResult { get; set; }

        public bool CaptureResult { get; set; } = true;

        public bool AnalysisResult { get; set; } = true;

        /// <summary>预设的文本输入结果；null 表示用户取消。</summary>
        public string? TextInputResult { get; set; }

        public IReadOnlyList<string> Calls => _calls;

        /// <summary>某次调用的序号，用于断言调用顺序。</summary>
        public int IndexOfCall(string name) => _calls.IndexOf(name);

        /// <summary>某次调用的次数。</summary>
        public int CountCalls(string name) => _calls.Count(call => call == name);

        public List<RoiBase> CommittedRois { get; } = new();

        public List<RoiBase> AnalyzedRois { get; } = new();

        public int CaptureCount { get; private set; }

        public int ReleaseCaptureCount { get; private set; }

        public int InvalidateCount { get; private set; }

        public int EndDrawCount { get; private set; }

        public Cursor? LastCursor { get; private set; }

        public Point SnapPoint(Point rawImagePoint)
        {
            _calls.Add(nameof(SnapPoint));
            return SnapPointResult ?? rawImagePoint;
        }

        public RoiBase? HitTest(Point point)
        {
            _calls.Add(nameof(HitTest));
            return HitTestResult;
        }

        public bool TryCaptureMouse()
        {
            _calls.Add(nameof(TryCaptureMouse));
            CaptureCount++;
            return CaptureResult;
        }

        public void ReleaseMouseCapture()
        {
            _calls.Add(nameof(ReleaseMouseCapture));
            ReleaseCaptureCount++;
        }

        public void SetCursor(Cursor cursor)
        {
            _calls.Add(nameof(SetCursor));
            LastCursor = cursor;
        }

        public void InvalidateOverlay()
        {
            _calls.Add(nameof(InvalidateOverlay));
            InvalidateCount++;
        }

        public string? RequestTextInput(string message, string defaultValue)
        {
            _calls.Add(nameof(RequestTextInput));
            return TextInputResult;
        }

        public bool TryApplyAnalysis(RoiBase roi)
        {
            _calls.Add(nameof(TryApplyAnalysis));
            AnalyzedRois.Add(roi);
            return AnalysisResult;
        }

        public void Commit(RoiBase roi)
        {
            _calls.Add(nameof(Commit));
            CommittedRois.Add(roi);
        }

        public void EndDraw()
        {
            _calls.Add(nameof(EndDraw));
            EndDrawCount++;
        }
    }
}
