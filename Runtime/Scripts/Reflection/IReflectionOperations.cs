using System;
using System.Collections.Generic;
using System.Reflection;

namespace JH.DataBinding
{
  public enum PathResolveOperation
  {
    GetValue,
    SetValue,
  }

  public interface IReflectionOperations
  {
    PropertyInfo FindProperty(Type type, string name);

    TValueType ResolveInstanceMember<TValueType>(object instance, string path);

    string GetFriendlyName(Type type);

    IEnumerable<MethodInfo> ResolvePublicPropertyPath(
      object instance,
      string path,
      PathResolveOperation operation
    );

    public static void SyncValue<T>(
      T reflectionOperations,
      object source,
      object target,
      MethodInfo[] getAccessors,
      MethodInfo[] setAccessors
    )
      where T : struct, IReflectionOperations
    {
      reflectionOperations.InvokeSetAccessChain(setAccessors, target, reflectionOperations.InvokeGetAccessChain(getAccessors, source));
    }

    object InvokeGetAccessChain(MethodInfo[] accessors, object instance);
    void InvokeSetAccessChain(MethodInfo[] accessors, object instance, params object[] values);

    bool InheritsOrImplements(Type type, Type baseType);
  }
}
