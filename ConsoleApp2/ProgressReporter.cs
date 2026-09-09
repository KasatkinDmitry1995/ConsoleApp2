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
}
