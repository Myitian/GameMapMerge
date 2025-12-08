using System.Drawing;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace GameMapMerge;

partial class Program
{
    [GeneratedRegex(@"^UI_(?<name>Map.+)_(?<x>-?[0-9]+)_(?<y>-?[0-9]+)\.png$",
        RegexOptions.ExplicitCapture | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex RxMapFileName();
    [GeneratedRegex(@"^BigWorldTerrain_(?<x>-?[0-9]+)_(?<y>-?[0-9]+)\.bin_(?<name>.+)\.png$",
        RegexOptions.ExplicitCapture | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex RxTerrainFileName();
    [GeneratedRegex(@"^UI_(?<name>Map.+)_None\.png$",
        RegexOptions.ExplicitCapture | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex RxMapNoneFileName();
    static void Main()
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        // These definitions are designed for a specific game.
        // You can modify these definitions according to your needs.
        Console.WriteLine("Mode: [1 (UI_Map) / 2 (BigWorldTerrain)]");
        MapDefinition def = Console.ReadLine() switch
        {
            "1" => new(RxMapFileName(), RxMapNoneFileName(), "UI_Map*.png", true, true, true),
            "2" => new(RxTerrainFileName(), null, "BigWorldTerrain_*.png", false, false, true),
            _ => throw new Exception()
        };

        Console.WriteLine("Input Directory:");
        string inputDir = Console.ReadLine().AsSpan().Trim().Trim('"').ToString();
        Console.WriteLine("Output Directory:");
        string outputDir = Console.ReadLine().AsSpan().Trim().Trim('"').ToString();
        Console.WriteLine();
        Process(in def, inputDir, outputDir);
    }
    static void Process(in MapDefinition def, string inputDir, string outputDir)
    {
        DirectoryInfo dir = new(inputDir);
        Dictionary<string, MapInfo> bitmaps = [];
        foreach (FileInfo file in dir.EnumerateFiles(def.Filter))
        {
            int x = 0, y = 0;
            Match? mMain = def.MainRegex.Match(file.Name);
            if (!mMain.Success
                || !int.TryParse(mMain.Groups["x"].ValueSpan, out x)
                || !int.TryParse(mMain.Groups["y"].ValueSpan, out y))
                mMain = null;
            else
            {
                if (def.FlipXY)
                    (x, y) = (y, x);
                if (def.NegX)
                    x = -x;
                if (def.NegY)
                    y = -y;
            }
            Match? mFallback = def.FallbackRegex?.Match(file.Name);
            if (mFallback?.Success is false)
                mFallback = null;
            if (!ReferenceEquals(mMain, mFallback)) // both not null
            {
                DisposableHolder<Bitmap> bmp = new(new(file.FullName));
                Console.WriteLine(file.FullName);
                if (mFallback is not null)
                {
                    string name = mFallback.Groups["name"].Value;
                    if (!bitmaps.TryGetValue(name, out MapInfo? info))
                        bitmaps.Add(name, info = new());
                    bmp.Increase();
                    info.Fallback = bmp;
                }
                if (mMain is not null)
                {
                    string name = mMain.Groups["name"].Value;
                    if (!bitmaps.TryGetValue(name, out MapInfo? info))
                        bitmaps.Add(name, info = new());
                    bmp.Increase();
                    info[x, y] = bmp;
                }
            }
        }
        foreach ((string name, MapInfo info) in bitmaps)
        {
            if (info.IsEmpty)
                continue;
            Console.WriteLine();
            Console.WriteLine(name);
            (Point lt, Point rb) = info.LTRB;
            int width = rb.X - lt.X + 1;
            int height = rb.Y - lt.Y + 1;
            int size = info.UnitSize;
            using DirectBitmap dbmp = new(width * size, height * size);
            for (int y = lt.Y; y <= rb.Y; y++)
            {
                int offy = (y - lt.Y) * size;
                for (int x = lt.X; x <= rb.X; x++)
                {
                    int offx = (x - lt.X) * size;
                    DisposableHolder<Bitmap>? bmp = info[x, y];
                    Rectangle rect = new(offx, offy, size, size);
                    Console.WriteLine($"{x},{y}:{rect}");
                    if (bmp is null)
                        dbmp.Fill(0, rect);
                    else
                        dbmp.CopyFrom(bmp.Value, rect);
                }
            }
            info.Dispose();
            string output = Path.Combine(outputDir, $"Merged_{name}_{width}x{height}@{dbmp.Width}x{dbmp.Height}.png");
            Console.WriteLine(output);
            using FileStream fs = File.Open(output, FileMode.Create, FileAccess.Write, FileShare.Read);
            dbmp.SavePNG(fs, CompressionLevel.SmallestSize, 16777216);
            // GDI+ seems unable to properly handle pixels with byte offsets exceeding int.MaxValue
            // (it will write fully transparent content in the excess portion)
            // Therefore, a basic PNG implementation is used here to write the PNG file.
        }
    }

    readonly struct MapDefinition(Regex mainRegex, Regex? fallbackRegex, string filter, bool flipXY, bool negX, bool negY)
    {
        public readonly Regex MainRegex = mainRegex;
        public readonly Regex? FallbackRegex = fallbackRegex;
        public readonly string Filter = filter;
        public readonly bool FlipXY = flipXY;
        public readonly bool NegX = negX;
        public readonly bool NegY = negY;
    }
}