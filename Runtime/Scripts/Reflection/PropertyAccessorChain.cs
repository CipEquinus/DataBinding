using System;
using System.Collections.Concurrent;
using System.Reflection;
using UnityEngine.Scripting;

namespace JH.DataBinding
{
  /// <summary>
  /// An executable property path. Delegates capture methods, never bound objects.
  /// </summary>
  public sealed class PropertyAccessorChain
  {
    public static readonly PropertyAccessorChain Empty =
      new PropertyAccessorChain(Array.Empty<MethodInfo>(), PathResolveOperation.GetValue);

    private static readonly ConcurrentDictionary<
      (PropertyAccessorChain, PropertyAccessorChain), Action<object, object>> transfers = new();
    private readonly PropertyAccessor[] accessors;

    public int Length => accessors.Length;

    /// <summary>Whether any segment requires reflection instead of an open delegate.</summary>
    public bool UsesReflection
    {
      get
      {
        for (var i = 0; i < accessors.Length; ++i)
        {
          if (accessors[i].UsesReflection) return true;
        }

        return false;
      }
    }

    /// <summary>The first delegate fallback reason, or null when all segments use delegates.</summary>
    public string ReflectionFallbackReason
    {
      get
      {
        for (var i = 0; i < accessors.Length; ++i)
        {
          if (accessors[i].UsesReflection) return accessors[i].FallbackReason;
        }

        return null;
      }
    }

    internal PropertyAccessorChain(MethodInfo[] methods, PathResolveOperation operation)
    {
      accessors = new PropertyAccessor[methods.Length];
      for (var i = 0; i < methods.Length; ++i)
      {
        accessors[i] = PropertyAccessor.For(methods[i],
          operation == PathResolveOperation.SetValue && i == methods.Length - 1);
      }
    }

    public object GetValue(object instance)
    {
      RequireAccessors();
      return instance == null ? null : accessors[accessors.Length - 1].GetValue(GetOwner(instance));
    }

    public void SetValue(object instance, object value)
    {
      RequireAccessors();
      if (instance == null) throw new ArgumentNullException(nameof(instance));
      accessors[accessors.Length - 1].SetValue(GetOwner(instance), value);
    }

    /// <summary>
    /// Creates a shared transfer that keeps matching leaf value types unboxed.
    /// Resolve this once when binding, then invoke it directly for each update.
    /// </summary>
    public Action<object, object> GetTransferTo(PropertyAccessorChain target)
    {
      if (target == null) throw new ArgumentNullException(nameof(target));
      RequireAccessors();
      target.RequireAccessors();
      var key = (this, target);
      if (!transfers.TryGetValue(key, out var transfer))
      {
        transfer = accessors[accessors.Length - 1].CreateTransfer(this, target,
          target.accessors[target.accessors.Length - 1]);
        transfer ??= (sourceInstance, targetInstance) =>
          target.SetValue(targetInstance, GetValue(sourceInstance));
        transfer = transfers.GetOrAdd(key, transfer);
      }

      return transfer;
    }

    internal object GetOwner(object instance)
    {
      for (var i = 0; i < accessors.Length - 1; ++i)
      {
        instance = accessors[i].GetValue(instance);
      }

      return instance;
    }

    private void RequireAccessors()
    {
      if (accessors.Length == 0)
      {
        throw new InvalidOperationException("The property path has no accessors.");
      }
    }
  }

