// Снимок окон средствами Windows.Graphics.Capture: каждое окно снимается отдельно, с альфа-каналом,
// и накладывается на первое ("основу") по экранным координатам.
//
//   WinShot <out.png> <hwnd основы> [hwnd накладки ...]      (hwnd — десятичное число)
//   WinShot --bg ff00ff <out.png> <hwnd>                      одно окно на сплошном фоне: видно, где оно прозрачное

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using WinRT;

static class Program
{
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComVisible(true)]
    interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int L, T, R, B; }

    [DllImport("d3d11.dll")]
    static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr featureLevels,
        uint featureLevelCount, uint sdkVersion, out IntPtr device, out int featureLevel, out IntPtr context);
    [DllImport("d3d11.dll")]
    static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
    [DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT rect, int size);

    static IDirect3DDevice CreateDevice()
    {
        int hr = D3D11CreateDevice(IntPtr.Zero, 1 /*HARDWARE*/, IntPtr.Zero, 0x20 /*BGRA*/, IntPtr.Zero, 0, 7, out var d3d, out _, out var context);
        Marshal.ThrowExceptionForHR(hr);
        var dxgiIid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3d, in dxgiIid, out var dxgi));
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var inspectable));
        var device = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        Marshal.Release(inspectable);
        Marshal.Release(dxgi);
        Marshal.Release(context);
        Marshal.Release(d3d);
        return device;
    }

    static async Task<Bitmap> CaptureAsync(IDirect3DDevice device, IntPtr hwnd)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var itemIid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        var itemPtr = interop.CreateForWindow(hwnd, ref itemIid);
        var item = GraphicsCaptureItem.FromAbi(itemPtr);
        Marshal.Release(itemPtr);

        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);
        using var session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = false;
        try { session.IsBorderRequired = false; } catch { }

        var arrived = new TaskCompletionSource<Direct3D11CaptureFrame>();
        pool.FrameArrived += (p, _) => { var f = p.TryGetNextFrame(); if (f != null) arrived.TrySetResult(f); };
        session.StartCapture();
        using var frame = await arrived.Task.WaitAsync(TimeSpan.FromSeconds(4));

        using var soft = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Premultiplied);
        var pixels = new byte[4 * soft.PixelWidth * soft.PixelHeight];
        soft.CopyToBuffer(pixels.AsBuffer());

        var bitmap = new Bitmap(soft.PixelWidth, soft.PixelHeight, PixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        for (int y = 0; y < bitmap.Height; y++)
            Marshal.Copy(pixels, y * bitmap.Width * 4, data.Scan0 + y * data.Stride, bitmap.Width * 4);
        bitmap.UnlockBits(data);
        return bitmap;
    }

    static Point Origin(IntPtr hwnd)
    {
        DwmGetWindowAttribute(hwnd, 9 /*EXTENDED_FRAME_BOUNDS*/, out var r, 16);
        return new Point(r.L, r.T);
    }

    static async Task<int> Main(string[] args)
    {
        try
        {
            var list = new List<string>(args);
            Color? background = null;
            if (list.Count > 1 && list[0] == "--bg")
            {
                background = ColorTranslator.FromHtml("#" + list[1]);
                list.RemoveRange(0, 2);
            }
            // --px x,y;x,y — напечатать сырые значения пикселей окна (синий, зелёный, красный, альфа) и выйти.
            // Нужно, чтобы поймать пиксели "альфа = 0, а цвет не чёрный": снимок их прячет,
            // а на живом экране они прибавляются к фону и дают белёсые прямоугольники.
            if (list.Count > 2 && list[0] == "--px")
            {
                using var raw = await CaptureAsync(CreateDevice(), new IntPtr(long.Parse(list[2])));
                var bits = raw.LockBits(new Rectangle(0, 0, raw.Width, raw.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                foreach (var point in list[1].Split(';'))
                {
                    var xy = point.Split(',');
                    int x = int.Parse(xy[0]), y = int.Parse(xy[1]);
                    if (x < 0 || y < 0 || x >= raw.Width || y >= raw.Height) { Console.WriteLine($"({x},{y}) вне окна"); continue; }
                    int value = Marshal.ReadInt32(bits.Scan0 + y * bits.Stride + x * 4);
                    Console.WriteLine($"({x},{y}) B={value & 255} G={(value >> 8) & 255} R={(value >> 16) & 255} A={(value >> 24) & 255}");
                }
                raw.UnlockBits(bits);
                return 0;
            }
            // --scan <hwnd> — найти "светящиеся" пиксели: цвет ярче, чем позволяет альфа-канал.
            // На живом экране такие пиксели прибавляются к фону и выглядят белёсыми пятнами.
            if (list.Count > 1 && list[0] == "--scan")
            {
                using var raw = await CaptureAsync(CreateDevice(), new IntPtr(long.Parse(list[1])));
                var bits = raw.LockBits(new Rectangle(0, 0, raw.Width, raw.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                int bad = 0, minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, sample = 0;
                for (int y = 0; y < raw.Height; y++)
                    for (int x = 0; x < raw.Width; x++)
                    {
                        int value = Marshal.ReadInt32(bits.Scan0 + y * bits.Stride + x * 4);
                        int a = (value >> 24) & 255, brightest = Math.Max(value & 255, Math.Max((value >> 8) & 255, (value >> 16) & 255));
                        if (brightest <= a + 2) continue;
                        if (bad++ == 0) sample = value;
                        minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                    }
                raw.UnlockBits(bits);
                Console.WriteLine(bad == 0
                    ? $"scan {raw.Width}x{raw.Height}: чисто, светящихся пикселей нет"
                    : $"scan {raw.Width}x{raw.Height}: светящихся пикселей {bad}, в прямоугольнике [{minX},{minY} .. {maxX},{maxY}], пример BGRA=({sample & 255},{(sample >> 8) & 255},{(sample >> 16) & 255},{(sample >> 24) & 255})");
                return 0;
            }
            if (list.Count < 2) { Console.Error.WriteLine("usage: WinShot [--bg rrggbb] <out.png> <hwnd> [hwnd ...]"); return 2; }

            var device = CreateDevice();
            var baseHwnd = new IntPtr(long.Parse(list[1]));
            using var result = await CaptureAsync(device, baseHwnd);
            var origin = Origin(baseHwnd);

            Bitmap canvas = result;
            if (background != null)
            {
                canvas = new Bitmap(result.Width, result.Height, PixelFormat.Format32bppPArgb);
                using var g0 = Graphics.FromImage(canvas);
                g0.Clear(background.Value);
                g0.DrawImage(result, 0, 0);
            }
            using (var g = Graphics.FromImage(canvas))
            {
                for (int i = 2; i < list.Count; i++)
                {
                    var hwnd = new IntPtr(long.Parse(list[i]));
                    using var overlay = await CaptureAsync(device, hwnd);
                    var at = Origin(hwnd);
                    g.DrawImage(overlay, at.X - origin.X, at.Y - origin.Y);
                }
            }
            canvas.Save(list[0], ImageFormat.Png);
            Console.WriteLine($"saved {list[0]} {canvas.Width}x{canvas.Height}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAILED: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }
}
