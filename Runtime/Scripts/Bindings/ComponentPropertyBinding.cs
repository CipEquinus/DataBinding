using System;
using System.Reflection;
using UnityEngine;

namespace JH.DataBinding
{
  [Serializable]
  public class ComponentPropertyBinding
  {
    [SerializeField]
    [BindingSourcePath]
    private string sourcePath;

    [SerializeField]
    private GameObject targetGameObject;

    [SerializeField]
    [BindingTargetComponent]
    private Component targetComponent;

    [SerializeField]
    [BindingTargetPath]
    private string targetPath;

    private object dataSource;

    private MethodInfo[] dataSourcePropertyAccessors = Array.Empty<MethodInfo>();
    private MethodInfo[] targetPropertyAccessors = Array.Empty<MethodInfo>();
    private bool targetAccessorsPrepared;
    private Action configurationChanged;

    internal void ObserveConfiguration(Action onConfigurationChanged) =>
      configurationChanged = onConfigurationChanged;

    internal void SetPreparedDataSource(object source) => dataSource = source;

    internal void BindPrepared(object source, MethodInfo[] sourceAccessors, MethodInfo[] targetAccessors)
    {
      dataSource = source;
      dataSourcePropertyAccessors = sourceAccessors;
      targetPropertyAccessors = targetAccessors;
      targetAccessorsPrepared = true;
    }

    public object DataSource
    {
      get => dataSource;
      set
      {
        if (dataSource != value)
        {
          dataSource = value;

          BindSource();
        }
      }
    }

    public string SourcePath
    {
      get => sourcePath;
      set
      {
        if (sourcePath != value)
        {
          sourcePath = value;
          configurationChanged?.Invoke();

          BindSource();
        }
      }
    }

    public GameObject TargetGameObject
    {
      get => targetGameObject;
      set
      {
        if (targetGameObject != value)
        {
          targetGameObject = value;
          targetComponent = null;
          configurationChanged?.Invoke();

          BindTarget();
        }
      }
    }
    public Component TargetComponent
    {
      get => targetComponent;
      set
      {
        if (targetComponent != value)
        {
          targetComponent = value;
          targetGameObject = value != null ? value.gameObject : null;
          configurationChanged?.Invoke();

          BindTarget();
        }
      }
    }

    public string TargetPath
    {
      get => targetPath;
      set
      {
        if (targetPath != value)
        {
          targetPath = value;
          configurationChanged?.Invoke();

          BindTarget();
        }
      }
    }

    private void BindSource()
    {
      if (dataSource != null && !String.IsNullOrEmpty(sourcePath))
      {
        dataSourcePropertyAccessors = RuntimeReflectionOperations.Instance
          .ResolvePublicPropertyPath(dataSource, SourcePath, PathResolveOperation.GetValue);
      }
      else
      {
        dataSourcePropertyAccessors = Array.Empty<MethodInfo>();
      }
    }

    private void BindTarget()
    {
      targetAccessorsPrepared = false;
      if (targetComponent != null && !String.IsNullOrEmpty(targetPath))
      {
        targetPropertyAccessors = RuntimeReflectionOperations.Instance
          .ResolvePublicPropertyPath(targetComponent, targetPath, PathResolveOperation.SetValue);
      }
      else
      {
        targetPropertyAccessors = Array.Empty<MethodInfo>();
      }
    }

    public void UpdateBinding()
    {
      if (dataSourcePropertyAccessors.Length == 0)
      {
        return;
      }

      if (targetPropertyAccessors.Length == 0 && !targetAccessorsPrepared)
      {
        BindTarget();
      }

      if (targetPropertyAccessors.Length == 0)
      {
        return;
      }

      IReflectionOperations.SyncValue(
        RuntimeReflectionOperations.Instance,
        dataSource,
        targetComponent,
        dataSourcePropertyAccessors,
        targetPropertyAccessors
      );
    }
  }
}
