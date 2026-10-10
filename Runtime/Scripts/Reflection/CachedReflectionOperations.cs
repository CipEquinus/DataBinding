using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace JH.DataBinding
{
  public readonly struct CachedReflectionOperations : IReflectionOperations
  {
    private enum Init
    {
      Init = 0
    }

    public static readonly CachedReflectionOperations Instance = new CachedReflectionOperations(Init.Init);

    private readonly DirectReflectionOperations reflectionOperations;
    private readonly ConcurrentDictionary<(Type, string), PropertyInfo> propertyCache;
    private readonly ConcurrentDictionary<(Type, Type), bool> inheritanceCache;
    private readonly ConcurrentDictionary<Type, string> nameCache;

    private CachedReflectionOperations(Init _)
    {
      reflectionOperations = new();
      propertyCache = new();
      inheritanceCache = new();
      nameCache = new();
    }

    public PropertyInfo FindProperty(Type type, string name)
    {
      if (!propertyCache.TryGetValue((type, name), out var propertyInfo))
      {
        propertyInfo = reflectionOperations.FindProperty(type, name);
        propertyCache[(type, name)] = propertyInfo;
      }

      return propertyInfo;
    }

    public TValueType ResolveInstanceMember<TValueType>(object instance, string path)
    {
      return reflectionOperations.ResolveInstanceMember<TValueType>(instance, path);
    }

    public string GetFriendlyName(Type type)
    {
      if (type == null)
      {
        return reflectionOperations.GetFriendlyName(type);
      }

      if (!nameCache.TryGetValue(type, out var name))
      {
        name = reflectionOperations.GetFriendlyName(type);
        nameCache[type] = name;
      }

      return name;
    }

    public IEnumerable<MethodInfo> ResolvePublicPropertyPath(object instance, string path, PathResolveOperation operation)
    {
      return reflectionOperations.ResolvePublicPropertyPath(instance, path, operation);
    }

    public object InvokeGetAccessChain(MethodInfo[] accessors, object instance)
    {
      return reflectionOperations.InvokeGetAccessChain(accessors, instance);
    }

    public void InvokeSetAccessChain(MethodInfo[] accessors, object instance, params object[] values)
    {
      reflectionOperations.InvokeSetAccessChain(accessors, instance, values);
    }

    public bool InheritsOrImplements(Type type, Type baseType)
    {
      if (!inheritanceCache.TryGetValue((type, baseType), out var inheritsOrImplements))
      {
        inheritsOrImplements = reflectionOperations.InheritsOrImplements(type, baseType);
        inheritanceCache[(type, baseType)] = inheritsOrImplements;
      }

      return inheritsOrImplements;
    }
  }
}
