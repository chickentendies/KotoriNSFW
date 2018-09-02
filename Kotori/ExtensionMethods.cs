using System;

namespace Kotori
{
    public static class ExtensionMethods
    {
        public static DateTime RoundUp(this DateTime dt, TimeSpan ts)
        {
            return new DateTime((dt.Ticks + ts.Ticks - 1) / ts.Ticks * ts.Ticks, dt.Kind);
        }
    }
}
