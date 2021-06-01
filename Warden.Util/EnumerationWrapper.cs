using System;
using System.Collections;
using System.Collections.Generic;

namespace Warden.Util
{
    public class EnumerationWrapper<T> : IEnumerable<T>
    {
        IEnumerator<T> enumerator;

        public EnumerationWrapper(IEnumerator<T> enumerator)
        {
            this.enumerator = enumerator ?? throw new ArgumentNullException(nameof(enumerator));
        }
        
        public IEnumerator<T> GetEnumerator()
        {
            return enumerator;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}