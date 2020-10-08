using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Warden.Logging
{
    public class LoggingMeta : IDictionary<string, object>, IReadOnlyDictionary<string, object>
    {
        public int MetaHash { get; private set; }

        ConcurrentDictionary<string, object> meta;

        public static LoggingMeta Empty { get; } = new LoggingMeta();

        public LoggingMeta()
        {
            this.meta = new ConcurrentDictionary<string, object>();
        }

        public LoggingMeta(int capacity)
        {
            this.meta = new ConcurrentDictionary<string, object>(Environment.ProcessorCount+1, capacity);
        }

        public LoggingMeta(IEnumerable<KeyValuePair<string, object>> meta)
        {
            this.meta = new ConcurrentDictionary<string, object>();
            foreach (var pair in meta)
                if (!this.meta.TryAdd(pair.Key, pair.Value))
                    throw new InvalidOperationException("Could not add key to the meta: " + pair.Key);
            RecalcHash();
        }

        public LoggingMeta(params KeyValuePair<string, object>[] tags)
            : this(tags as IEnumerable<KeyValuePair<string, object>>)
        {
            
        }

        void RecalcHash()
        {
            int val = 0;
            bool first = true;

            foreach(var pair in meta)
            {
                if (first)
                {
                    val = pair.Key.GetHashCode();
                    first = false;
                }
                else
                {
                    val ^= pair.Key.GetHashCode();
                }

                val ^= pair.Value.GetHashCode();
            }

            this.MetaHash = val;
        }

        public object this[string key]
        {
            get
            {
                return meta[key];
            }
            set
            {
                meta[key] = value;
                RecalcHash();
            }
        }

        public LoggingMeta Merge(params LoggingMeta[] meta)
        {
            LoggingMeta newMeta = new LoggingMeta(this.meta);
            foreach(var m in meta)
            {
                foreach (var pair in m)
                {
                    newMeta[pair.Key] = pair.Value;
                }
            }
            return newMeta;
        }

        public LoggingMeta Copy()
        {
            return new LoggingMeta(this.meta);
        }

        public ICollection<string> Keys => meta.Keys;

        public ICollection<object> Values => meta.Values;

        public int Count => meta.Count;

        public bool IsReadOnly => false;

        IEnumerable<string> IReadOnlyDictionary<string, object>.Keys => meta.Keys;

        IEnumerable<object> IReadOnlyDictionary<string, object>.Values => meta.Values;

        public void Add(string key, object value)
        {
            if (!meta.TryAdd(key, value))
                throw new ArgumentException("Could not add key to the meta: " + key);
            RecalcHash();
        }

        public void Add(KeyValuePair<string, object> item)
        {
            if (!meta.TryAdd(item.Key, item.Value))
                throw new ArgumentException("Could not add key to the meta: " + item.Key);
            RecalcHash();
        }

        public void Clear()
        {
            meta.Clear();
            MetaHash = 0;
        }

        public bool Contains(KeyValuePair<string, object> item)
        {
            return (meta as IDictionary<string, object>).Contains(item);
        }

        public bool ContainsKey(string key)
        {
            return meta.ContainsKey(key);
        }

        public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex)
        {
            (meta as IDictionary<string, object>).CopyTo(array, arrayIndex);
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            return meta.GetEnumerator();
        }

        public bool Remove(string key)
        {
            var ret = meta.TryRemove(key, out object removed);
            RecalcHash();
            return ret;
        }

        public bool Remove(KeyValuePair<string, object> item)
        {
            var ret = (meta as IDictionary<string, object>).Remove(item);
            RecalcHash();
            return ret;
        }

        public bool TryGetValue(string key, out object value)
        {
            return meta.TryGetValue(key, out value);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return meta.GetEnumerator();
        }
    }
}
