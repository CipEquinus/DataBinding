using System;
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

    private PropertyAccessorChain dataSourcePropertyAccessors = PropertyAccessorChain.Empty;
    private PropertyAccessorChain targetPropertyAccessors = PropertyAccessorChain.Empty;
    private Action<object, object> transferValue;

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
          targetGameObject = value.gameObject;

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

          BindTarget();
        }
      }
    }

    internal void ApplyPlan(object source, PropertyAccessorChain getter,
      PropertyAccessorChain setter, Action<object, object> transfer)
    {
      dataSource = source;
      dataSourcePropertyAccessors = getter;
      targetPropertyAccessors = setter;
      transferValue = transfer;
    }

    private void BindSource()
    {
      if (dataSource != null && !String.IsNullOrEmpty(sourcePath))
      {
        dataSourcePropertyAccessors = RuntimeReflectionOperations.Instance
          .ResolvePropertyAccessors(dataSource, SourcePath, PathResolveOperation.GetValue);
      }
      else
      {
        dataSourcePropertyAccessors = PropertyAccessorChain.Empty;
      }

      BindTransfer();
    }

    private void BindTarget()
    {
      if (targetComponent != null && !String.IsNullOrEmpty(targetPath))
      {
        targetPropertyAccessors = RuntimeReflectionOperations.Instance
          .ResolvePropertyAccessors(targetComponent, targetPath, PathResolveOperation.SetValue);
      }
      else
      {
        targetPropertyAccessors = PropertyAccessorChain.Empty;
      }

      BindTransfer();
    }

    private void BindTransfer()
    {
      transferValue = dataSourcePropertyAccessors.Length > 0 && targetPropertyAccessors.Length > 0
        ? dataSourcePropertyAccessors.GetTransferTo(targetPropertyAccessors)
        : null;
    }

    public void UpdateBinding()
    {
      if (dataSourcePropertyAccessors.Length == 0)
      {
        return;
      }

      if (targetPropertyAccessors.Length == 0)
      {
        BindTarget();
      }

      if (targetPropertyAccessors.Length == 0)
      {
        return;
      }

      transferValue(dataSource, targetComponent);
    }
  }
}