  internal abstract class PropertyAccessor
  {
    private static readonly ConcurrentDictionary<(MethodInfo, bool), PropertyAccessor> cache = new();
    private static readonly MethodInfo referenceFactory = typeof(PropertyAccessor).GetMethod(
      nameof(CreateReference), BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly MethodInfo staticFactory = typeof(PropertyAccessor).GetMethod(
      nameof(CreateStatic), BindingFlags.Static | BindingFlags.NonPublic);

    protected readonly MethodInfo Method;
    internal virtual bool UsesReflection => false;
    internal virtual string FallbackReason => null;

    protected PropertyAccessor(MethodInfo method) => Method = method;

    internal static PropertyAccessor For(MethodInfo method, bool setter)
    {
      if (method == null) return new ReflectionAccessor(null, "Missing accessor method.");
      var key = (method, setter);
      if (!cache.TryGetValue(key, out var accessor))
      {
        accessor = Create(method, setter);
        accessor = cache.GetOrAdd(key, accessor);
      }

      return accessor;
    }

    private static PropertyAccessor Create(MethodInfo method, bool setter)
    {
      var parameters = method.GetParameters();
      // A struct receiver needs a ref delegate and access to its existing box.
      // Keep reflection's boxed mutation semantics without an Unsafe dependency.
      if ((!method.IsStatic && method.DeclaringType.IsValueType)
        || parameters.Length != (setter ? 1 : 0) || method.ContainsGenericParameters)
      {
        return new ReflectionAccessor(method, "Struct receiver or unsupported accessor signature.");
      }

      var valueType = setter ? parameters[0].ParameterType : method.ReturnType;
      try
      {
        var factory = method.IsStatic
          ? staticFactory.MakeGenericMethod(valueType)
          : referenceFactory.MakeGenericMethod(method.DeclaringType, valueType);
        return (PropertyAccessor)factory.Invoke(null, new object[] { method, setter });
      }
      catch (Exception exception) when (CannotCreateDelegate(exception))
      {
        // Some AOT runtimes cannot instantiate an arbitrary generic wrapper.
        // Cache that fallback too; never retry delegate creation during updates.
        return new ReflectionAccessor(method, exception.GetBaseException().ToString());
      }
    }

    private static bool CannotCreateDelegate(Exception exception)
    {
      if (exception is TargetInvocationException invocation && invocation.InnerException != null)
      {
        return CannotCreateDelegate(invocation.InnerException);
      }

      return exception is ArgumentException || exception is NotSupportedException
        || exception is MissingMethodException || exception is TypeLoadException
        || exception is MemberAccessException || exception is InvalidProgramException
        || exception.GetType().FullName == "System.ExecutionEngineException";
    }

    [Preserve]
    private static PropertyAccessor CreateReference<TReceiver, TValue>(MethodInfo method, bool setter)
      where TReceiver : class => new ReferenceAccessor<TReceiver, TValue>(method, setter);

    [Preserve]
    private static PropertyAccessor CreateStatic<TValue>(MethodInfo method, bool setter) =>
      new StaticAccessor<TValue>(method, setter);

    // Fixed library roots for common value types. These do not depend on binding
    // paths or application classes and never need regeneration when bindings edit.
    // Reference receiver types share the object receiver specialization in IL2CPP.
    [Preserve]
    private static void UsedOnlyForAOTCodeGeneration(MethodInfo method)
    {
      PreserveValueType<object>(method);
      PreserveValueType<bool>(method);
      PreserveValueType<byte>(method);
      PreserveValueType<sbyte>(method);
      PreserveValueType<short>(method);
      PreserveValueType<ushort>(method);
      PreserveValueType<int>(method);
      PreserveValueType<uint>(method);
      PreserveValueType<long>(method);
      PreserveValueType<ulong>(method);
      PreserveValueType<float>(method);
      PreserveValueType<double>(method);
      PreserveValueType<decimal>(method);
      PreserveValueType<char>(method);
      PreserveValueType<DateTime>(method);
      PreserveValueType<TimeSpan>(method);
      PreserveValueType<Guid>(method);
      PreserveValueType<UnityEngine.Vector2>(method);
      PreserveValueType<UnityEngine.Vector3>(method);
      PreserveValueType<UnityEngine.Vector4>(method);
      PreserveValueType<UnityEngine.Vector2Int>(method);
      PreserveValueType<UnityEngine.Vector3Int>(method);
      PreserveValueType<UnityEngine.Quaternion>(method);
      PreserveValueType<UnityEngine.Color>(method);
      PreserveValueType<UnityEngine.Color32>(method);
      PreserveValueType<UnityEngine.Rect>(method);
      PreserveValueType<UnityEngine.RectInt>(method);
      PreserveValueType<UnityEngine.Bounds>(method);
      PreserveValueType<UnityEngine.BoundsInt>(method);
      PreserveValueType<UnityEngine.Matrix4x4>(method);
      throw new InvalidOperationException("This method is only for AOT code generation.");
    }

    private static void PreserveValueType<TValue>(MethodInfo method)
    {
      CreateReference<object, TValue>(method, false);
      CreateReference<object, TValue>(method, true);
      CreateStatic<TValue>(method, false);
      CreateStatic<TValue>(method, true);
    }

    internal abstract object GetValue(object instance);
    internal abstract void SetValue(object instance, object value);

    internal virtual Action<object, object> CreateTransfer(PropertyAccessorChain source,
      PropertyAccessorChain target, PropertyAccessor setter) => null;

    private interface ITypedSetter<TValue>
    {
      void SetTypedValue(object instance, TValue value);
    }

    private abstract class TypedAccessor<TValue> : PropertyAccessor, ITypedSetter<TValue>
    {
      protected TypedAccessor(MethodInfo method) : base(method) { }

      protected abstract TValue GetTypedValue(object instance);
      public abstract void SetTypedValue(object instance, TValue value);

      internal override object GetValue(object instance) => GetTypedValue(instance);

      internal override void SetValue(object instance, object value)
      {
        if (value == null || value is TValue)
        {
          SetTypedValue(instance, value == null ? default : (TValue)value);
        }
        else
        {
          // Preserve reflection's conversions, e.g. an int source to a long setter.
          Method.Invoke(instance, new[] { value });
        }
      }

      internal override Action<object, object> CreateTransfer(PropertyAccessorChain source,
        PropertyAccessorChain target, PropertyAccessor setter)
      {
        if (!(setter is ITypedSetter<TValue> typedSetter)) return null;
        return (sourceInstance, targetInstance) =>
        {
          if (sourceInstance == null)
          {
            target.SetValue(targetInstance, null);
            return;
          }

          var value = GetTypedValue(source.GetOwner(sourceInstance));
          if (targetInstance == null) throw new ArgumentNullException(nameof(targetInstance));
          typedSetter.SetTypedValue(target.GetOwner(targetInstance), value);
        };
      }
    }

    private sealed class ReferenceAccessor<TReceiver, TValue> : TypedAccessor<TValue>
      where TReceiver : class
    {
      private readonly Func<TReceiver, TValue> getter;
      private readonly Action<TReceiver, TValue> setter;

      internal ReferenceAccessor(MethodInfo method, bool isSetter) : base(method)
      {
        if (isSetter)
          setter = (Action<TReceiver, TValue>)method.CreateDelegate(typeof(Action<TReceiver, TValue>));
        else
          getter = (Func<TReceiver, TValue>)method.CreateDelegate(typeof(Func<TReceiver, TValue>));
      }

      protected override TValue GetTypedValue(object instance)
      {
        if (!(instance is TReceiver receiver)) return (TValue)Method.Invoke(instance, null);
        try { return getter(receiver); }
        catch (Exception exception) { throw new TargetInvocationException(exception); }
      }

      public override void SetTypedValue(object instance, TValue value)
      {
        if (!(instance is TReceiver receiver))
        {
          Method.Invoke(instance, new object[] { value });
          return;
        }

        try { setter(receiver, value); }
        catch (Exception exception) { throw new TargetInvocationException(exception); }
      }
    }

    private sealed class StaticAccessor<TValue> : TypedAccessor<TValue>
    {
      private readonly Func<TValue> getter;
      private readonly Action<TValue> setter;

      internal StaticAccessor(MethodInfo method, bool isSetter) : base(method)
      {
        if (isSetter) setter = (Action<TValue>)method.CreateDelegate(typeof(Action<TValue>));
        else getter = (Func<TValue>)method.CreateDelegate(typeof(Func<TValue>));
      }

      protected override TValue GetTypedValue(object instance)
      {
        try { return getter(); }
        catch (Exception exception) { throw new TargetInvocationException(exception); }
      }

      public override void SetTypedValue(object instance, TValue value)
      {
        try { setter(value); }
        catch (Exception exception) { throw new TargetInvocationException(exception); }
      }
    }

    private sealed class ReflectionAccessor : PropertyAccessor
    {
      private readonly string fallbackReason;
      internal ReflectionAccessor(MethodInfo method, string reason) : base(method) =>
        fallbackReason = reason;
      internal override bool UsesReflection => true;
      internal override string FallbackReason => fallbackReason;
      internal override object GetValue(object instance) => Method.Invoke(instance, null);
      internal override void SetValue(object instance, object value) =>
        Method.Invoke(instance, new[] { value });
    }
  }
}
