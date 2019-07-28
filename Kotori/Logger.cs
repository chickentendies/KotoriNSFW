using System;
using System.Text;

namespace Kotori
{
    public static class Logger
    {
        public static void Write(string str)
            => Console.WriteLine(string.Format(@"[{1}] {0}", str, DateTime.Now));

        public static void Write(byte[] bytes)
            => Write(Encoding.UTF8.GetString(bytes));

        public static void Write(object obj)
            => Write(obj.ToString());
    }
}
