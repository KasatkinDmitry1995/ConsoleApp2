using Microsoft.VisualBasic.FileIO;
using System.Collections.Concurrent;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;

namespace ConsoleApp1
{

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

            var filenameOpt = new Option<string>("--filename")
            {
                Description = "Имя файла. По-умолчанию \"links.txt\".",
                DefaultValueFactory = parseResult => "links.txt",
            };

            var maxThreadsOpt = new Option<int>("--max_threads")
            {
                Description = "Число потоков. По-умолчанию 5.",
                DefaultValueFactory = parseResult => 5,
            };

            var timeoutOpt = new Option<int>("--timeout")
            {
                Description = "Таймаут в секундах, сколько мы ждем ответ от сервера. По-умолчанию 5.",
                DefaultValueFactory = parseResult => 5,
            };

            RootCommand rootCommand = new();
            rootCommand.Options.Add(filenameOpt);
            rootCommand.Options.Add(maxThreadsOpt);
            rootCommand.Options.Add(timeoutOpt);

            rootCommand.SetAction(async parseResult =>
            {
                await RunProgram(
                        parseResult.GetValue(filenameOpt),
                        parseResult.GetValue(maxThreadsOpt),
                        parseResult.GetValue(timeoutOpt));
            });

            ParseResult parseResult = rootCommand.Parse(args);
            await parseResult.InvokeAsync();

        }

        static async Task RunProgram(string filename, int maxThreads, int timeout)
        {

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
            var semaphore = new SemaphoreSlim(maxThreads);
            object _progressLock = new object();
            var progress = new ProgressReporter(links.Count());

            using (HttpClient client = new HttpClient())
            {

                client.Timeout = TimeSpan.FromSeconds(timeout);

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
                    catch (TaskCanceledException)
                    {
                        resultList.Add(new CheckResultRecord(link, CheckResult.TIMEOUT, 0));
                    }
                    catch (Exception)
                    {
                        resultList.Add(new CheckResultRecord(link, CheckResult.ERROR, 0));
                    }
                    finally
                    {
                        semaphore.Release();
                        lock (_progressLock)
                        {
                            progress.Report(++completedCount);
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
