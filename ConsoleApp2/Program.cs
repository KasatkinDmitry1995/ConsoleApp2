using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp1
{

    public class ProgressReporter : IProgress<int>
    {
        private readonly int _total;
        private readonly int _barSize;

        public ProgressReporter(int total, int barSize = 50)
        {
            _total = total;
            _barSize = barSize;
        }

        public void Report(int value)
        {
            double percent = (double)value / _total;
            int filled = (int)(percent * _barSize);

            Console.Write($"\r[");
            Console.Write(new string('█', filled));
            Console.Write(new string('░', _barSize - filled));
            Console.Write($"] {percent:P0}");
        }
    }

    public record CheckResultRecord(string Link, CheckResult Result, long ElapsedMilliseconds)
    {
        public override string ToString()
        {
            if (Result == CheckResult.OK)
                return $"{Link,-35}  Статус:{Result}  Ожидание:{ElapsedMilliseconds} мс";
  
            return $"{Link,-35}  Статус:{Result}";
        }
    }

    public enum CheckResult
    {
        OK,
        TIMEOUT,
        ERROR
    }


    class Program
    {
        private static CheckResult FromHttpStatusCode(int statusCode)
        {
            if (statusCode < 400)
                return CheckResult.OK;
            else if (statusCode == 408)
                return CheckResult.TIMEOUT;
            else
                return CheckResult.ERROR;
        }

        static async Task Main(string[] args)
        {

            AppContext.SetSwitch("System.Net.DisableIPv6", true);

            String filename;

            if (args.Length == 0)
                filename = @"links.txt";
            else
                filename = args[0];

            string[] links;

            try
            {
                links = File.ReadAllLines(filename);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.ReadKey();
                return;
            }

            var resultList = new ConcurrentBag<CheckResultRecord>();
            int completedCount = 0;
            var semaphore = new SemaphoreSlim(15);
            object _progressLock = new object();
            var progress = new ProgressReporter(links.Count());

            using (HttpClient client = new HttpClient())
            {

                client.Timeout = TimeSpan.FromSeconds(5);

                var tasks = links.Select(async link =>
                {

                    await semaphore.WaitAsync();
                    try
                    {
                        Stopwatch sw = new Stopwatch();
                        sw.Start();
                        var response = await client.GetAsync(link);
                        sw.Stop();

                        var result = FromHttpStatusCode((int)response.StatusCode);
                        resultList.Add(new CheckResultRecord(link, result, sw.ElapsedMilliseconds));
                    }
                    catch (TaskCanceledException ex)
                    {
                        resultList.Add(new CheckResultRecord(link, CheckResult.TIMEOUT, 0));
                    }
                    catch (Exception ex)
                    {
                        resultList.Add(new CheckResultRecord(link, CheckResult.ERROR, 0));
                    }
                    finally
                    {
                        semaphore.Release();
                        int completed = Interlocked.Increment(ref completedCount);
                        lock (_progressLock)
                        {
                            progress.Report(completed);
                        }
                    }

                });

                await Task.WhenAll(tasks);

                var resultListSorted = resultList.OrderBy(r => r.Result).ToList();

                Console.Clear();

                foreach (CheckResultRecord result in resultListSorted)
                    Console.WriteLine(result);

                var resultCounts = from r in resultListSorted
                                   group r by r.Result into g
                                   select new { Result = g.Key, Count = g.Count() };

                Console.WriteLine($"Всего проанализировано ссылок: {resultListSorted.Count}");
                Console.WriteLine("Из них:");

                foreach (var resultCount in resultCounts)
                    Console.WriteLine($"{resultCount.Result}: {resultCount.Count}");

                Console.ReadKey();
            }
        }
    }
}
