using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Warden.Rpc
{
    public struct RemotingObjectConfiguration
    {
        public bool allowAsync;
        public bool allowNonVoid;
        public bool canUseLambdas;
        public bool onlyPublicMethods;

        public RemotingObjectConfiguration(bool canUseLambdas, bool allowAsync, bool allowNonVoid)
        {
            this.canUseLambdas = canUseLambdas;
            this.allowAsync = allowAsync;
            this.allowNonVoid = allowNonVoid;
            this.onlyPublicMethods = false;
        }
    }

    public class RemotingObjectScheme
    {
        public Type EntityType { get; }
        public IReadOnlyDictionary<object, MethodContainer> Methods => remotingMethods;

        Dictionary<object, MethodContainer> remotingMethods;

        public RemotingObjectScheme(RemotingObjectConfiguration configuration, Type entityType)
        {
            this.EntityType = entityType;
            Init(configuration, entityType);
        }

        public MethodContainer GetInvokationContainer(object key)
        {
            //if (!init)
            //    throw new InvalidOperationException($"Call {nameof(Init)} first");

            MethodContainer methodContainer = null;

            if (!remotingMethods.ContainsKey(key))
                throw new ArgumentException(string.Format("Method key `{0}` not found in type {1}", key, this.GetType().Name));
            methodContainer = remotingMethods[key];

            return methodContainer;
        }

        void Init(RemotingObjectConfiguration configuration, Type entityType)
        {
            Dictionary<object, MethodContainer> myRemotingMethods2 = new Dictionary<object, MethodContainer>();

            BindingFlags flags = BindingFlags.Public | BindingFlags.FlattenHierarchy | BindingFlags.Instance;
            if (!configuration.onlyPublicMethods)
                flags |= BindingFlags.NonPublic;

            foreach (var method in entityType
                    .GetMethods(flags))
            {
                var attr = method.GetCustomAttribute<RemotingMethodAttribute>(true);
                var asyncAttr = method.GetCustomAttribute<AsyncStateMachineAttribute>(true);

                if (attr == null)
                    continue;

                var isReturnGenericTask = method.ReturnType.IsGenericType &&
                    method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>);
                var isReturnTask = method.ReturnType == typeof(Task);

                if (!configuration.allowAsync && (asyncAttr != null || isReturnGenericTask || isReturnTask))
                    throw new TypeLoadException($"Async method not allowed in {entityType.FullName}");

                if (!configuration.allowNonVoid && method.ReturnType != typeof(void))
                    throw new TypeLoadException($"Non void method not allowed in {entityType.FullName}");

                if (asyncAttr != null && method.ReturnType == typeof(void))
                    throw new TypeLoadException($"Async void methods not allowed: {entityType.FullName}, {method.Name}");

                switch (attr.MethodIdentityType)
                {
                    case RemotingMethodAttribute.MethodIdentityTypeEnum.ByIndex:
                        myRemotingMethods2.Add(attr.Index, new MethodContainer(method, configuration.canUseLambdas));
                        break;
                    case RemotingMethodAttribute.MethodIdentityTypeEnum.ByName:
                        myRemotingMethods2.Add(attr.Name, new MethodContainer(method, configuration.canUseLambdas));
                        break;
                    case RemotingMethodAttribute.MethodIdentityTypeEnum.Default:
                        myRemotingMethods2.Add(method.Name, new MethodContainer(method, configuration.canUseLambdas));
                        break;
                    default:
                        throw new ArgumentException($"Could not get method identification for {method.Name}");
                }
            }

            this.remotingMethods = myRemotingMethods2;
        }
    }
}
