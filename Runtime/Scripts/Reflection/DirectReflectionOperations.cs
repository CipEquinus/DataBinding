using System;
using System.Collections.Generic;
using System.Reflection;

namespace JH.DataBinding
{
  public readonly struct DirectReflectionOperations : IReflectionOperations
  {
    public static readonly DirectReflectionOperations Instance = new();

    public PropertyInfo FindProperty(Type type, string name)
    {
      return type.FindProperty(name);
    }

    public TValueType ResolveInstanceMember<TValueType>(object instance, string path)
    {
      return instance.ResolveInstanceMember<TValueType>(path);
    }

    public string GetFriendlyName(Type type)
    {
      return type.GetFriendlyName();
    }

    public IEnumerable<MethodInfo> ResolvePublicPropertyPath(object instance, string path, PathResolveOperation operation)
    {
      return instance.ResolvePublicPropertyPath(path, operation);
    }

    public object InvokeGetAccessChain(MethodInfo[] accessors, object instance)
    {
      return accessors.InvokeGetAccessChain(instance);
    }

    public void InvokeSetAccessChain(MethodInfo[] accessors, object instance, params object[] values)
    {
      accessors.InvokeSetAccessChain(instance, values);
    }

    public bool InheritsOrImplements(Type type, Type baseType)
    {
      return type.InheritsOrImplements(baseType);
    }
  }
}
