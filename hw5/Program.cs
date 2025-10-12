using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

public class ReadersWritersSolution
{
  private static int _sharedResourceX = 0;
  private static readonly SemaphoreSlim _turnstile = new SemaphoreSlim(1, 1);
  private static readonly SemaphoreSlim _resourceLock = new SemaphoreSlim(1, 1);
  private static readonly SemaphoreSlim _readerCountLock = new SemaphoreSlim(1, 1);
  private static int _readerCount = 0;

  private const int NumReaders = 990;
  private const int NumWriters = 10;
  private const int TotalThreads = NumReaders + NumWriters;

  public static void Main(string[] args)
  {
    Console.WriteLine("Starting Readers-Writers simulation (Optimized Wait)...");
    var stopwatch = Stopwatch.StartNew();

    var threads = new List<Thread>();

    var threadRoles = new List<int>();
    threadRoles.AddRange(Enumerable.Repeat(0, NumWriters));
    threadRoles.AddRange(Enumerable.Repeat(1, NumReaders));
    var random = new Random();
    var shuffledRoles = threadRoles.OrderBy(x => random.Next()).ToList();

    for (int i = 0; i < TotalThreads; i++)
    {
      int threadId = i + 1;
      if (shuffledRoles[i] == 0)
      {
        threads.Add(new Thread(() => Writer(threadId)));
      }
      else
      {
        threads.Add(new Thread(() => Reader(threadId)));
      }
    }

    threads.ForEach(t => t.Start());
    threads.ForEach(t => t.Join());

    stopwatch.Stop();
    Console.WriteLine("\n=============================================");
    Console.WriteLine("All threads have finished.");
    Console.WriteLine($"Final value of x: {_sharedResourceX}");
    Console.WriteLine($"Total execution time: {stopwatch.Elapsed.TotalSeconds:F2} seconds.");
    Console.WriteLine("=============================================");
  }

  public static void Writer(int id)
  {
    try
    {
      _turnstile.Wait();
      _resourceLock.Wait();
    }
    finally
    {
      _turnstile.Release();
    }

    try
    {
      _sharedResourceX++;
      Console.WriteLine($"Writer no = {id,-4} x = {_sharedResourceX}");
      SimulateWork(1);
    }
    finally
    {
      _resourceLock.Release();
    }
  }

  public static void Reader(int id)
  {
    try
    {
      _turnstile.Wait();
      _turnstile.Release();

      _readerCountLock.Wait();
      try
      {
        _readerCount++;
        if (_readerCount == 1)
        {
          _resourceLock.Wait();
        }
      }
      finally
      {
        _readerCountLock.Release();
      }

      Console.WriteLine($"Reader no = {id,-4} x = {_sharedResourceX}");
      SimulateWork(1);

      _readerCountLock.Wait();
      try
      {
        _readerCount--;
        if (_readerCount == 0)
        {
          _resourceLock.Release();
        }
      }
      finally
      {
        _readerCountLock.Release();
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Exception in Reader {id}: {ex.Message}");
    }
  }

  /// <summary>
  /// ฟังก์ชันจำลองการทำงาน 1 วินาที (ไม่กิน CPU และไม่ใช่ Sleep)
  /// โดยใช้การ Wait ที่มี Timeout ซึ่งเป็นวิธีที่มีประสิทธิภาพ
  /// </summary>
  private static void SimulateWork(int seconds)
  {
    // var waitHandle = new ManualResetEventSlim(false);
    // waitHandle.Wait(seconds * 1000);

    var sw = new SpinWait();
    var stopwatch = Stopwatch.StartNew();
    while (stopwatch.ElapsedMilliseconds < seconds * 1000)
    {
      sw.SpinOnce();
    }
    stopwatch.Stop();
  }
}
