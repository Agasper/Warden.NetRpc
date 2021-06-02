using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Warden.Util
{
    public interface IResettableCachedEnumerable<T> : IEnumerable<T>
    {
        void Reset();
        int Count { get; }
    }
    
    public class ResettableCachedEnumerator<T> : IResettableCachedEnumerable<T>
    {
        public int Count => collection.Count;
        
        IEnumerator<T> enumerator;
        ICollection<T> collection;
        Action resetDelegate;

        public ResettableCachedEnumerator(ICollection<T> collection)
        {
            this.collection = collection ?? throw new ArgumentNullException(nameof(collection));
            this.enumerator = collection.GetEnumerator();
            Type type = enumerator.GetType();
            MethodInfo methodInfo = type.GetMethod("Reset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (methodInfo == null)
                throw new ArgumentException("Enumerator doesn't have reset method");
            resetDelegate = (Action)Delegate.CreateDelegate(typeof(Action), enumerator, methodInfo, true);
        }
        
        public IEnumerator<T> GetEnumerator()
        {
            return enumerator;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Reset()
        {
            resetDelegate.Invoke();
        }
    }
}