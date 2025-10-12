using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;

public class WavefrontErrorDiffusion
{
  private static int[,] isDone = new int[0, 0];
  private static volatile int rowsCompleted = 0;

  public static void Main(string[] args)
  {
    // --- Parameters ---
    int width = 4096;
    int height = 4096;

    Console.WriteLine($"Image Size: {width}x{height}");

    // --- Setup ---
    // สร้างข้อมูลรูปภาพแค่ครั้งเดียวเพื่อใช้เป็นต้นฉบับ
    int[,] originalImageData = CreateGradientImage(width, height);

    // --- 1. Sequential Run ---
    long sequentialTime = 0;
    Console.WriteLine("\n--- Running Sequential (Ground Truth) ---");
    int[,] seqImageData = (int[,])originalImageData.Clone();
    RunAndTrackProgress("Sequential", width, height, () =>
    {
      ProcessSequential(seqImageData, width, height);
    }, out sequentialTime);
    SaveImage(seqImageData, width, height, "_sequential_output.png");

    // --- 2. Parallel Benchmark ---
    Console.WriteLine("\n--- Running Parallel Benchmark ---");
    var parallelResults = new Dictionary<int, long>();
    int maxThreads = Environment.ProcessorCount;

    for (int numThreads = 1; numThreads <= maxThreads; numThreads++)
    {
      Console.WriteLine($"\n-> Testing with {numThreads} thread(s)...");
      int[,] parImageData = (int[,])originalImageData.Clone();
      long parallelTime = 0;

      RunAndTrackProgress($"Parallel ({numThreads}T)", width, height, () =>
      {
        isDone = new int[height + 2, width + 2];
        // Initialize ขอบของ isDone
        for (int i = 0; i < height + 2; i++) { isDone[i, 0] = 1; isDone[i, width + 1] = 1; }
        for (int j = 0; j < width + 2; j++) { isDone[0, j] = 1; isDone[height + 1, j] = 1; }

        var threads = new List<Thread>();
        for (int i = 0; i < numThreads; i++)
        {
          int startRow = i;
          var thread = new Thread(() => ProcessParallel(parImageData, width, height, startRow, numThreads));
          threads.Add(thread);
          thread.Start();
        }
        foreach (var thread in threads) { thread.Join(); }
      }, out parallelTime);

      parallelResults[numThreads] = parallelTime;

      // ตรวจสอบความถูกต้องหลังรันเสร็จ
      Console.WriteLine("   Verifying result...");
      CompareImages(seqImageData, parImageData);
      SaveImage(parImageData, width, height, $"_parallel_output_{numThreads}T.png");
    }

    // --- 3. Print Summary & Export ---
    Console.WriteLine("\n\n--- BENCHMARK SUMMARY ---");
    Console.WriteLine($"Sequential Time: {sequentialTime} ms (1.00x)");
    Console.WriteLine("-------------------------------------");
    foreach (var result in parallelResults)
    {
      double speedup = (double)sequentialTime / result.Value;
      Console.WriteLine($"Parallel ({result.Key} Threads): {result.Value} ms ({speedup:F2}x)");
    }
    Console.WriteLine("-------------------------------------");

    ExportResultsToCsv(sequentialTime, parallelResults);
    Console.WriteLine("\nBenchmark results exported to results.csv");
    Console.WriteLine("You can now run 'python plot_results.py' to generate the graph.");
  }
  public static int FloorDiv(int a, int b)
  {
    // Python-style floor division: floor(a / b)
    int quotient = a / b;
    int remainder = a % b;

    // If remainder is non-zero and signs differ, subtract 1
    if (remainder != 0 && ((a < 0) != (b < 0)))
    {
      quotient--;
    }

    return quotient;
  }
  public static void ExportResultsToCsv(long sequentialTime, Dictionary<int, long> parallelResults)
  {
    var csv = new StringBuilder();
    csv.AppendLine("Threads,Time_ms,Speedup,Type");

    // เราจะใช้ Sequential run เป็น baseline สำหรับ 1 thread ในกราฟ Speedup
    csv.AppendLine($"1,{sequentialTime},1.00,Sequential");

    foreach (var result in parallelResults)
    {
      double speedup = (double)sequentialTime / result.Value;
      // สำหรับ Parallel(1T) ให้ใช้ข้อมูลของมันเอง ไม่ใช่ sequential
      if (result.Key == 1)
      {
        csv.AppendLine($"{result.Key},{result.Value},{speedup:F2},Parallel_Overhead");
      }
      else
      {
        csv.AppendLine($"{result.Key},{result.Value},{speedup:F2},Parallel");
      }
    }

    File.WriteAllText("results.csv", csv.ToString());
  }

