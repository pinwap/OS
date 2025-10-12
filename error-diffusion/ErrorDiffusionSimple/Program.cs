using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

public class ErrorDiffusionApp
{
  // ========== CONFIGURATION ==========
  // Configure these values directly in the code
  private const string INPUT_PATH = "input.png";  // Change this to your input image path
  private const string OUTPUT_PATH = "output_dithered.png";  // Change this to your desired output path
  private const int NUM_THREADS = 4;  // Change this to the number of threads you want to use
  // ===================================

  private static int[,] isDone = new int[0, 0];

  public static void Main(string[] args)
  {
    Console.WriteLine("--- Floyd-Steinberg Error Diffusion (Configured Version) ---");
    Console.WriteLine($"Configuration:");
    Console.WriteLine($"  Input Path: {INPUT_PATH}");
    Console.WriteLine($"  Output Path: {OUTPUT_PATH}");
    Console.WriteLine($"  Number of Threads: {NUM_THREADS}");
    Console.WriteLine();

    RunDithering(INPUT_PATH, OUTPUT_PATH, NUM_THREADS);
  }

  public static int FloorDiv(int a, int b)
  {
    // Python-style floor division: floor(a / b)
    // For negative results, we need to round down (towards negative infinity)
    int quotient = a / b;
    int remainder = a % b;

    // If remainder is non-zero and signs of a and b differ, we need to subtract 1
    if (remainder != 0 && ((a < 0) != (b < 0)))
    {
      quotient--;
    }

    return quotient;
  }

  /// <summary>
  /// Main processing function
  /// </summary>
  public static void RunDithering(string inputPath, string outputPath, int numThreads)
  {
    int maxThreads = Environment.ProcessorCount;
    int actualThreads = Math.Min(numThreads, maxThreads);
    if (numThreads > maxThreads)
    {
      Console.WriteLine($"Warning: Requested {numThreads} threads, but machine only has {maxThreads}. Using {actualThreads} threads.");
    }
    actualThreads = Math.Max(1, actualThreads);

    var stopwatch = Stopwatch.StartNew();

    try
    {
      Console.WriteLine($"Loading '{inputPath}'...");
      var (imageData, width, height) = LoadImageToBuffer(inputPath);

      Console.WriteLine($"Processing {width}x{height} image with {actualThreads} thread(s)...");

      if (actualThreads == 1)
      {
        // Use true sequential processing for 1 thread
        ProcessSequential(imageData, width, height);
      }
      else
      {
        // Use parallel processing for multiple threads
        isDone = new int[height + 2, width + 2];
        for (int i = 0; i < height + 2; i++) { isDone[i, 0] = 1; isDone[i, width + 1] = 1; }
        for (int j = 0; j < width + 2; j++) { isDone[0, j] = 1; isDone[height + 1, j] = 1; }

        var threads = new List<Thread>();
        for (int i = 0; i < actualThreads; i++)
        {
          int startRow = i;
          var thread = new Thread(() => ProcessParallel(imageData, width, height, startRow, actualThreads));
          threads.Add(thread);
          thread.Start();
        }
        foreach (var thread in threads) { thread.Join(); }
      }

      Console.WriteLine($"\nSaving result to '{outputPath}'...");
      SaveImage(imageData, width, height, outputPath);

      stopwatch.Stop();
      Console.WriteLine($"✅ Done in {stopwatch.Elapsed.TotalSeconds:F2} seconds.");
    }
    catch (Exception ex)
    {
      Console.ForegroundColor = ConsoleColor.Red;
      Console.WriteLine($"\n❌ An error occurred: {ex.Message}");
      Console.ResetColor();
    }
  }

  #region Core Logic and Helpers

  public static void ProcessSequential(int[,] image, int width, int height)
  {
    int paddedWidth = width + 1;
    int paddedHeight = height + 1;
    const int threshold = 128;

    for (int y = 1; y < paddedHeight; y++)
    {
      for (int x = 1; x < paddedWidth; x++)
      {
        int oldPixel = image[y, x];
        int newPixel = oldPixel > threshold ? 255 : 0;
        image[y, x] = newPixel;
        int error = oldPixel - newPixel;

        image[y, x + 1] += FloorDiv(error * 7, 16);
        image[y + 1, x - 1] += FloorDiv(error * 3, 16);
        image[y + 1, x] += FloorDiv(error * 5, 16);
        image[y + 1, x + 1] += FloorDiv(error * 1, 16);
      }
    }
  }

  public static void ProcessParallel(int[,] image, int width, int height, int startRow, int step)
  {
    int paddedWidth = width + 1;
    int paddedHeight = height + 1;
    const int threshold = 128;

    for (int y = startRow + 1; y < paddedHeight; y += step)
    {
      for (int x = 1; x < paddedWidth; x++)
      {
        while (isDone[y - 1, x + 1] == 0)
        {
          Thread.SpinWait(1);
        }

        int oldPixel = image[y, x];
        int newPixel = oldPixel > threshold ? 255 : 0;
        image[y, x] = newPixel;
        int error = oldPixel - newPixel;

        Interlocked.Add(ref image[y, x + 1], FloorDiv(error * 7, 16));
        Interlocked.Add(ref image[y + 1, x - 1], FloorDiv(error * 3, 16));
        Interlocked.Add(ref image[y + 1, x], FloorDiv(error * 5, 16));
        Interlocked.Add(ref image[y + 1, x + 1], FloorDiv(error * 1, 16));

        Interlocked.Exchange(ref isDone[y, x], 1);
      }
    }
  }

  public static (int[,] buffer, int width, int height) LoadImageToBuffer(string filePath)
  {
    using (Image<Rgba32> image = Image.Load<Rgba32>(filePath))
    {
      int width = image.Width;
      int height = image.Height;
      int[,] buffer = new int[height + 2, width + 2];

      image.ProcessPixelRows(accessor =>
      {
        for (int y = 0; y < height; y++)
        {
          Span<Rgba32> pixelRow = accessor.GetRowSpan(y);
          for (int x = 0; x < width; x++)
          {
            Rgba32 pixel = pixelRow[x];
            // For grayscale images, R=G=B, so just use R directly to avoid rounding errors
            int gray = pixel.R;
            buffer[y + 1, x + 1] = gray;  // NO scaling by 16
          }
        }
      });

      return (buffer, width, height);
    }
  }

  public static void SaveImage(int[,] image, int width, int height, string filename)
  {
    try
    {
      using (Image<Rgba32> img = new Image<Rgba32>(width, height))
      {
        img.ProcessPixelRows(accessor =>
        {
          for (int y = 0; y < height; y++)
          {
            Span<Rgba32> pixelRow = accessor.GetRowSpan(y);
            for (int x = 0; x < width; x++)
            {
              int colorVal = Math.Max(0, Math.Min(255, image[y + 1, x + 1]));
              pixelRow[x] = new Rgba32((byte)colorVal, (byte)colorVal, (byte)colorVal, 255);
            }
          }
        });

        img.SaveAsPng(filename);
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"\nError saving image '{filename}': {ex.Message}");
      Console.WriteLine("This might be due to image size limitations or file permissions.");
    }
  }

  #endregion
}
