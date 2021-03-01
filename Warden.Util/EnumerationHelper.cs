using System.Collections.Generic;

namespace Warden.Util
{
    public static class EnumerationHelper
    {
        public static IEnumerable<T> SingleItemAsEnumerable<T>(this T item)
        {
            yield return item;
        }
    }
}