  #region Core Logic

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
      Interlocked.Increment(ref rowsCompleted);
    }
  }

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
      Interlocked.Increment(ref rowsCompleted);
    }
  }

  #endregion

  #region Helper Functions

  public static void RunAndTrackProgress(string label, int width, int height, Action actionToRun, out long elapsedMilliseconds)
  {
    rowsCompleted = 0;
    var stopwatch = Stopwatch.StartNew();

    var cts = new CancellationTokenSource();
    var reporterThread = new Thread(() => ProgressReporter(label, height, cts.Token));
    reporterThread.Start();

    actionToRun.Invoke();

    stopwatch.Stop();
    cts.Cancel();
    reporterThread.Join();

    elapsedMilliseconds = stopwatch.ElapsedMilliseconds;

    Console.Write($"\r{label}: {height}/{height} rows (100.0%) - Time: {elapsedMilliseconds} ms      \n");
  }

  public static void ProgressReporter(string label, int totalRows, CancellationToken token)
  {
    while (!token.IsCancellationRequested)
    {
      int currentRows = rowsCompleted;
      double percentage = (double)currentRows / totalRows * 100;
      Console.Write($"\r{label}: {currentRows}/{totalRows} rows ({percentage:F1}%)");
      Thread.Sleep(100);
    }
  }

  public static void CompareImages(int[,] image1, int[,] image2)
  {
    int height = image1.GetLength(0);
    int width = image1.GetLength(1);

    if (height != image2.GetLength(0) || width != image2.GetLength(1))
    {
      Console.WriteLine("   ❌ Verification FAILED: Image dimensions do not match.");
      return;
    }

    for (int y = 1; y < height - 1; y++)
    {
      for (int x = 1; x < width - 1; x++)
      {
        if (image1[y, x] != image2[y, x])
        {
          Console.WriteLine($"   ❌ Verification FAILED: Mismatch found at pixel ({x - 1}, {y - 1}).");
          return;
        }
      }
    }
    Console.WriteLine("   ✅ Verification SUCCESSFUL!");
  }

  public static int[,] CreateGradientImage(int width, int height)
  {
    int[,] image = new int[height + 2, width + 2];
    for (int y = 0; y < height; y++)
    {
      for (int x = 0; x < width; x++)
      {
        image[y + 1, x + 1] = (int)((float)(x + y) / (width + height) * 255f);
      }
    }
    return image;
  }

#pragma warning disable CA1416
  public static void SaveImage(int[,] image, int width, int height, string filename)
  {
    try
    {
      using (Bitmap bmp = new Bitmap(width, height))
      {
        for (int y = 0; y < height; y++)
        {
          for (int x = 0; x < width; x++)
          {
            int colorVal = Math.Max(0, Math.Min(255, image[y + 1, x + 1]));
            bmp.SetPixel(x, y, Color.FromArgb(colorVal, colorVal, colorVal));
          }
        }
        bmp.Save(filename, ImageFormat.Png);
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"\nError saving image '{filename}': {ex.Message}");
      Console.WriteLine("This might be due to image size limitations.");
    }
  }
#pragma warning restore CA1416

  #endregion
}
