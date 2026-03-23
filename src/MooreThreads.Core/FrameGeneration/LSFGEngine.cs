using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MooreThreadsUpScaler.Core.FrameGeneration
{
    public class FrameGenerationResult
    {
        public BitmapSource OriginalFrame { get; set; } = null!;
        public BitmapSource? GeneratedFrame { get; set; }
        public bool IsInterpolated { get; set; }
        public double GenerationTimeMs { get; set; }
    }

    public sealed class LSFGEngine : IDisposable
    {
        private BitmapSource? _previousFrame;
        private double _lastMotionX;
        private double _lastMotionY;
        private bool _disposed;

        public int Multiplier { get; set; } = 2;
        public bool ZeroLatencyMode { get; set; }

        public FrameGenerationResult ProcessFrame(BitmapSource frame)
        {
            var result = new FrameGenerationResult { OriginalFrame = frame };

            if (_previousFrame is null)
            {
                _previousFrame = frame;
                return result;
            }

            try
            {
                var sw     = System.Diagnostics.Stopwatch.StartNew();
                var motion = EstimateMotion(_previousFrame, frame);

                if (motion.confidence > 0.3)
                {
                    _lastMotionX = _lastMotionX * 0.7 + motion.mx * 0.3;
                    _lastMotionY = _lastMotionY * 0.7 + motion.my * 0.3;
                }

                result.GeneratedFrame  = InterpolateFrames(_previousFrame, frame, _lastMotionX * 0.5, _lastMotionY * 0.5);
                result.IsInterpolated  = true;
                sw.Stop();
                result.GenerationTimeMs = sw.Elapsed.TotalMilliseconds;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LSFG] {ex.Message}");
            }
            finally
            {
                _previousFrame = frame;
            }

            return result;
        }

        public void Reset()
        {
            _previousFrame = null;
            _lastMotionX   = _lastMotionY = 0;
        }

        // ── Motion estimation (block-matching SAD) ─────────────────────────────
        private static (double mx, double my, double confidence) EstimateMotion(
            BitmapSource prev, BitmapSource curr)
        {
            if (prev.PixelWidth != curr.PixelWidth || prev.PixelHeight != curr.PixelHeight)
                return (0, 0, 0);

            int w = prev.PixelWidth, h = prev.PixelHeight, stride = w * 4;
            var prevPx = new byte[h * stride];
            var currPx = new byte[h * stride];
            prev.CopyPixels(prevPx, stride, 0);
            curr.CopyPixels(currPx, stride, 0);

            const int blockSize = 32, search = 16, step = 4;
            double totalX = 0, totalY = 0;
            int valid = 0;
            int bx = w / blockSize, by = h / blockSize;

            for (int byy = 0; byy < by; byy++)
            {
                for (int bxx = 0; bxx < bx; bxx++)
                {
                    int ox = bxx * blockSize, oy = byy * blockSize;
                    double bestSAD = double.MaxValue, bestDx = 0, bestDy = 0;

                    for (int dy = -search; dy <= search; dy += step)
                    {
                        for (int dx = -search; dx <= search; dx += step)
                        {
                            double sad = 0; int n = 0;
                            for (int py = 0; py < blockSize; py += 2)
                            {
                                int sy = oy + py, ty = sy + dy;
                                if (ty < 0 || ty >= h) continue;
                                for (int px = 0; px < blockSize; px += 2)
                                {
                                    int sx = ox + px, tx = sx + dx;
                                    if (tx < 0 || tx >= w) continue;
                                    int si = (sy * stride) + (sx * 4);
                                    int ti = (ty * stride) + (tx * 4);
                                    sad += Math.Abs(currPx[ti]     - prevPx[si]);
                                    sad += Math.Abs(currPx[ti + 1] - prevPx[si + 1]);
                                    sad += Math.Abs(currPx[ti + 2] - prevPx[si + 2]);
                                    n   += 3;
                                }
                            }
                            if (n > 0) sad /= n;
                            if (sad < bestSAD) { bestSAD = sad; bestDx = dx; bestDy = dy; }
                        }
                    }

                    if (bestSAD < 30) { totalX += bestDx; totalY += bestDy; valid++; }
                }
            }

            if (valid == 0) return (0, 0, 0);
            return (totalX / valid, totalY / valid, Math.Min(1.0, valid / (double)(bx * by)));
        }

        // ── Bilinear motion-compensated blend ─────────────────────────────────
        private static BitmapSource InterpolateFrames(
            BitmapSource prev, BitmapSource curr, double motionX, double motionY)
        {
            int w = prev.PixelWidth, h = prev.PixelHeight, stride = w * 4;
            var prevPx = new byte[h * stride];
            var currPx = new byte[h * stride];
            prev.CopyPixels(prevPx, stride, 0);
            curr.CopyPixels(currPx, stride, 0);

            var result = new byte[h * stride];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var p1  = SampleBilinear(prevPx, w, h, stride, x - motionX, y - motionY);
                    var p2  = SampleBilinear(currPx, w, h, stride, x + motionX, y + motionY);
                    int idx = (y * stride) + (x * 4);
                    result[idx]     = (byte)((p1[0] + p2[0]) >> 1);
                    result[idx + 1] = (byte)((p1[1] + p2[1]) >> 1);
                    result[idx + 2] = (byte)((p1[2] + p2[2]) >> 1);
                    result[idx + 3] = 255;
                }
            }

            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, result, stride);
            bmp.Freeze();
            return bmp;
        }

        private static byte[] SampleBilinear(byte[] px, int w, int h, int stride, double x, double y)
        {
            x = Math.Clamp(x, 0, w - 1);
            y = Math.Clamp(y, 0, h - 1);
            int x0 = (int)x, y0 = (int)y;
            int x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
            double fx = x - x0, fy = y - y0;

            var result = new byte[4];
            for (int c = 0; c < 3; c++)
            {
                double v = (px[(y0 * stride) + (x0 * 4) + c] * (1 - fx) + px[(y0 * stride) + (x1 * 4) + c] * fx) * (1 - fy)
                         + (px[(y1 * stride) + (x0 * 4) + c] * (1 - fx) + px[(y1 * stride) + (x1 * 4) + c] * fx) * fy;
                result[c] = (byte)Math.Clamp(v, 0, 255);
            }
            return result;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _previousFrame = null;
            _disposed      = true;
        }
    }
}
