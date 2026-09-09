using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;

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

            Stopwatch sw = new Stopwatch();
            var resultList = new List<CheckResultRecord>();

            using (HttpClient client = new HttpClient())
            {

                HttpResponseMessage response;
                client.Timeout = TimeSpan.FromSeconds(5);

                foreach (String link in links)
                {
                    try
                    {
                        sw.Restart();
                        response = await client.GetAsync(link);
                        sw.Stop();
                    }
                    catch (TaskCanceledException ex)
                    {
                        resultList.Add(new CheckResultRecord(link, CheckResult.TIMEOUT, 0));
                        continue;
                    }
                    catch (Exception ex)
                    {
                        resultList.Add(new CheckResultRecord(link, CheckResult.ERROR, 0));
                        continue;
                    }

                    var result = FromHttpStatusCode((int)response.StatusCode);
                    resultList.Add(new CheckResultRecord(link, result, sw.ElapsedMilliseconds));

                }
            }

            resultList.Sort((r1, r2) => r1.Result.CompareTo(r2.Result));

            foreach (CheckResultRecord result in resultList)
                Console.WriteLine(result);

            var resultCounts = from r in resultList
                      group r by r.Result into g
                      select new { Result = g.Key, Count = g.Count() };

            Console.WriteLine($"Всего проанализировано ссылок: {resultList.Count}");
            Console.WriteLine("Из них:");

            foreach(var resultCount in resultCounts)
                Console.WriteLine($"{resultCount.Result}: {resultCount.Count}");

            Console.ReadKey();
        }
    }
}
